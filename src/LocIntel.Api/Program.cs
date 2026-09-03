using System.Globalization;
using System.Reflection;
using System.Threading.RateLimiting;
using JasperFx;
using LocIntel.Api;
using LocIntel.Integrations.WorkOS;
using LocIntel.Modules.Audit;
using LocIntel.Modules.Checklists;
using LocIntel.Modules.Entitlements;
using LocIntel.Modules.Identity;
using LocIntel.Modules.Identity.Auth;
using LocIntel.Modules.Ingest;
using LocIntel.Modules.Storage;
using LocIntel.Modules.Tenancy;
using LocIntel.Platform.Audit;
using LocIntel.Platform.Auth;
using LocIntel.Platform.Data;
using LocIntel.Platform.Infra;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Notifications;
using LocIntel.Platform.Secrets;
using LocIntel.Platform.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Wolverine;
using Wolverine.Http;
using Wolverine.Postgresql;

var builder = WebApplication.CreateBuilder(args);

// Scope validation ALWAYS (not just Development): singleton captures of
// scoped services are tenant-leak bugs; fail on them in every environment.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = false; // Wolverine registers open generics that trip build validation
});

// Role flag (ADR 34): one image, run as "api" or "worker".
var role = builder.Configuration["ROLE"] ?? "api";

// "What version are you running?" must be answerable (maturity review,
// hole 4): CI stamps Build:Version (see docs/production.md); local builds
// fall back to the assembly's informational version.
var buildVersion =
    builder.Configuration["Build:Version"]
    ?? System
        .Reflection.Assembly.GetExecutingAssembly()
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion
    ?? "dev";

// The role split (ADR 38): api and worker connect as the unprivileged app
// role; only the migrate role keeps the owner credentials the orchestrator
// hands out. Rewritten HERE so every consumer of the connection string -
// region sources, Wolverine, middleware - sees the same identity.
if (role != "migrate" && builder.Configuration["Database:AppUser"] is { Length: > 0 } appUser)
{
    var ownerCs =
        builder.Configuration.GetConnectionString("locintel")
        ?? throw new InvalidOperationException("Missing connection string 'locintel'.");
    builder.Configuration["ConnectionStrings:locintel"] = new Npgsql.NpgsqlConnectionStringBuilder(
        ownerCs
    )
    {
        Username = appUser,
        Password = builder.Configuration["Database:AppPassword"],
    }.ConnectionString;
}

// Observability (ADR 33): OTLP only - the standard OTEL_EXPORTER_OTLP_*
// env vars point it anywhere (the Aspire dashboard in dev, any collector in
// prod). Tenant/site/actor ride traces and logs as baggage, NEVER metric
// labels. Wolverine publishes its own activity source.
builder
    .Services.AddOpenTelemetry()
    .ConfigureResource(r =>
        r.AddService(serviceName: $"locintel-{role}", serviceInstanceId: Environment.MachineName)
    )
    .WithTracing(tracing =>
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("Wolverine")
            .AddOtlpExporter()
    )
    .WithMetrics(metrics =>
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter("Wolverine:*")
            .AddOtlpExporter()
    );
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeScopes = true;
    logging.AddOtlpExporter();
});

// No ambient connection string (ADR 35): everything resolves through the
// region seam, single-region in v1.
builder.Services.AddSingleton<IRegionDataSources, SingleRegionDataSources>();

// Principals (ADR 7): read-time resolution, usable from any scope.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IPrincipalAccessor, RequestPrincipalAccessor>();
builder.Services.AddScoped<TenantContext>(); // envelope-tenant holder (ADR 24)
builder.Services.AddScoped<ITenantContext, PrincipalTenantContext>();
builder.Services.AddSingleton(TimeProvider.System);

// Gates 2+3: roles compile to grants; scope evaluated per request (ADR 6),
// decorated with authz-decision audit (ADR 12: denials always).
builder.Services.AddScoped<LocIntel.Modules.Identity.Access.GrantScopeResolver>();
builder.Services.AddScoped<IScopeResolver, AuditedScopeResolver>();
builder.Services.AddSingleton<AuditPolicyCache>();
builder.Services.AddSingleton<AuditSaveChangesInterceptor>();

// Auth seam (ADR 14): provider selected by config; WorkOS is the built-in
// full-capability implementation, local is the dev/test base implementation.
var authProvider = builder.Configuration["Auth:Provider"] ?? "local";
switch (authProvider)
{
    case "workos":
        builder.Services.Configure<WorkOSOptions>(builder.Configuration.GetSection("Auth:WorkOS"));
        builder.Services.AddSingleton<IAuthProvider, WorkOSAuthProvider>();
        break;
    case "local" when !builder.Environment.IsProduction():
        builder.Services.AddSingleton<IAuthProvider, LocalAuthProvider>();
        break;
    default:
        throw new InvalidOperationException(
            $"Auth:Provider '{authProvider}' is not valid for {builder.Environment.EnvironmentName}. "
                + "Use 'workos' in Production; 'local' is dev/test only (ADR 14)."
        );
}

// Data protection (security review): the keyring protects auth-ticket
// cookies AND contact magic-link tokens. The framework default is a
// per-process filesystem keyring, unencrypted - which means (a) across
// REPLICAS a cookie/token minted by one instance cannot be read by
// another (broken sessions and dead magic links behind a load balancer),
// and (b) keys vanish on a fresh container. A shared, protected store is
// therefore REQUIRED in any multi-replica deployment.
//
// The application name is pinned so a shared ring is unambiguous; the
// persistence directory (a mounted volume or network path all replicas
// share) is config-driven, and Production REFUSES to boot on the
// ephemeral default rather than silently breaking sessions after the
// first scale-out. Forks on a cloud should point this at a blob/secret
// store and wrap it with their KMS (see docs/production.md).
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("locintel");
if (builder.Configuration["DataProtection:KeyPath"] is { Length: > 0 } keyPath)
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
else if (builder.Environment.IsProduction() && role != "migrate")
    throw new InvalidOperationException(
        "DataProtection:KeyPath is required in Production (a store all replicas share); "
            + "the default per-process keyring breaks sessions and magic links after scale-out."
    );

// Cookie session (ADR 21): HttpOnly, no token ever reachable from JS.
builder
    .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "locintel_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Production is a hard floor: Always, so a fork that forgets to
        // trust its proxy's X-Forwarded-Proto gets broken logins (loud)
        // instead of session cookies over plain HTTP (silent). Elsewhere
        // SameAsRequest keeps http://localhost working.
        options.Cookie.SecurePolicy = builder.Environment.IsProduction()
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        // API, not a browser app: never redirect to a login page.
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = 403;
            return Task.CompletedTask;
        };
    });

builder.Services.AddTenancyModule(runBackgroundWork: role == "worker");
builder.Services.AddIdentityModule();
builder.Services.AddEntitlementsModule(runBackgroundWork: role == "worker");
builder.Services.AddAuditModule(runBackgroundWork: role == "worker");
builder.Services.AddStorageModule(runBackgroundWork: role == "worker");
builder.Services.AddIngestModule(runBackgroundWork: role == "worker");
builder.Services.AddChecklistsModule();

// Platform infra context (idempotency, ADR 29)
builder.Services.AddDbContext<PlatformDbContext>(
    (sp, options) =>
    {
        // singleton options: default region only (see module registrations / ADR 35)
        var regions = sp.GetRequiredService<IRegionDataSources>();
        options
            .UseNpgsql(
                regions.For(RegionId.Default),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "platform")
            )
            .AddInterceptors(TenantSessionInterceptor.Instance);
    }
);
if (role == "worker")
    builder.Services.AddHostedService<IdempotencyCleanupService>();

// Object storage (ADR 19): local adapter by default; forks swap in
// S3ObjectStore (LocIntel.Integrations.AmazonS3), AzureBlobObjectStore
// (LocIntel.Integrations.AzureBlob), or their own - both cloud adapters are
// smoke-tested against MinIO/Azurite in the integration suite.
builder.Services.AddSingleton<IObjectStore, LocalObjectStore>();
builder.Services.AddSingleton<IVirusScanner, EicarScanner>();

// Secrets (ADR 31): local wrapper is DEV/TEST ONLY - Production must boot a
// KMS (KmsKeyWrapper in LocIntel.Integrations.AmazonS3, LocalStack-tested).
if (builder.Configuration["Secrets:LocalMasterKey"] is { } localKey)
{
    if (builder.Environment.IsProduction())
        throw new InvalidOperationException(
            "LocalKeyWrapper is dev/test only (ADR 31); configure a cloud KMS adapter."
        );
    builder.Services.AddSingleton<IKeyWrapper>(
        new LocalKeyWrapper(Convert.FromBase64String(localKey))
    );
}

// Billing (ADR 39): local provider is DEV/TEST ONLY - Production must boot a
// real one (StripeBillingProvider in LocIntel.Integrations.Stripe, or a fork's).
switch (builder.Configuration["Billing:Provider"] ?? "local")
{
    case "stripe":
        builder.Services.Configure<LocIntel.Integrations.Stripe.StripeOptions>(
            builder.Configuration.GetSection("Billing:Stripe")
        );
        builder.Services.AddSingleton<
            LocIntel.Platform.Billing.IBillingProvider,
            LocIntel.Integrations.Stripe.StripeBillingProvider
        >();
        break;
    case "local" when !builder.Environment.IsProduction():
        builder.Services.AddSingleton<LocIntel.Platform.Billing.IBillingProvider>(
            new LocIntel.Modules.Entitlements.LocalBillingProvider(
                builder.Configuration["Billing:WebhookSecret"] ?? "dev-billing-secret"
            )
        );
        break;
    default:
        throw new InvalidOperationException(
            "Billing:Provider 'local' is dev/test only (ADR 39); configure 'stripe' or a fork adapter in Production."
        );
}

builder.Services.AddWolverineHttp();
builder.Services.AddOpenApi(); // ADR 16: the spec is the contract; TS client + keys generate from it

// Notifications (ADR 32): email is on the auth critical path (magic links),
// so Production must configure a real transport - the built-in SMTP adapter
// reaches every mainstream provider, forks add vendor SDKs behind the port.
// SMS egress (a seam, not a feature): "off" is the default and is safe in
// Production - a fork that needs texting registers its own adapter here.
switch (builder.Configuration["Notifications:Sms"] ?? "off")
{
    case "off":
        builder.Services.AddSingleton<ISmsTransport, NoSmsTransport>();
        break;
    case "local" when !builder.Environment.IsProduction():
        builder.Services.AddSingleton<LocalSmsCatcher>();
        builder.Services.AddSingleton<ISmsTransport>(sp =>
            sp.GetRequiredService<LocalSmsCatcher>()
        );
        break;
    default:
        throw new InvalidOperationException(
            "Notifications:Sms 'local' is dev/test only; use 'off' or a fork adapter in Production."
        );
}

switch (builder.Configuration["Notifications:Transport"] ?? "local")
{
    case "smtp":
        builder.Services.Configure<LocIntel.Integrations.Smtp.SmtpOptions>(
            builder.Configuration.GetSection("Notifications:Smtp")
        );
        builder.Services.AddSingleton<LocIntel.Integrations.Smtp.SmtpNotificationTransport>();
        builder.Services.AddSingleton<
            INotificationTransport,
            LocIntel.Modules.Identity.Users.SuppressingNotificationTransport<LocIntel.Integrations.Smtp.SmtpNotificationTransport>
        >();
        break;
    case "local" when !builder.Environment.IsProduction():
        builder.Services.AddSingleton<LocalMailCatcher>();
        builder.Services.AddSingleton<
            INotificationTransport,
            LocIntel.Modules.Identity.Users.SuppressingNotificationTransport<LocalMailCatcher>
        >();
        break;
    default:
        throw new InvalidOperationException(
            "Notifications:Transport 'local' is dev/test only (ADR 32); configure 'smtp' or a fork adapter in Production."
        );
}

// Rate limiting (ADR 30): partitioned by principal tier. Guests limit on
// their session cookie (fallback: IP), users on user id. The per-org quota
// reading metered entitlements attaches in step 4.
var guestLimit = builder.Configuration.GetValue("RateLimits:GuestPerMinute", 60);
var userLimit = builder.Configuration.GetValue("RateLimits:UserPerMinute", 300);
builder.Services.AddSingleton<OrgRateLimitCache>();
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // consumers deserve to know when to come back: fixed one-minute windows,
    // so the limiter's own retry hint (when present) or the window size
    limiter.OnRejected = (context, _) =>
    {
        var seconds = context.Lease.TryGetMetadata(
            System.Threading.RateLimiting.MetadataName.RetryAfter,
            out var retryAfter
        )
            ? Math.Max(1, (int)retryAfter.TotalSeconds)
            : 60;
        context.HttpContext.Response.Headers.RetryAfter = seconds.ToString();
        return ValueTask.CompletedTask;
    };
    limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        // ADR 30: org-level quota from the metered entitlement, over the per-principal limiter
        PartitionedRateLimiter.Create<HttpContext, string>(http =>
        {
            // ONE resolver for "who is this request": the same Principal the
            // endpoints see. This lambda used to re-parse claims and Items
            // itself, and the two readings drifted - API keys fell into the
            // per-IP guest bucket and skipped the org quota entirely.
            var principal = http.RequestServices.GetRequiredService<IPrincipalAccessor>().Current;
            OrgId? org = principal switch
            {
                Principal.User { ActiveOrg: { } active } => active,
                Principal.Service service => service.Org,
                Principal.Contact contact => contact.Org,
                _ => null,
            };
            if (org is { } quotaOrg)
            {
                var orgGuid = quotaOrg.Value;
                var orgLimit = http
                    .RequestServices.GetRequiredService<OrgRateLimitCache>()
                    .LimitFor(quotaOrg);
                // the limit is part of the KEY: partition limiters are
                // created once and cached, so a quota change must roll to a
                // fresh partition or a hot org keeps its old limit forever
                // (found by the load baseline)
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"org:{orgGuid}:{orgLimit}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = orgLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }
                );
            }
            return RateLimitPartition.GetNoLimiter("org:none");
        }),
        PartitionedRateLimiter.Create<HttpContext, string>(http =>
        {
            var (key, permits) = http
                .RequestServices.GetRequiredService<IPrincipalAccessor>()
                .Current switch
            {
                // an API key is a first-class principal (ADR 40): its own
                // bucket at the USER limit, never the per-IP guest bucket
                Principal.Service service => ($"key:{service.KeyId}", userLimit),
                Principal.User user => ($"user:{user.UserId}", userLimit),
                _ => http.Request.Cookies.TryGetValue(
                    GuestSessionMiddleware.CookieName,
                    out var guest
                )
                    ? ($"guest:{guest}", guestLimit)
                    : ($"ip:{http.Connection.RemoteIpAddress}", guestLimit),
            };
            return RateLimitPartition.GetFixedWindowLimiter(
                key,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permits,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            );
        })
    );
});

// Wolverine (ADR 23): mediation + messaging + durable Postgres outbox.
builder.UseWolverine(opts =>
{
    var cs =
        builder.Configuration.GetConnectionString("locintel")
        ?? throw new InvalidOperationException("Missing connection string 'locintel'.");
    opts.PersistMessagesWithPostgresql(cs, "wolverine");
    // the migrate role owns DDL, not messaging: never let it provision or
    // touch the envelope schema as the OWNER (the app role must own it)
    if (role == "migrate")
        opts.Durability.Mode = Wolverine.DurabilityMode.MediatorOnly;
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Discovery.IncludeAssembly(typeof(TenancyModule).Assembly);
    opts.Discovery.IncludeAssembly(typeof(IdentityModule).Assembly);
    opts.Discovery.IncludeAssembly(typeof(EntitlementsModule).Assembly);
    opts.Discovery.IncludeAssembly(typeof(AuditModule).Assembly);
    opts.Discovery.IncludeAssembly(typeof(StorageModule).Assembly);
    opts.Discovery.IncludeAssembly(typeof(IngestModule).Assembly);
    opts.Discovery.IncludeAssembly(typeof(LocIntel.Modules.Checklists.ChecklistsModule).Assembly);
});

if (role == "migrate")
    builder.Services.AddHostedService<MigrationRunner>(); // migrate, provision app role, exit

var bootstraps = builder.Environment.IsDevelopment() && role == "api";
builder.Services.AddSingleton(new ReadinessState(ready: !bootstraps));
if (bootstraps)
    builder.Services.AddHostedService<DevBootstrap>(); // seed for `aspire run` (migrations: the migrate role)

var app = builder.Build();

if (role == "api")
{
    // Behind the documented TLS-terminating proxy the request arrives as
    // HTTP: without this, cookies lose the Secure flag and every URL built
    // from Request.Scheme/Host (billing returns, SSO portal returns) comes
    // out http://. Opt-in because trusting these headers from an UNKNOWN
    // peer lets clients spoof scheme/host/ip - only enable it when the
    // immediate proxy strips inbound X-Forwarded-* (reverse proxies do).
    if (builder.Configuration.GetValue("Proxy:TrustForwardedHeaders", false))
    {
        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders =
                ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost,
        };
        forwarded.KnownIPNetworks.Clear(); // trust the immediate peer: the proxy
        forwarded.KnownProxies.Clear();
        app.UseForwardedHeaders(forwarded);
    }
    app.UseMiddleware<UnhandledErrorMiddleware>();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseMiddleware<PublicCacheMiddleware>();
    app.UseAuthentication();
    app.UseMiddleware<SessionValidationMiddleware>();
    app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
    app.UseMiddleware<CsrfOriginMiddleware>();
    app.UseMiddleware<GuestSessionMiddleware>();
    app.UseMiddleware<GuestOrgMiddleware>();
    app.UseRateLimiter();
    app.UseMiddleware<SuspensionMiddleware>();
    app.UseMiddleware<IdempotencyMiddleware>();
    app.UseMiddleware<AccessLogMiddleware>();
    app.MapLocalObjectStore();

    // The OpenAPI spec publishes the full API surface. It's on by default so
    // the console's developer page can link it, but a fork that treats its
    // API shape as non-public sets Api:ExposeOpenApi=false (or gates
    // /openapi at the proxy). The contract snapshot test hits the spec
    // in-process, so codegen is unaffected either way.
    if (builder.Configuration.GetValue("Api:ExposeOpenApi", true))
        app.MapOpenApi();
    if (!app.Environment.IsProduction())
        app.MapGet(
                "/dev/boom",
                new Func<IResult>(() =>
                    throw new InvalidOperationException("deliberate dev failure")
                )
            )
            .ExcludeFromDescription();

    if (app.Environment.IsDevelopment())
        // instant "checkout" for the local billing provider: applies the plan
        // as if the provider's webhook had fired, then returns to the console
        app.MapGet(
                "/billing/dev/complete",
                async (Guid org, string plan, string? returnUrl, Wolverine.IMessageBus bus) =>
                {
                    await bus.PublishAsync(
                        new LocIntel.Modules.Entitlements.BillingSubscriptionChanged(
                            plan,
                            LocIntel.Platform.Billing.SubscriptionStatus.Active,
                            $"local_cus_{org:N}",
                            $"local_sub_{org:N}",
                            DateTimeOffset.UtcNow.AddMonths(1)
                        ),
                        new Wolverine.DeliveryOptions { TenantId = org.ToString() }
                    );
                    // checkout hands the provider an ABSOLUTE success URL
                    // (Stripe requires one); redirect to its path only, which
                    // also forecloses open redirects
                    var path = Uri.TryCreate(returnUrl, UriKind.Absolute, out var absolute)
                        ? absolute.PathAndQuery
                        : returnUrl;
                    return Results.Redirect(
                        path is ['/', ..] && !path.StartsWith("//") ? path : "/"
                    );
                }
            )
            .ExcludeFromDescription();

    if (app.Environment.IsDevelopment())
        // caught mail (contact links, password resets) is otherwise trapped
        // in memory: this closes the dev loop. Never mapped outside dev.
        app.MapGet(
                "/dev/mail",
                // the catcher sits BEHIND the suppression decorator now, so
                // resolve the concrete type (absent when transport is smtp)
                (IServiceProvider sp) =>
                    sp.GetService<LocIntel.Platform.Notifications.LocalMailCatcher>() is { } catcher
                        ? Results.Ok(catcher.Sent)
                        : Results.NotFound()
            )
            .ExcludeFromDescription();
    app.MapIdentityEndpoints();
    app.MapAccountEndpoints();
    app.MapContactLinkEndpoints();
    app.MapOperatorDeadLetterEndpoints();
    app.MapOperatorOverviewEndpoint();
    app.MapOperatorHealthEndpoint();
    app.MapWolverineEndpoints();
    app.MapGet(
        "/healthz",
        (ReadinessState readiness) =>
            readiness.Ready
                ? Results.Ok(
                    new
                    {
                        status = "ok",
                        role,
                        version = buildVersion,
                    }
                )
                : Results.Json(
                    new
                    {
                        status = "starting",
                        role,
                        version = buildVersion,
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable
                )
    );
}

// JasperFx command line only when a command is given (design-debt close):
// `-- codegen write` really writes the generated handler code, so CI
// catches "fails at first request" codegen errors at build time. The
// no-args path stays plain app.Run() - WebApplicationFactory (the whole
// integration suite) hooks that and NOT the JasperFx runner.
// JasperFx command line only when a command is given (design-debt close):
// `-- codegen write` really writes the generated handler code, so CI
// catches "fails at first request" codegen errors at build time. Two
// hard-won constraint: under WebApplicationFactory (the whole integration
// suite) the entry point sees the TEST HOST's own arguments, so gating on
// "any args" sends every test through the JasperFx runner and nothing
// starts. Gate on the one command we actually use.
if (args is ["codegen", ..])
{
    _ = await app.RunJasperFxCommands(args);
    return;
}
app.Run();

// Exposed for WebApplicationFactory in the integration/isolation suites.
public partial class Program;
