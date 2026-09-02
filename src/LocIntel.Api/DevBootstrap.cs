using Microsoft.EntityFrameworkCore;
using LocIntel.Contracts;
using LocIntel.Platform.Kernel;
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
    ILogger<DevBootstrap> logger
) : BackgroundService
{
    public const string EmulatorUserId = "user_01DEVALICE00000000000000";
    public const string EmulatorOrgId = "org_01DEVACME000000000000000";
    public const string EmulatorOperatorId = "user_01DEVOPERATOR0000000000";

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

        // RLS-protected rows (roles, grants, assignments) are seeded in the
        // org's own tenant scope: the app role holds no bypass (ADR 38)
        await SeedOwnerAsync(
            sp,
            org.Id,
            "workos",
            EmulatorUserId,
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
            "workos",
            EmulatorOperatorId,
            "operator@locintel.local",
            "LocIntel Operator",
            "Operator",
            ct
        );

        // what every org-writing flow does: publish the event (org_directory)
        var bus = sp.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(
            new OrganizationUpserted(org.Id, org.Name, org.Slug, org.Region, org.ExternalId)
        );
        await bus.PublishAsync(
            new OrganizationUpserted(
                platformOrg.Id,
                platformOrg.Name,
                platformOrg.Slug,
                platformOrg.Region,
                platformOrg.ExternalId,
                "Active",
                IsPlatform: true
            )
        );
    }

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
                identity.RoleGrants.Add(
                    new LocIntel.Modules.Identity.Access.RoleGrant
                    {
                        Id = Guid.CreateVersion7(),
                        OrgId = orgId,
                        RoleId = role.Id,
                        Domain = "*",
                        Action = "*",
                    }
                );
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
