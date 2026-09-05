using System.Reflection;
using JasperFx;
using LocIntel.Api;
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
using LocIntel.Platform.Messaging;
using LocIntel.Platform.Notifications;
using LocIntel.Platform.Storage;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Wolverine;
using Wolverine.Http;
using Wolverine.Postgresql;
using static LocIntel.Api.ProviderOptionsValidation;

var builder = WebApplication.CreateBuilder(args);

// Scope validation ALWAYS (not just Development): singleton captures of
// scoped services are tenant-leak bugs; fail on them in every environment.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = false; // Wolverine registers open generics that trip build validation
});

// Role flag (ADR 34): one image, run as "migrate", "api" or "worker". An
// unknown value used to start a host that mapped nothing and swept nothing
// - a typo in a manifest looked like a healthy process. Refused instead.
var role = builder.Configuration["ROLE"] ?? "api";
if (role is not ("migrate" or "api" or "worker"))
    throw new InvalidOperationException(
        $"ROLE '{role}' is not a role this image runs; use migrate, api or worker (ADR 34)."
    );

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

// Independent EF, messaging, and direct-connection pools share one server budget.
// Bound the default before registering any consumer; native connection-string
// overrides remain authoritative for deployments with a different replica budget.
if (builder.Configuration.GetConnectionString("locintel") is { } databaseConnection)
{
    var settings = new Npgsql.NpgsqlConnectionStringBuilder(databaseConnection);
    var explicitSettings = new System.Data.Common.DbConnectionStringBuilder
    {
        ConnectionString = settings.ConnectionString,
    };
    if (!explicitSettings.ContainsKey("Maximum Pool Size"))
        settings.MaxPoolSize = Math.Max(20, settings.MinPoolSize);
    builder.Configuration["ConnectionStrings:locintel"] = settings.ConnectionString;
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

builder.AddAuthenticationHosting(role);

builder.Services.AddTenancyModule(runBackgroundWork: role == "worker");
builder.Services.AddIdentityModule();
builder.Services.AddEntitlementsModule(runBackgroundWork: role == "worker");
builder.Services.AddAuditModule(runBackgroundWork: role == "worker");
builder.Services.AddStorageModule(runBackgroundWork: role == "worker");
builder.Services.AddIngestModule(runBackgroundWork: role == "worker");
builder.Services.AddChecklistsModule();

// Platform infra context (idempotency, ADR 29; sweep leases)
builder.Services.AddScoped<ISweepLease, SweepLease>(); // by TYPE: Wolverine codegen refuses factories
builder.Services.AddModuleDbContext<PlatformDbContext>("platform", audited: false); // by TYPE, not a factory: a handler that takes this context (CleanupIdempotencyHandler) is refused by Wolverine codegen otherwise
if (role == "worker")
    builder.Services.AddHostedService<IdempotencyCleanupService>();

var storageProvider = builder.AddStorageHosting();

// Billing (ADR 39): local provider is DEV/TEST ONLY - Production must boot a
// real one (StripeBillingProvider in LocIntel.Integrations.Stripe, or a fork's).
switch (builder.Configuration["Billing:Provider"] ?? "local")
{
    case "stripe":
        builder
            .Services.AddOptions<LocIntel.Integrations.Stripe.StripeOptions>()
            .Bind(builder.Configuration.GetSection("Billing:Stripe"))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ApiKey),
                "Billing:Stripe:ApiKey is required."
            )
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.WebhookSecret),
                "Billing:Stripe:WebhookSecret is required."
            )
            .Validate(
                o => IsHttpUrl(o.ApiBase),
                "Billing:Stripe:ApiBase must be an absolute HTTP(S) URL."
            )
            .Validate(
                o =>
                    LocIntel.Platform.Billing.PlanCatalog.Plans.All(plan =>
                        o.PriceIds.TryGetValue(plan.Id, out var priceId)
                        && !string.IsNullOrWhiteSpace(priceId)
                    ),
                "Billing:Stripe:PriceIds must contain every PlanCatalog plan."
            )
            .ValidateOnStart();
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
        builder
            .Services.AddOptions<LocIntel.Integrations.Smtp.SmtpOptions>()
            .Bind(builder.Configuration.GetSection("Notifications:Smtp"))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.Host),
                "Notifications:Smtp:Host is required."
            )
            .Validate(
                o => o.Port is >= 1 and <= 65535,
                "Notifications:Smtp:Port must be between 1 and 65535."
            )
            .Validate(
                o =>
                    MimeKit.MailboxAddress.TryParse(o.FromAddress, out var address)
                    && address.Address.Contains('@')
                    && address.Address == o.FromAddress,
                "Notifications:Smtp:FromAddress must be a valid email address."
            )
            .Validate(
                o => CredentialsMatch(o.UserName, o.Password),
                "Notifications:Smtp:UserName and Password must be configured together."
            )
            .ValidateOnStart();
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

builder.AddRequestPolicies();

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
    // The image build runs `codegen write` first (checks.yml, image job), so
    // Production loads the pre-generated handler code instead of generating
    // and compiling it on every boot. Auto rather than Static: a fork that
    // publishes without the codegen step still boots (generating at start,
    // as in dev) instead of dying with a stale-cache error. Dev stays Dynamic.
    if (builder.Environment.IsProduction())
        opts.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Auto;
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
builder.Services.AddSingleton(sp => new ReadinessState(
    ready: !bootstraps,
    sp.GetRequiredService<IRegionDataSources>(),
    sp.GetRequiredService<Wolverine.Runtime.IWolverineRuntime>()
));
if (bootstraps)
    builder.Services.AddHostedService<DevBootstrap>(); // seed for `aspire run` (migrations: the migrate role)

var app = builder.Build();

if (role == "api")
{
    app.UseRequestPolicies();
    if (storageProvider == "local")
        app.MapLocalObjectStore(); // the in-process ticket endpoints exist only for the local adapter

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
}

// Probes for EVERY role (the worker had none, and the guide told operators
// to wire /healthz to the deployed process). /livez answers as soon as the
// process serves requests: liveness, restart if it stops. /healthz is
// readiness: 503 until the role's dependencies are usable (in Development
// the api's bootstrap flips it after migrations and seed), take the pod out
// of rotation while it is not.
app.MapGet(
        "/livez",
        () =>
            Results.Ok(
                new
                {
                    status = "alive",
                    role,
                    version = buildVersion,
                }
            )
    )
    .ExcludeFromDescription();
app.MapGet(
        "/healthz",
        async (ReadinessState readiness, CancellationToken ct) =>
        {
            var ready = await readiness.DependenciesReadyAsync(role, ct);
            return ready
                ? Results.Ok(new HealthResponse("ok", role, buildVersion))
                : Results.Json(
                    new HealthResponse(
                        readiness.Ready ? "unhealthy" : "starting",
                        role,
                        buildVersion
                    ),
                    statusCode: StatusCodes.Status503ServiceUnavailable
                );
        }
    )
    .Produces<HealthResponse>(); // described: it is in the published contract snapshot; /livez is a probe only

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

public sealed record HealthResponse(string Status, string Role, string Version);
