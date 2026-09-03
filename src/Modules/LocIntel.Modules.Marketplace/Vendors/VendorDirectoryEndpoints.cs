using LocIntel.Contracts;
using LocIntel.Modules.Marketplace.Data;
using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Modules.Marketplace.Vendors.Api;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Marketplace.Vendors;

/// <summary>The buyer's view of the catalog - the platform-global vendor directory (ADR 48) - and their own preferred/blocked list.</summary>
public static class VendorDirectoryEndpoints
{
    [Transactional(typeof(MarketplaceDbContext))]
    [WolverineGet("/api/marketplace/vendors")]
    [ProducesResponseType(typeof(VendorListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        ServiceCategory? category,
        string? area,
        string? q,
        bool? preferredOnly,
        int? limit,
        int? offset,
        CancellationToken ct
    )
    {
        var gate = await ActorGate.RequireAsync(accessor, scopes, Capabilities.MarketplaceRead, ct);
        if (gate.Actor is not { } actor)
            return gate.ToResult();
        var now = time.GetUtcNow();
        var query = db.Directory.Where(p => p.OrgId != actor.Org);
        if (category is { } c)
        {
            var name = c.ToString();
            query = query.Where(p => p.Categories.Contains(name));
        }
        if (!string.IsNullOrWhiteSpace(area))
        {
            var code = area.Trim().ToUpperInvariant();
            query = query.Where(p => p.ServiceAreas.Contains(code));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = $"%{q.Trim()}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, needle) || EF.Functions.ILike(p.Description, needle)
            );
        }
        if (preferredOnly is true)
            query = query.Where(p => db.Preferred.Any(x => x.VendorOrgId == p.OrgId && !x.Blocked));
        var total = await query.CountAsync(ct);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var skip = Math.Max(offset ?? 0, 0);
        var rows = await query
            .OrderBy(p => p.Name)
            .Skip(skip)
            .Take(take)
            .Select(p => new
            {
                Listing = p,
                Preferred = db.Preferred.Any(x => x.VendorOrgId == p.OrgId && !x.Blocked),
                Blocked = db.Preferred.Any(x => x.VendorOrgId == p.OrgId && x.Blocked),
            })
            .ToListAsync(ct);
        var items = rows.Select(r =>
            {
                var credentials = VendorMatcher.Credentials(r.Listing);
                return new VendorSummary(
                    r.Listing.OrgId.Value,
                    r.Listing.Name,
                    r.Listing.Description,
                    r.Listing.Categories,
                    r.Listing.ServiceAreas,
                    credentials.Count(x => x.ExpiresAt > now),
                    credentials.Count(x => x.ExpiresAt <= now),
                    r.Preferred,
                    r.Blocked
                );
            })
            .ToList();
        return Results.Ok(
            new VendorListResponse(
                items,
                total,
                skip + items.Count < total ? skip + items.Count : null
            )
        );
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverineGet("/api/marketplace/vendors/{orgId}")]
    [ProducesResponseType(typeof(VendorProfileView), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid orgId,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.MarketplaceRead, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        var id = new OrgId(orgId);
        var listing = await db.Directory.FirstOrDefaultAsync(p => p.OrgId == id, ct);
        if (listing is null)
            return Results.NotFound();
        var now = time.GetUtcNow();
        return Results.Ok(
            new VendorProfileView(
                listing.OrgId.Value,
                listing.Name,
                listing.Description,
                listing.Categories,
                listing.ServiceAreas,
                listing.Latitude,
                listing.Longitude,
                listing.ServiceRadiusKm,
                listing.ContactEmail,
                listing.ContactPhone,
                true,
                listing.UpdatedAt,
                VendorMatcher
                    .Credentials(listing)
                    .Select(c => new CredentialView(
                        c.Id,
                        c.Kind,
                        c.Label,
                        null, // the number is the vendor's own business
                        c.Jurisdiction,
                        c.ExpiresAt,
                        c.ExpiresAt <= now
                    ))
                    .ToList()
            )
        );
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverineGet("/api/marketplace/preferred")]
    [ProducesResponseType(typeof(List<PreferredVendorView>), StatusCodes.Status200OK)]
    public static async Task<IResult> Preferred(
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.MarketplaceRead, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        var items = await db
            .Preferred.OrderBy(p => p.Blocked)
            .ThenBy(p => p.CreatedAt)
            .Select(p => new PreferredVendorView(
                p.Id,
                p.VendorOrgId.Value,
                db.Directory.Where(v => v.OrgId == p.VendorOrgId)
                    .Select(v => v.Name)
                    .FirstOrDefault(),
                p.Categories,
                p.Notes,
                p.Blocked,
                p.CreatedAt
            ))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/marketplace/preferred")]
    [ProducesResponseType(typeof(PreferredVendorSaved), StatusCodes.Status200OK)]
    public static async Task<IResult> SetPreferred(
        SetPreferredVendorRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await ActorGate.RequireAsync(
            accessor,
            scopes,
            Capabilities.MarketplaceManage,
            ct
        );
        if (gate.Actor is not { } actor)
            return gate.ToResult();
        var vendorOrg = new OrgId(request.VendorOrgId);
        if (!await db.Directory.AnyAsync(p => p.OrgId == vendorOrg, ct))
            return Results.NotFound();
        var row = await db.Preferred.FirstOrDefaultAsync(p => p.VendorOrgId == vendorOrg, ct);
        var created = row is null;
        row ??= new PreferredVendor
        {
            Id = Guid.CreateVersion7(),
            OrgId = actor.Org,
            VendorOrgId = vendorOrg,
            CreatedBy = actor.Id,
        };
        row.Categories = (request.Categories ?? []).Select(c => c.ToString()).Distinct().ToArray();
        row.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        row.Blocked = request.Blocked;
        if (created)
            db.Preferred.Add(row);
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            actor.Org,
            actor.Audit,
            request.Blocked ? "marketplace.vendor_blocked" : "marketplace.vendor_preferred",
            new { VendorOrgId = vendorOrg.Value }
        );
        return Results.Ok(new PreferredVendorSaved(row.Id));
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverineDelete("/api/marketplace/preferred/{id}")]
    [ProducesResponseType(typeof(PreferredVendorRemoved), StatusCodes.Status200OK)]
    public static async Task<IResult> RemovePreferred(
        Guid id,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.MarketplaceManage, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        var row = await db.Preferred.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null)
            return Results.NotFound();
        db.Preferred.Remove(row);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new PreferredVendorRemoved(row.Id));
    }
}
