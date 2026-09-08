// Local orchestration (ADR 34): one command boots Postgres, the WorkOS
// emulator, the API, and a worker instance of the same project (the role
// flag), with the Aspire dashboard as the OTLP sink (ADR 33). Local dev runs
// the REAL WorkOS adapter (ADR 14) against @workos/emulate - the local
// provider remains for bare `dotnet run` and the test suites.
using LocIntel.Platform.Data;

var builder = DistributedApplication.CreateBuilder(args);

// LOCINTEL_AUTH=local boots WITHOUT the WorkOS emulator, using the local auth
// provider - the same one the test suites use. A browser smoke run can then
// sign in by hint alone, and an automated smoke should never be typing
// credentials into a login form. The default stays "workos" so ordinary dev
// keeps exercising the real adapter against the emulator (ADR 14 parity).
var localAuth = string.Equals(
    Environment.GetEnvironmentVariable("LOCINTEL_AUTH"),
    "local",
    StringComparison.OrdinalIgnoreCase
);

var postgresServer = builder
    .AddPostgres("postgres")
    // PostGIS (ADR 50), the same pinned multi-arch image the tests and scripts use
    .WithImage(PostgresImage.Repository, PostgresImage.Tag)
    .WithImageSHA256(PostgresImage.Sha256)
    .WithDataVolume("locintel-pgdata");
var postgres = postgresServer.AddDatabase("locintel");

// LOCINTEL_PGBOUNCER=1 puts a PgBouncer container between api/worker and
// Postgres, the pooler production runs (docs/production.md): session mode,
// because the RLS session variable and Wolverine's advisory locks are
// connection state (ADR 52). The migrate role keeps talking to Postgres
// directly. Off by default so the ordinary dev boot is unchanged.
var pooled = string.Equals(
    Environment.GetEnvironmentVariable("LOCINTEL_PGBOUNCER"),
    "1",
    StringComparison.Ordinal
);
var pgbouncer = pooled
    ? builder
        .AddContainer("pgbouncer", "edoburu/pgbouncer", "v1.24.1-p1")
        .WithEndpoint(targetPort: 5432, scheme: "tcp", name: "tcp")
        .WithEnvironment("DB_HOST", "postgres")
        .WithEnvironment("DB_PORT", "5432")
        .WithEnvironment("DB_USER", "postgres")
        .WithEnvironment("DB_PASSWORD", postgresServer.Resource.PasswordParameter)
        .WithEnvironment("DB_NAME", "locintel")
        .WithEnvironment("AUTH_TYPE", "scram-sha-256")
        .WithEnvironment("AUTH_USER", "postgres")
        .WithEnvironment("AUTH_QUERY", "SELECT usename, passwd FROM pg_shadow WHERE usename=$1")
        .WithEnvironment("POOL_MODE", "session")
        .WithEnvironment("MAX_CLIENT_CONN", "1000")
        .WithEnvironment("DEFAULT_POOL_SIZE", "20")
        .WithEnvironment("IGNORE_STARTUP_PARAMETERS", "extra_float_digits,search_path")
        .WaitFor(postgresServer)
    : null;

// Runtime roles never receive the owner connection string, even in development.
var databaseEndpoint = pgbouncer?.GetEndpoint("tcp") ?? postgresServer.GetEndpoint("tcp");
var appConnection = ReferenceExpression.Create(
    $"Host={databaseEndpoint.Property(EndpointProperty.Host)};Port={databaseEndpoint.Property(EndpointProperty.Port)};Database=locintel;Username=app_user;Password=app_user"
);

var workos = localAuth
    ? null
    : builder
        .AddContainer("workos", "ghcr.io/workos/emulate", "latest")
        .WithHttpEndpoint(port: 4100, targetPort: 4100)
        .WithBindMount(
            "../../workos-emulate.config.yaml",
            "/app/workos-emulate.config.yaml",
            isReadOnly: true
        )
        .WithArgs("--host", "0.0.0.0", "--interactive"); // serve real login pages

var workosEndpoint = workos?.GetEndpoint("http");

// The migrate role (ADR 38): owner credentials, applies migrations,
// provisions the app role, exits. api/worker wait for it to COMPLETE and
// then connect as the unprivileged app_user - never as the owner.
var migrate = builder
    .AddProject<Projects.LocIntel_Api>("migrate", launchProfileName: null)
    // no launch profile -> no environment -> Production, where the local
    // auth provider is (rightly) refused; this is dev orchestration
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:0")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithEnvironment("ROLE", "migrate");

var apiBuilder = builder
    .AddProject<Projects.LocIntel_Api>("api")
    .WithEnvironment("ConnectionStrings__locintel", appConnection)
    .WaitForCompletion(migrate)
    .WithEnvironment("ROLE", "api")
    .WithEnvironment("Database__AppUser", "app_user")
    .WithEnvironment("Database__AppPassword", "app_user");
if (pgbouncer is not null)
    apiBuilder = apiBuilder.WaitFor(pgbouncer);

if (workos is not null && workosEndpoint is not null)
    apiBuilder = apiBuilder
        .WaitFor(workos)
        .WithEnvironment("Auth__Provider", "workos")
        .WithEnvironment("Auth__WorkOS__ApiKey", "sk_test_default")
        .WithEnvironment("Auth__WorkOS__ClientId", "client_locintel_dev")
        .WithEnvironment("Auth__WorkOS__ApiBaseUrl", workosEndpoint);
else
    apiBuilder = apiBuilder.WithEnvironment("Auth__Provider", "local");

// WaitFor(api) waits for HEALTHY: 503 until dev bootstrap finishes
var api = apiBuilder.WithHttpHealthCheck("/healthz");

builder
    .AddNpmApp("console", "../../web/apps/console", "dev")
    // unproxied: executables cannot be proxied onto their own target port;
    // vite binds 5173 directly and reads it from PORT
    .WithHttpEndpoint(env: "PORT", port: 5173, isProxied: false)
    .WithEnvironment("LOCINTEL_API", api.GetEndpoint("http"))
    .WaitFor(api);

builder
    .AddNpmApp("public", "../../web/apps/public", "dev")
    .WithHttpEndpoint(env: "PORT", port: 5174, isProxied: false)
    .WithEnvironment("LOCINTEL_API", api.GetEndpoint("http"))
    .WaitFor(api);

// launchProfileName: null - the worker must NOT inherit launchSettings'
// http port, or it races the api for 5293 and every API path 404s
// (whichever resource registers first wins the proxy).
var workerBuilder = builder
    .AddProject<Projects.LocIntel_Api>("worker", launchProfileName: null)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:0")
    .WithEnvironment("ConnectionStrings__locintel", appConnection)
    .WaitForCompletion(migrate)
    .WithEnvironment("Database__AppUser", "app_user")
    .WithEnvironment("Database__AppPassword", "app_user")
    .WithEnvironment("ROLE", "worker");
if (pgbouncer is not null)
    workerBuilder.WaitFor(pgbouncer);

builder.Build().Run();
