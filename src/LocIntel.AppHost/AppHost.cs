// Local orchestration (ADR 34): one command boots Postgres, the WorkOS
// emulator, the API, and a worker instance of the same project (the role
// flag), with the Aspire dashboard as the OTLP sink (ADR 33). Local dev runs
// the REAL WorkOS adapter (ADR 14) against @workos/emulate - the local
// provider remains for bare `dotnet run` and the test suites.
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("postgres")
    .WithDataVolume("locintel-pgdata")
    .AddDatabase("locintel");

var workos = builder
    .AddContainer("workos", "ghcr.io/workos/emulate", "latest")
    .WithHttpEndpoint(port: 4100, targetPort: 4100)
    .WithBindMount(
        "../../workos-emulate.config.yaml",
        "/app/workos-emulate.config.yaml",
        isReadOnly: true
    )
    .WithArgs("--host", "0.0.0.0", "--interactive"); // serve real login pages

var workosEndpoint = workos.GetEndpoint("http");

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

var api = builder
    .AddProject<Projects.LocIntel_Api>("api")
    .WithReference(postgres)
    .WaitForCompletion(migrate)
    .WaitFor(workos)
    .WithEnvironment("ROLE", "api")
    .WithEnvironment("Database__AppUser", "app_user")
    .WithEnvironment("Database__AppPassword", "app_user")
    .WithEnvironment("Auth__Provider", "workos")
    .WithEnvironment("Auth__WorkOS__ApiKey", "sk_test_default")
    .WithEnvironment("Auth__WorkOS__ClientId", "client_locintel_dev")
    .WithEnvironment("Auth__WorkOS__ApiBaseUrl", workosEndpoint)
    // WaitFor(api) waits for HEALTHY: 503 until dev bootstrap finishes
    .WithHttpHealthCheck("/healthz");

builder
    .AddNpmApp("console", "../../web/apps/console", "dev")
    // unproxied: executables cannot be proxied onto their own target port;
    // vite binds 5173 directly and reads it from PORT
    .WithHttpEndpoint(env: "PORT", port: 5173, isProxied: false)
    .WithEnvironment("PREMISE_API", api.GetEndpoint("http"))
    .WaitFor(api);

builder
    .AddNpmApp("public", "../../web/apps/public", "dev")
    .WithHttpEndpoint(env: "PORT", port: 5174, isProxied: false)
    .WithEnvironment("PREMISE_API", api.GetEndpoint("http"))
    .WaitFor(api);

// launchProfileName: null - the worker must NOT inherit launchSettings'
// http port, or it races the api for 5293 and every API path 404s
// (whichever resource registers first wins the proxy).
builder
    .AddProject<Projects.LocIntel_Api>("worker", launchProfileName: null)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:0")
    .WithReference(postgres)
    .WaitForCompletion(migrate)
    .WithEnvironment("Database__AppUser", "app_user")
    .WithEnvironment("Database__AppPassword", "app_user")
    .WithEnvironment("ROLE", "worker");

builder.Build().Run();
