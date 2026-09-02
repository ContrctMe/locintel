using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Modules.Audit.Data;
using LocIntel.Modules.Entitlements.Data;
using LocIntel.Modules.Identity.Access;
using LocIntel.Modules.Identity.Data;
using LocIntel.Modules.Identity.Users;
using LocIntel.Modules.Ingest.Data;
using LocIntel.Modules.Storage.Data;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Organizations;
using LocIntel.Platform.Infra;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Boots real Postgres (Testcontainers), applies every module's migrations
/// (RLS policies included), seeds two orgs and their users, and hosts the API
/// in-process with the local auth provider (ADR 14). Clients authenticate
/// through the REAL cookie flow (ADR 21) - login redirect, code exchange,
/// session cookie - not a header hack.
/// The app connects as a NON-superuser role: table owners bypass RLS unless
/// FORCEd, and superusers always do - testing as postgres would test nothing.
/// </summary>
public class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(
        "postgres:17-alpine"
    ).Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    private readonly string _storageRoot = Path.Combine(
        Path.GetTempPath(),
        "locintel-test-objects",
        Guid.NewGuid().ToString("N")
    );
    public string AppConnectionString { get; private set; } = "";
    public OrgId OrgA { get; } = OrgId.New();
    public OrgId OrgB { get; } = OrgId.New();
    public const string UserA = "user-a@locintel.local"; // member: OrgA
    public const string UserB = "user-b@locintel.local"; // member: OrgB
    public const string UserBoth = "user-ab@locintel.local"; // member: OrgA + OrgB
    public const string ViewerA = "viewer-a@locintel.local"; // member: OrgA, NO role
    public const string Operator = "operator@locintel.local"; // member: platform org
    public OrgId PlatformOrg { get; } = OrgId.New();

    private static LocIntel.Platform.Data.ModuleDbContext CreateCatalogContext(
        LocIntel.Platform.Modules.ModuleDescriptor module,
        string connectionString
    )
    {
        var build = typeof(ApiFixture)
            .GetMethod(
                nameof(CreateModuleContext),
                System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Static
            )!
            .MakeGenericMethod(module.DbContextType);
        return (LocIntel.Platform.Data.ModuleDbContext)
            build.Invoke(null, [connectionString, module.Schema])!;
    }

    public virtual async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminCs = _postgres.GetConnectionString();
        // migrate + grant straight from the module catalog, so a new module
        // needs no fixture edit (a fork found Checklists missing from lists
        // exactly like these)
        foreach (var module in LocIntel.Api.ModuleCatalog.AllWithPlatform)
        {
            await using var context = CreateCatalogContext(module, adminCs);
            await context.Database.MigrateAsync();
        }

        var grants = string.Join(
            "\n",
            LocIntel.Api.ModuleCatalog.Schemas.Select(schema =>
                $"GRANT USAGE ON SCHEMA {schema} TO app_user; "
                + $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {schema} TO app_user;"
            )
        );
        await _postgres.ExecScriptAsync(
            """
            CREATE ROLE app_user LOGIN PASSWORD 'app_user' NOSUPERUSER;
            -- Wolverine owns its envelope schema; the app creates it at startup
            GRANT CREATE ON DATABASE postgres TO app_user;
            {GRANTS}
            """.Replace("{GRANTS}", grants)
        );
        AppConnectionString = new Npgsql.NpgsqlConnectionStringBuilder(adminCs)
        {
            Username = "app_user",
            Password = "app_user",
        }.ConnectionString;
        var appCs = AppConnectionString;

        await using (var seed = CreateTenancyContext(adminCs))
        {
            seed.Organizations.AddRange(
                new Organization
                {
                    Id = OrgA,
                    Name = "Org A",
                    Slug = "org-a",
                    Region = RegionId.Default,
                },
                new Organization
                {
                    Id = OrgB,
                    Name = "Org B",
                    Slug = "org-b",
                    Region = RegionId.Default,
                },
                new Organization
                {
                    Id = PlatformOrg,
                    Name = "Platform Ops",
                    Slug = "platform-ops",
                    Region = RegionId.Default,
                    IsPlatform = true,
                }
            );
            seed.OrganizationSettings.AddRange(
                OrganizationSetting.Create(OrgA, "brand.color", "#B01458"),
                OrganizationSetting.Create(OrgB, "brand.color", "#0A6E8A")
            );
            await seed.SaveChangesAsync();
        }
        await using (var seed = CreateIdentityContext(adminCs))
        {
            var a = AppUser.Create("local", UserA, UserA, "User A");
            var b = AppUser.Create("local", UserB, UserB, "User B");
            var both = AppUser.Create("local", UserBoth, UserBoth, "User AB");
            var viewer = AppUser.Create("local", ViewerA, ViewerA, "Viewer A");
            var op = AppUser.Create("local", Operator, Operator, "Operator");
            seed.Users.AddRange(a, b, both, viewer, op);
            // Distinct join instants (real joins are never in the same
            // microsecond): "first joined" must MEAN something - UserBoth
            // joined OrgA before OrgB, so OrgA is their default org.
            var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
            Membership Join(Guid userId, OrgId org, int order) =>
                new()
                {
                    Id = Guid.CreateVersion7(),
                    UserId = userId,
                    OrgId = org,
                    CreatedAt = t0.AddSeconds(order),
                };
            var memberships = new[]
            {
                (Join(a.Id, OrgA, 0), true),
                (Join(b.Id, OrgB, 1), true),
                (Join(both.Id, OrgA, 2), true),
                (Join(both.Id, OrgB, 3), true),
                (Join(viewer.Id, OrgA, 4), false), // no role: gets nothing
                (Join(op.Id, PlatformOrg, 5), true),
            };
            seed.Memberships.AddRange(memberships.Select(m => m.Item1));
            // Owner (*:*) per org, assigned org-wide to the seeded members (ADR 6)
            var owners = new Dictionary<OrgId, Role>
            {
                [OrgA] = Role.Create(OrgA, "Owner"),
                [OrgB] = Role.Create(OrgB, "Owner"),
                [PlatformOrg] = Role.Create(PlatformOrg, "Operator"),
            };
            foreach (var (org, ownerRole) in owners)
            {
                seed.Roles.Add(ownerRole);
                seed.RoleGrants.Add(
                    new RoleGrant
                    {
                        Id = Guid.CreateVersion7(),
                        OrgId = org,
                        RoleId = ownerRole.Id,
                        Domain = "*",
                        Action = "*",
                    }
                );
            }
            foreach (var (membership, isOwner) in memberships.Where(m => m.Item2))
                seed.MembershipRoles.Add(
                    new MembershipRole
                    {
                        Id = Guid.CreateVersion7(),
                        OrgId = membership.OrgId,
                        MembershipId = membership.Id,
                        RoleId = owners[membership.OrgId].Id,
                        ScopePath = null,
                    }
                );
            await seed.SaveChangesAsync();
        }

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:locintel", appCs);
            builder.UseSetting("Auth:Provider", "local");
            builder.UseSetting("Audit:PolicyCacheTtlSeconds", "1");
            builder.UseSetting("Storage:LocalRoot", _storageRoot);
            builder.UseSetting("Secrets:LocalMasterKey", Convert.ToBase64String(new byte[32])); // dev/test wrapper key
            builder.UseSetting("Webhooks:RetryBaseSeconds", "1"); // fast retry backoff in tests
            builder.UseEnvironment("Testing");
            ConfigureHost(builder);
        });

        // What every org-writing flow does (tenant lifecycle, ingest): publish
        // the integration event. Identity's org_directory read model feeds
        // from it - login and /me depend on it, so wait for the sync.
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<Wolverine.IMessageBus>();
            await bus.PublishAsync(
                new LocIntel.Contracts.OrganizationUpserted(
                    OrgA,
                    "Org A",
                    "org-a",
                    RegionId.Default,
                    null
                )
            );
            await bus.PublishAsync(
                new LocIntel.Contracts.OrganizationUpserted(
                    OrgB,
                    "Org B",
                    "org-b",
                    RegionId.Default,
                    null
                )
            );
            await bus.PublishAsync(
                new LocIntel.Contracts.OrganizationUpserted(
                    PlatformOrg,
                    "Platform Ops",
                    "platform-ops",
                    RegionId.Default,
                    null,
                    "Active",
                    IsPlatform: true
                )
            );
        }
        for (var i = 0; i < 100; i++)
        {
            await using var check = CreateIdentityContext(adminCs);
            if (await check.OrgDirectory.CountAsync() >= 3)
                break;
            await Task.Delay(100);
        }
    }

    /// <summary>Subclass hook (e.g. the WorkOS-emulator fixture overrides auth settings).</summary>
    protected virtual void ConfigureHost(IWebHostBuilder builder) { }

    /// <summary>Fresh client authenticated as the given user via the real login flow.</summary>
    public async Task<HttpClient> LoginAsync(string email, string? orgHint = null)
    {
        var client = Factory.CreateDefaultClient(
            new RedirectHandler(),
            new CookieContainerHandler()
        );
        var url = $"/auth/login?returnUrl=%2Fme&hint={Uri.EscapeDataString(email)}";
        if (orgHint is not null)
            url += $"&org={Uri.EscapeDataString(orgHint)}";
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode(); // followed: login -> provider -> callback -> /me
        return client;
    }

    /// <summary>Unauthenticated client: a Guest principal (ADR 7).</summary>
    public HttpClient GuestClient() =>
        Factory.CreateDefaultClient(new RedirectHandler(), new CookieContainerHandler());

    public async Task<List<(DateTimeOffset start, DateTimeOffset end)>> QueryWindows(Guid siteId)
    {
        await using var db = CreateTenancyContext(_postgres.GetConnectionString());
        var typed = new SiteId(siteId);
        return (
            await db
                .SiteOpenWindows.IgnoreQueryFilters()
                .Where(w => w.SiteId == typed)
                .OrderBy(w => w.StartsAtUtc)
                .Select(w => new { w.StartsAtUtc, w.EndsAtUtc })
                .ToListAsync()
        )
            .Select(w => (w.StartsAtUtc, w.EndsAtUtc))
            .ToList();
    }

    /// <summary>Debug helper: surface Wolverine dead-letter exceptions in test failures.</summary>
    public async Task<string> DeadLetterSummary()
    {
        await using var conn = new Npgsql.NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        try
        {
            await using var cmd = new Npgsql.NpgsqlCommand(
                """
                SELECT (SELECT count(*) FROM wolverine.wolverine_dead_letters),
                       (SELECT min(exception_type || ': ' || exception_message) FROM wolverine.wolverine_dead_letters),
                       (SELECT string_agg(message_type || '=' || cnt, ',') FROM (
                           SELECT message_type, count(*)::text AS cnt
                           FROM wolverine.wolverine_incoming_envelopes GROUP BY message_type) t),
                       (SELECT count(*) FROM audit.authz_log)
                """,
                conn
            );
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            return $"dead={reader.GetInt64(0)} first={(reader.IsDBNull(1) ? "-" : reader.GetString(1))} incoming={(reader.IsDBNull(2) ? "-" : reader.GetString(2))} outgoing={reader.GetInt64(3)}";
        }
        catch (Npgsql.PostgresException e)
        {
            return "dead-letter query failed: " + e.MessageText;
        }
    }

    /// <summary>Superuser connection for test ARRANGE steps (RLS does not gate the superuser).</summary>
    public string PostgresConnectionString => _postgres.GetConnectionString();

    public async Task DeleteWindows(Guid siteId)
    {
        await using var db = CreateTenancyContext(_postgres.GetConnectionString());
        var typed = new SiteId(siteId);
        await db
            .SiteOpenWindows.IgnoreQueryFilters()
            .Where(w => w.SiteId == typed)
            .ExecuteDeleteAsync();
    }

    public async Task PublishForOrgA<T>(T message)
        where T : notnull
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<Wolverine.IMessageBus>();
        await LocIntel.Platform.Messaging.TenantedMessaging.PublishForOrgAsync(bus, OrgA, message);
    }

    public Task<HttpClient> OperatorClient() => LoginAsync(Operator);

    /// <summary>
    /// The org's hierarchy root, creating it if absent. Eighteen test classes
    /// wrote this dance out; idempotence matters as much as brevity, because
    /// a seed that creates rather than reuses is how order-dependent tests
    /// are born (see RandomOrderer).
    /// </summary>
    public static async Task<Guid> EnsureRootAsync(
        HttpClient client,
        string name = "Test Org",
        string[]? levels = null
    )
    {
        var existing = await client.GetAsync("/api/hierarchy");
        if (existing.IsSuccessStatusCode)
            return (await existing.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("nodes")
                .EnumerateArray()
                .First(n => n.GetProperty("depth").GetInt32() == 0)
                .GetProperty("id")
                .GetGuid();

        var created = await client.PostAsJsonAsync(
            "/api/hierarchy",
            new { name, levels = levels ?? ["Region"] }
        );
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("rootNodeId")
            .GetGuid();
    }

    /// <summary>
    /// A site with this name under the org's root, reused if it already
    /// exists. Returns its id.
    /// </summary>
    public static async Task<Guid> EnsureSiteAsync(
        HttpClient client,
        string name,
        string timeZone = "Etc/UTC",
        double? latitude = null,
        double? longitude = null
    )
    {
        foreach (var site in (await GetItemsAsync(client, "/api/sites")).EnumerateArray())
            if (site.GetProperty("name").GetString() == name)
                return site.GetProperty("id").GetGuid();

        var created = await client.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = await EnsureRootAsync(client),
                name,
                timeZone,
                latitude,
                longitude,
            }
        );
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Unwrap a paged list envelope ({ items, total, nextOffset }) to its items.</summary>
    public static async Task<System.Text.Json.JsonElement> GetItemsAsync(
        HttpClient client,
        string url
    ) =>
        (
            await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<System.Text.Json.JsonElement>(
                client,
                url
            )
        ).GetProperty("items");

    /// <summary>A user with NO org at all - the day-zero starting state.</summary>
    public async Task<Guid> CreateUserOnly(string email)
    {
        await using var db = CreateIdentityContext(_postgres.GetConnectionString());
        var user = AppUser.Create("local", email, email, email.Split('@')[0]);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    public async Task<string> ExternalOrgIdOf(Guid orgId)
    {
        await using var db = CreateTenancyContext(_postgres.GetConnectionString());
        var typed = new OrgId(orgId);
        return await db
            .Organizations.Where(o => o.Id == typed)
            .Select(o => o.ExternalId!)
            .SingleAsync();
    }

    /// <summary>Fresh role-less member of the org - for order-independent grant tests.</summary>
    public async Task<Guid> CreateMemberAsync(string email, OrgId org)
    {
        await using var db = CreateIdentityContext(_postgres.GetConnectionString());
        var user = AppUser.Create("local", email, email, email.Split('@')[0]);
        db.Users.Add(user);
        db.Memberships.Add(Membership.Create(user.Id, org));
        await db.SaveChangesAsync();
        return user.Id;
    }

    public async Task<Guid> UserIdOf(string email)
    {
        await using var db = CreateIdentityContext(_postgres.GetConnectionString());
        return await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
    }

    public async Task<Guid> SettingIdOf(OrgId org, string key)
    {
        await using var db = CreateTenancyContext(_postgres.GetConnectionString());
        return await db
            .OrganizationSettings.IgnoreQueryFilters()
            .Where(s => s.OrgId == org && s.Key == key)
            .Select(s => s.Id)
            .SingleAsync();
    }

    private static TenancyDbContext CreateTenancyContext(string cs) =>
        new(
            new DbContextOptionsBuilder<TenancyDbContext>()
                .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", "tenancy"))
                .Options,
            new TenantContext()
        );

    public async Task SeedAuditChange(OrgId org, Guid id, DateTimeOffset occurredAt)
    {
        await using var db = CreateAuditContext(_postgres.GetConnectionString());
        db.Changes.Add(
            new LocIntel.Platform.Audit.AuditChangeLog
            {
                Id = id,
                OrgId = org.Value,
                ActorTier = "system",
                ActorId = null,
                ActorLabel = null,
                SchemaName = "tenancy",
                TableName = "seeded",
                RowId = id.ToString(),
                Operation = "added",
                Diff = "{}",
                OccurredAt = occurredAt,
            }
        );
        await db.SaveChangesAsync();
    }

    public async Task<object?> QueryIngestBatch(string source)
    {
        await using var db = CreateModuleContext<IngestDbContext>(
            _postgres.GetConnectionString(),
            "ingest"
        );
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            db.Batches.IgnoreQueryFilters()
                .Where(b => b.Source == source)
                .Select(b => new { b.Id, status = b.Status.ToString() })
        );
    }

    public async Task<List<T>> QueryAudit<T>(Func<AuditDbContext, IQueryable<T>> query)
    {
        await using var db = CreateAuditContext(_postgres.GetConnectionString());
        return await query(db).ToListAsync();
    }

    private static T CreateModuleContext<T>(string cs, string schema)
        where T : LocIntel.Platform.Data.ModuleDbContext =>
        (T)
            Activator.CreateInstance(
                typeof(T),
                new DbContextOptionsBuilder<T>()
                    .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", schema))
                    .Options,
                new TenantContext()
            )!;

    private static AuditDbContext CreateAuditContext(string cs) =>
        new(
            new DbContextOptionsBuilder<AuditDbContext>()
                .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", "audit"))
                .Options,
            new TenantContext()
        );

    private static EntitlementsDbContext CreateEntitlementsContext(string cs) =>
        new(
            new DbContextOptionsBuilder<EntitlementsDbContext>()
                .UseNpgsql(
                    cs,
                    n => n.MigrationsHistoryTable("__ef_migrations_history", "entitlements")
                )
                .Options,
            new TenantContext()
        );

    private static IdentityDbContext CreateIdentityContext(string cs) =>
        new(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", "identity"))
                .Options,
            new TenantContext()
        );

    public virtual async Task DisposeAsync()
    {
        if (Factory is not null)
            await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
