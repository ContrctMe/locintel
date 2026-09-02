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

/// <summary>
/// The vendor's own catalog entry (vendor:manage, inside the vendor org).
/// Publishing is what opens the row to every other org's reads.
/// </summary>
public static class VendorProfileEndpoints
{
    [Transactional(typeof(MarketplaceDbContext))]
    [WolverineGet("/api/vendor/profile")]
    [ProducesResponseType(typeof(VendorProfileView), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.VendorManage, ct))
            return Results.Unauthorized();
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.OrgId == actor.Org, ct);
        if (profile is null)
            return Results.NotFound();
        return Results.Ok(await ViewAsync(profile, db, time, ct));
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePut("/api/vendor/profile")]
    [ProducesResponseType(typeof(VendorProfileView), StatusCodes.Status200OK)]
    public static async Task<IResult> Upsert(
        UpsertVendorProfileRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.VendorManage, ct))
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
            return Results.BadRequest(
                new { error = "a vendor needs a name of up to 200 characters" }
            );
        if (request.Categories is null || request.Categories.Length == 0)
            return Results.BadRequest(new { error = "offer at least one category" });
        if ((request.Latitude is null) != (request.Longitude is null))
            return Results.BadRequest(new { error = "latitude and longitude come as a pair" });
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
            return Results.BadRequest(new { error = "coordinates out of range" });
        if (request.ServiceRadiusKm is < 0 or > 5000)
            return Results.BadRequest(new { error = "service radius must be 0-5000 km" });

        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.OrgId == actor.Org, ct);
        var created = profile is null;
        profile ??= new VendorProfile
        {
            Id = Guid.CreateVersion7(),
            OrgId = actor.Org,
            Name = request.Name.Trim(),
        };
        profile.Name = request.Name.Trim();
        profile.Description = request.Description?.Trim() ?? "";
        profile.Categories = request.Categories.Select(c => c.ToString()).Distinct().ToArray();
        profile.ServiceAreas = (request.ServiceAreas ?? [])
            .Select(a => a.Trim().ToUpperInvariant())
            .Where(a => a.Length > 0)
            .Distinct()
            .Take(60)
            .ToArray();
        profile.Latitude = request.Latitude;
        profile.Longitude = request.Longitude;
        profile.ServiceRadiusKm = request.ServiceRadiusKm;
        profile.ContactEmail = Clean(request.ContactEmail);
        profile.ContactPhone = Clean(request.ContactPhone);
        profile.UpdatedAt = time.GetUtcNow();
        if (created)
            db.Profiles.Add(profile);
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            actor.Org,
            actor.Audit,
            created ? "vendor.profile_created" : "vendor.profile_updated",
            new { profile.Name, profile.Categories }
        );
        return Results.Ok(await ViewAsync(profile, db, time, ct));
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/vendor/profile/publish")]
    [ProducesResponseType(typeof(VendorProfileView), StatusCodes.Status200OK)]
    public static async Task<IResult> Publish(
        PublishVendorProfileRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.VendorManage, ct))
            return Results.Unauthorized();
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.OrgId == actor.Org, ct);
        if (profile is null)
            return Results.NotFound();
        profile.Published = request.Published;
        profile.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            actor.Org,
            actor.Audit,
            request.Published ? "vendor.profile_published" : "vendor.profile_unpublished",
            new { profile.Name }
        );
        return Results.Ok(await ViewAsync(profile, db, time, ct));
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/vendor/credentials")]
    [ProducesResponseType(typeof(CredentialCreated), StatusCodes.Status200OK)]
    public static async Task<IResult> AddCredential(
        AddCredentialRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.VendorManage, ct))
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Label) || request.Label.Trim().Length > 200)
            return Results.BadRequest(
                new { error = "a credential needs a label of up to 200 characters" }
            );
        if (!await db.Profiles.AnyAsync(p => p.OrgId == actor.Org, ct))
            return Results.NotFound();
        var credential = new VendorCredential
        {
            Id = Guid.CreateVersion7(),
            OrgId = actor.Org,
            Kind = request.Kind,
            Label = request.Label.Trim(),
            Number = Clean(request.Number),
            Jurisdiction = Clean(request.Jurisdiction)?.ToUpperInvariant(),
            ExpiresAt = request.ExpiresAt.ToUniversalTime(),
        };
        db.Credentials.Add(credential);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new CredentialCreated(credential.Id));
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverineDelete("/api/vendor/credentials/{id}")]
    [ProducesResponseType(typeof(CredentialRemoved), StatusCodes.Status200OK)]
    public static async Task<IResult> RemoveCredential(
        Guid id,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.VendorManage, ct))
            return Results.Unauthorized();
        var credential = await db.Credentials.FirstOrDefaultAsync(
            c => c.Id == id && c.OrgId == actor.Org,
            ct
        );
        if (credential is null)
            return Results.NotFound();
        db.Credentials.Remove(credential);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new CredentialRemoved(credential.Id));
    }

    internal static async Task<VendorProfileView> ViewAsync(
        VendorProfile profile,
        MarketplaceDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var now = time.GetUtcNow();
        var credentials = await db
            .Credentials.Where(c => c.OrgId == profile.OrgId)
            .OrderBy(c => c.ExpiresAt)
            .ToListAsync(ct);
        return new VendorProfileView(
            profile.OrgId.Value,
            profile.Name,
            profile.Description,
            profile.Categories,
            profile.ServiceAreas,
            profile.Latitude,
            profile.Longitude,
            profile.ServiceRadiusKm,
            profile.ContactEmail,
            profile.ContactPhone,
            profile.Published,
            profile.UpdatedAt,
            credentials
                .Select(c => new CredentialView(
                    c.Id,
                    c.Kind,
                    c.Label,
                    c.Number,
                    c.Jurisdiction,
                    c.ExpiresAt,
                    c.ExpiresAt <= now
                ))
                .ToList()
        );
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
