using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Identity.Users;

/// <summary>
/// The provider's directory-sync webhook (ADR 41): anonymous by nature,
/// authenticated by the provider's signature inside the seam. Verified events
/// for org or event types we do not track 202 (the provider's retry health
/// stays green); only unverifiable deliveries 400.
/// </summary>
public static class DirectoryWebhookEndpoint
{
    [Transactional(typeof(IdentityDbContext))]
    [WolverinePost("/auth/directory/webhook")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public static async Task<IResult> Receive(
        HttpContext http,
        IAuthProvider provider,
        IdentityDbContext db,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (provider is not IDirectoryEventSource source)
            return Results.NotFound();
        using var reader = new StreamReader(http.Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        var headers = http.Request.Headers.ToDictionary(
            h => h.Key,
            h => h.Value.ToString(),
            StringComparer.OrdinalIgnoreCase
        );
        var webhook = await source.ParseDirectoryWebhookAsync(body, headers, ct);
        if (!webhook.Verified)
            return Results.BadRequest();
        if (webhook.Event is not { } evt)
            return Results.Accepted();

        var entry = await db.OrgDirectory.FirstOrDefaultAsync(
            d => d.ExternalId == evt.ExternalOrgId,
            ct
        );
        if (entry is null)
            return Results.Accepted(); // org since offboarded, or never ours

        await bus.PublishAsync(
            new DirectoryUserSynced(evt.Kind, evt.Email, evt.Name),
            new DeliveryOptions { TenantId = entry.OrgId.Value.ToString() }
        );
        return Results.Accepted();
    }
}
