using System.Security.Claims;
using LocIntel.Contracts;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using LocIntel.Platform.Notifications;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.Http;

namespace LocIntel.Modules.Identity.Auth;

public sealed record ContactResponse(Guid Id, string Email, DateTimeOffset CreatedAt, bool Revoked);

/// <summary>
/// The identified-contact tier (ADR 7): known via a signed, expiring token -
/// no account. Issuance persists the Contact record (the revocation store)
/// and publishes SendContactLink through the Wolverine outbox (ADR 32: the
/// email is transactional with its cause); the handler renders and hands to
/// the transport. Tokens are short-lived (30 min); the CONTACT is long-lived
/// and revocable. The link points at the org's own public host - a redeemed
/// contact lands on the org's public app, identified.
/// </summary>
public static class ContactLinks
{
    private const string TokenPurpose = "locintel.contact.link";

    public sealed record IssueContactLinkRequest(string Email);

    public sealed record SendContactLink(string Email, string Url, string OrgName);

    public static IEndpointRouteBuilder MapContactLinkEndpoints(this IEndpointRouteBuilder app)
    {
        // Issuance: a member of the active org invites a contact into it.
        app.MapPost(
            "/contact-links",
            async (
                HttpContext http,
                IPrincipalAccessor accessor,
                IdentityDbContext db,
                IDataProtectionProvider dp,
                IMessageBus bus,
                IEntitlements entitlements,
                IConfiguration configuration,
                IssueContactLinkRequest request,
                CancellationToken ct
            ) =>
            {
                if (
                    accessor.Current
                    is not Principal.User { ActiveOrg: { } org, UserId: var userId }
                )
                    return Results.Unauthorized();
                if (!await db.Memberships.AnyAsync(m => m.UserId == userId && m.OrgId == org, ct))
                    return Results.NotFound();
                // the one place a human can be TOLD the address is dead -
                // the transport-level suppression drop is silent by design
                if (
                    await db.EmailSuppressions.AnyAsync(
                        s => s.Email == request.Email.Trim().ToLower(),
                        ct
                    )
                )
                    return Results.UnprocessableEntity(
                        new
                        {
                            error = "this address has bounced before and is suppressed; "
                                + "verify it with the contact, then ask the operator to unsuppress it",
                        }
                    );

                // Gate 1, both shapes: boolean feature switch + monthly meter
                // (Grace absorbs the approximate live count, ADR 9).
                if (!await entitlements.HasAsync(org, EntitlementCatalog.ContactLinksEnabled, ct))
                    return GateResults.FeatureOff(EntitlementCatalog.ContactLinksEnabled);
                var usage = await entitlements.RecordUsageAsync(
                    org,
                    EntitlementCatalog.ContactLinksMonthly,
                    1,
                    ct
                );
                if (!usage.IsAllowed)
                    return GateResults.LimitReached(usage);

                // the CONTACT is the durable, revocable thing; the token is
                // just a 30-minute key to it. Re-inviting a revoked contact
                // is a deliberate re-grant.
                var email = request.Email.Trim().ToLowerInvariant();
                var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Email == email, ct);
                if (contact is null)
                {
                    contact = new Contact
                    {
                        Id = Guid.CreateVersion7(),
                        OrgId = org,
                        Email = email,
                        CreatedBy = userId,
                    };
                    db.Contacts.Add(contact);
                }
                else
                {
                    contact.RevokedAt = null;
                }
                await db.SaveChangesAsync(ct);

                var expires = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
                var token = dp.CreateProtector(TokenPurpose)
                    .Protect($"{contact.Id}|{org.Value}|{expires}");

                // the link lands on the ORG'S public host: the contact's
                // world is the public app, not the console or the API
                var directoryEntry = await db.OrgDirectory.FirstAsync(d => d.OrgId == org, ct);
                var slug = directoryEntry.Slug;
                var publicHost =
                    configuration["Public:HostTemplate"] ?? "http://{slug}.localhost:5174";
                var url =
                    $"{publicHost.Replace("{slug}", slug)}/contact/redeem?token={Uri.EscapeDataString(token)}";

                await bus.PublishAsync(new SendContactLink(email, url, directoryEntry.Name));
                await bus.AuditAsync(
                    org,
                    AuditActor.User(userId),
                    "contact.invited",
                    new { contactId = contact.Id }
                );
                return Results.Ok(new { contactId = contact.Id });
            }
        );

        app.MapGet(
            "/contact/redeem",
            async (HttpContext http, IDataProtectionProvider dp, string token) =>
            {
                string[] parts;
                try
                {
                    parts = dp.CreateProtector(TokenPurpose).Unprotect(token).Split('|');
                }
                catch (Exception)
                {
                    return Results.BadRequest(new { error = "invalid or tampered link" });
                }
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > long.Parse(parts[2]))
                    return Results.BadRequest(new { error = "link expired, request a new one" });

                // the token is only a key: the CONTACT RECORD decides. This
                // request is anonymous and contacts are RLS-protected, so the
                // read runs in a scope tenanted from the authenticated token.
                var contactId = Guid.Parse(parts[0]);
                var org = new OrgId(Guid.Parse(parts[1]));
                var email = await TenantScope.RunAsAsync(
                    http.RequestServices,
                    org,
                    sp =>
                        sp.GetRequiredService<IdentityDbContext>()
                            .Contacts.Where(c => c.Id == contactId && c.RevokedAt == null)
                            .Select(c => c.Email)
                            .FirstOrDefaultAsync(http.RequestAborted)
                );
                if (email is null)
                    return Results.BadRequest(new { error = "this link has been revoked" });

                await http.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    AuthEndpoints.BuildContactClaimsPrincipal(
                        Guid.Parse(parts[0]),
                        new OrgId(Guid.Parse(parts[1])),
                        email
                    )
                );
                // relative: through the public app's redeem proxy this is the
                // org's public locator; hit directly it is the API root
                return Results.Redirect("/");
            }
        );

        return app;
    }
}

/// <summary>Wolverine handler: durable via the outbox, retried on transport failure.</summary>
public static class SendContactLinkHandler
{
    public static Task Handle(
        ContactLinks.SendContactLink message,
        INotificationTransport transport,
        CancellationToken ct
    ) =>
        transport.SendAsync(
            EmailTemplate.Render(
                message.Email,
                $"Your {message.OrgName} access link",
                message.OrgName,
                [$"{message.OrgName} has shared their locations with you."],
                (message.Url, "Open your access link"),
                "The link expires in 30 minutes. If you didn't expect this, ignore it."
            ),
            ct
        );
}

/// <summary>
/// Contact custody for members: who was let in, and taking it back. Revocation
/// is a status flip - the row remains the auditable record, and both the
/// scope resolver (live sessions) and redemption (unexpired tokens) consult
/// it, so revoking cuts off every path at once.
/// </summary>
public static class ContactManagementEndpoints
{
    [WolverineGet("/api/contacts")]
    [ProducesResponseType(typeof(List<ContactResponse>), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.RolesManage, ct))
            return new GateOutcome.Forbidden(Capabilities.RolesManage).ToResult();
        var contacts = await db
            .Contacts.OrderByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                c.Email,
                c.CreatedAt,
                revoked = c.RevokedAt != null,
            })
            .ToListAsync(ct);
        return Results.Ok(contacts);
    }

    [WolverineDelete("/api/contacts/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> Revoke(
        Guid id,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.RolesManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var userId = principal.UserId;
        var contact = await db.Contacts.FirstOrDefaultAsync(
            c => c.Id == id && c.RevokedAt == null,
            ct
        );
        if (contact is null)
            return Results.NotFound();
        contact.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            org,
            AuditActor.User(userId),
            "contact.revoked",
            new { contactId = id }
        );
        return Results.NoContent();
    }
}
