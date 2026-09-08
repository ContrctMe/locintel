using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Hierarchy;
using LocIntel.Platform.Auth;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Organizations;

public sealed record CreateOrgRequest(string Name, string Slug);

public sealed record OrgCreatedResponse(Guid OrgId, string Slug);

/// <summary>
/// Day-zero onboarding (the tenant-lifecycle front half): any authenticated
/// user may create an org. The provider directory capability (WorkOS) gets
/// the org created on its side too; Identity provisions the founder via the
/// outbox; OrganizationUpserted feeds every read model.
/// </summary>
public static class OnboardingEndpoints
{
    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/orgs")]
    [ProducesResponseType(typeof(OrgCreatedResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Create(
        CreateOrgRequest request,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IAuthProvider provider,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { UserId: var userId })
            return Results.Unauthorized();

        var slug = request.Slug.Trim().ToLowerInvariant();
        if (
            slug.Length is < 3 or > 60
            || !slug.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')
        )
            return ApiErrors.BadRequest(
                "slug must be 3-60 chars of lowercase letters, digits, and dashes"
            );
        if (string.IsNullOrWhiteSpace(request.Name))
            return ApiErrors.BadRequest("name is required");
        if (await db.Organizations.AnyAsync(o => o.Slug == slug, ct))
            return ApiErrors.Conflict($"slug '{slug}' is taken");

        // provider org first (WorkOS as much as possible): invitations and
        // SSO hang off it. Absent the capability (bare OIDC), ExternalId
        // stays null and everything else still works.
        string? externalId = null;
        if (provider is IOrganizationDirectory directory)
            externalId = await directory.CreateOrganizationAsync(request.Name, ct);

        var org = new Organization
        {
            Id = OrgId.New(),
            Name = request.Name.Trim(),
            Slug = slug,
            Region = RegionId.Default,
            ExternalId = externalId,
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new OrganizationUpserted(
                org.Id,
                org.Name,
                org.Slug,
                org.Region,
                org.ExternalId,
                org.Version,
                org.Status.ToString(),
                org.IsPlatform
            )
        );
        // the org's first hierarchy (root named after it, default levels) and
        // its founder both land under the NEW tenant via the outbox: this
        // request's connection runs as the founder's previous org, or none
        await bus.PublishAsync(
            new ProvisionDefaultHierarchy(org.Id),
            new DeliveryOptions { TenantId = org.Id.Value.ToString() }
        );
        await bus.PublishAsync(
            new ProvisionFounderMembership(userId, org.Id),
            new DeliveryOptions { TenantId = org.Id.Value.ToString() }
        );
        await bus.AuditAsync(
            org.Id,
            AuditActor.User(userId),
            "org.created",
            new { org.Name, org.Slug }
        );
        return Results.Ok(new OrgCreatedResponse(org.Id.Value, org.Slug));
    }
}

public sealed record RenameOrgRequest(string Name);

public static class OrgSettingsEndpoints
{
    /// <summary>Rename the active org: local truth, read models, and the provider directory all learn.</summary>
    [Transactional(typeof(TenancyDbContext))]
    [WolverinePut("/api/org")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> Rename(
        RenameOrgRequest request,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IAuthProvider provider,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OrgManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var orgId })
            return gate.ToResult();
        var userId = principal.UserId;
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
            return ApiErrors.BadRequest("name must be 1-200 characters");

        var org = await db.Organizations.FirstAsync(o => o.Id == orgId, ct);
        var previous = org.Name;
        org.Name = request.Name.Trim();
        await db.SaveChangesAsync(ct);

        if (provider is IOrganizationDirectory directory && org.ExternalId is { } externalId)
            await directory.UpdateOrganizationNameAsync(externalId, org.Name, ct);

        await bus.PublishAsync(
            new OrganizationUpserted(
                org.Id,
                org.Name,
                org.Slug,
                org.Region,
                org.ExternalId,
                org.Version,
                org.Status.ToString(),
                org.IsPlatform
            )
        );
        await bus.AuditAsync(
            org.Id,
            AuditActor.User(userId),
            "org.renamed",
            new { from = previous, to = org.Name }
        );
        return Results.Ok(new { org.Name });
    }
}
