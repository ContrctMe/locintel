using LocIntel.Contracts;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace LocIntel.Api;

/// <summary>
/// Development-only boot: seed the dev org/user matched to the WorkOS
/// emulator's pinned ids (workos-emulate.config.yaml), so `aspire run` on a
/// fresh clone gives a working login. Never registered outside Development.
/// Migrations belong to the MIGRATE role (ADR 38); this service retries
/// until that role has run. It connects as the unprivileged app role, so
/// RLS-protected seed rows are written in per-org tenant scopes.
/// </summary>
public sealed class DevBootstrap(
    IServiceProvider services,
    ReadinessState readiness,
    IConfiguration configuration,
    ILogger<DevBootstrap> logger
) : BackgroundService
{
    public const string EmulatorUserId = "user_01DEVALICE00000000000000";
    public const string EmulatorOrgId = "org_01DEVACME000000000000000";
    public const string EmulatorOperatorId = "user_01DEVOPERATOR0000000000";

    // The seeded memberships must be keyed the way the ACTIVE provider mints
    // ids, or the dev user signs in as a brand-new orgless person. With
    // Auth:Provider=local (LOCINTEL_AUTH=local: a password-less boot for
    // browser smoke runs) LocalAuthProvider mints "local_{email}", so seed
    // that instead of the emulator's user_… ids.
    private string Provider => configuration["Auth:Provider"] ?? "local";

    private string ExternalIdFor(string email, string emulatorId) =>
        Provider == "workos" ? emulatorId : email;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await RunAsync(stoppingToken);
                readiness.MarkReady();
                logger.LogInformation("dev bootstrap complete");
                return;
            }
            catch (Exception e) when (attempt < 30 && !stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("dev bootstrap waiting on dependencies ({Error})", e.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // seed keyed to the emulator's PINNED ids - login just matches
        var tenancy = sp.GetRequiredService<LocIntel.Modules.Tenancy.Data.TenancyDbContext>();
        var org = await tenancy.Organizations.FirstOrDefaultAsync(o => o.Slug == "acme-dev", ct);
        if (org is null)
        {
            org = new LocIntel.Modules.Tenancy.Organizations.Organization
            {
                Id = OrgId.New(),
                Name = "Acme Dev",
                Slug = "acme-dev",
                Region = RegionId.Default,
                ExternalId = EmulatorOrgId,
            };
            tenancy.Organizations.Add(org);
            await tenancy.SaveChangesAsync(ct);
        }

        // brand color: the public app's theming hook, visible on a fresh clone
        await TenantScope.RunAsAsync(
            sp,
            org.Id,
            async scoped =>
            {
                var scopedTenancy =
                    scoped.GetRequiredService<LocIntel.Modules.Tenancy.Data.TenancyDbContext>();
                if (
                    !await scopedTenancy.OrganizationSettings.AnyAsync(
                        x => x.Key == "brand.color",
                        ct
                    )
                )
                {
                    scopedTenancy.OrganizationSettings.Add(
                        LocIntel.Modules.Tenancy.Organizations.OrganizationSetting.Create(
                            org.Id,
                            "brand.color",
                            "#B01458"
                        )
                    );
                    await scopedTenancy.SaveChangesAsync(ct);
                }
            }
        );

        // a hierarchy and a handful of sites WITH coordinates, so the console's
        // map view (ADR 49/50) has something to draw on a fresh clone
        await SeedSitesAsync(sp, org.Id, ct);

        // RLS-protected rows (roles, grants, assignments) are seeded in the
        // org's own tenant scope: the app role holds no bypass (ADR 38)
        await SeedOwnerAsync(
            sp,
            org.Id,
            Provider,
            ExternalIdFor("alice@acme.test", EmulatorUserId),
            "alice@acme.test",
            "Alice Dev",
            "Owner",
            ct
        );

        // the vendor's own org: operators live here (platform:operate)
        var platformOrg = await tenancy.Organizations.FirstOrDefaultAsync(
            o => o.Slug == "locintel-ops",
            ct
        );
        if (platformOrg is null)
        {
            platformOrg = new LocIntel.Modules.Tenancy.Organizations.Organization
            {
                Id = OrgId.New(),
                Name = "LocIntel Operations",
                Slug = "locintel-ops",
                Region = RegionId.Default,
                IsPlatform = true,
            };
            tenancy.Organizations.Add(platformOrg);
            await tenancy.SaveChangesAsync(ct);
        }
        await SeedOwnerAsync(
            sp,
            platformOrg.Id,
            Provider,
            ExternalIdFor("operator@locintel.local", EmulatorOperatorId),
            "operator@locintel.local",
            "LocIntel Operator",
            "Operator",
            ct
        );

        // what every org-writing flow does: publish the event (org_directory)
        var bus = sp.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(
            new OrganizationUpserted(
                org.Id,
                org.Name,
                org.Slug,
                org.Region,
                org.ExternalId,
                org.Version
            )
        );
        await bus.PublishAsync(
            new OrganizationUpserted(
                platformOrg.Id,
                platformOrg.Name,
                platformOrg.Slug,
                platformOrg.Region,
                platformOrg.ExternalId,
                platformOrg.Version,
                "Active",
                IsPlatform: true
            )
        );
    }

    /// <summary>
    /// Region › Market hierarchy and six Portland-area sites with real
    /// coordinates. Idempotent: skipped once the org has a hierarchy. Written
    /// straight through the DbContext in tenant scope - the dev seed is not a
    /// request, so the gates do not apply, but RLS still does (ADR 38).
    /// </summary>
    private static Task SeedSitesAsync(IServiceProvider sp, OrgId orgId, CancellationToken ct) =>
        TenantScope.RunAsAsync(
            sp,
            orgId,
            async scoped =>
            {
                var db =
                    scoped.GetRequiredService<LocIntel.Modules.Tenancy.Data.TenancyDbContext>();
                if (await db.Hierarchies.AnyAsync(ct))
                    return;

                var hierarchy = LocIntel.Modules.Tenancy.Hierarchy.OrgHierarchy.Create(
                    orgId,
                    "Organization",
                    ["Region", "Market"]
                );
                var root = LocIntel.Modules.Tenancy.Hierarchy.HierarchyNode.CreateRoot(
                    orgId,
                    hierarchy.Id,
                    "Acme Dev"
                );
                var region = LocIntel.Modules.Tenancy.Hierarchy.HierarchyNode.CreateChild(
                    root,
                    "Pacific Northwest"
                );
                var portland = LocIntel.Modules.Tenancy.Hierarchy.HierarchyNode.CreateChild(
                    region,
                    "Portland Metro"
                );
                var seattle = LocIntel.Modules.Tenancy.Hierarchy.HierarchyNode.CreateChild(
                    region,
                    "Seattle"
                );
                db.Hierarchies.Add(hierarchy);
                db.HierarchyNodes.AddRange(root, region, portland, seattle);

                // (node, name, address, city, postal, lat, lng) - all America/Los_Angeles
                (
                    LocIntel.Modules.Tenancy.Hierarchy.HierarchyNode,
                    string,
                    string,
                    string,
                    string,
                    double,
                    double
                )[] seeds =
                [
                    (
                        portland,
                        "Hawthorne",
                        "3520 SE Hawthorne Blvd",
                        "Portland",
                        "97214",
                        45.5122,
                        -122.6284
                    ),
                    (
                        portland,
                        "Pearl District",
                        "1234 NW Lovejoy St",
                        "Portland",
                        "97209",
                        45.5289,
                        -122.6844
                    ),
                    (
                        portland,
                        "Downtown Portland",
                        "610 SW Alder St",
                        "Portland",
                        "97205",
                        45.5195,
                        -122.6787
                    ),
                    (
                        portland,
                        "Lake Oswego",
                        "410 N State St",
                        "Lake Oswego",
                        "97034",
                        45.4207,
                        -122.6685
                    ),
                    (
                        seattle,
                        "Capitol Hill",
                        "1500 E Olive Way",
                        "Seattle",
                        "98122",
                        47.6191,
                        -122.3235
                    ),
                    (
                        seattle,
                        "Ballard",
                        "5401 Ballard Ave NW",
                        "Seattle",
                        "98107",
                        47.6683,
                        -122.3841
                    ),
                ];
                foreach (var (node, name, address, city, postal, lat, lng) in seeds)
                {
                    var id = SiteId.New();
                    db.Sites.Add(
                        new LocIntel.Modules.Tenancy.Sites.Site
                        {
                            Id = id,
                            OrgId = orgId,
                            NodeId = node.Id,
                            Name = name,
                            TimeZone = "America/Los_Angeles",
                            Path = new Microsoft.EntityFrameworkCore.LTree(
                                $"{node.Path}.{LocIntel.Modules.Tenancy.Sites.Site.Label(id)}"
                            ),
                            AddressLine1 = address,
                            City = city,
                            PostalCode = postal,
                            CountryCode = "US",
                            Latitude = lat,
                            Longitude = lng,
                        }
                    );
                }
                await db.SaveChangesAsync(ct);
            }
        );

    private static Task SeedOwnerAsync(
        IServiceProvider sp,
        OrgId orgId,
        string provider,
        string subject,
        string email,
        string name,
        string roleName,
        CancellationToken ct
    ) =>
        TenantScope.RunAsAsync(
            sp,
            orgId,
            async scoped =>
            {
                var identity =
                    scoped.GetRequiredService<LocIntel.Modules.Identity.Data.IdentityDbContext>();
                if (await identity.Users.AnyAsync(u => u.Subject == subject, ct))
                    return;
                var user = LocIntel.Modules.Identity.Users.AppUser.Create(
                    provider,
                    subject,
                    email,
                    name
                );
                var membership = LocIntel.Modules.Identity.Users.Membership.Create(user.Id, orgId);
                var role = LocIntel.Modules.Identity.Access.Role.Create(orgId, roleName);
                identity.Users.Add(user);
                identity.Memberships.Add(membership);
                identity.Roles.Add(role);
                identity.RoleGrants.Add(LocIntel.Modules.Identity.Access.RoleGrant.Wildcard(role));
                identity.MembershipRoles.Add(
                    new LocIntel.Modules.Identity.Access.MembershipRole
                    {
                        Id = Guid.CreateVersion7(),
                        OrgId = orgId,
                        MembershipId = membership.Id,
                        RoleId = role.Id,
                        ScopePath = null,
                    }
                );
                await identity.SaveChangesAsync(ct);
            }
        );
}
