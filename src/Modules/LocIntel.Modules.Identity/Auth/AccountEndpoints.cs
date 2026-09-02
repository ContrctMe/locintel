using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Identity.Access;
using LocIntel.Modules.Identity.Data;
using LocIntel.Modules.Identity.Users;
using LocIntel.Platform.Auth;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using LocIntel.Platform.Notifications;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace LocIntel.Modules.Identity.Auth;

/// <summary>
/// Account self-service: the user acting on THEMSELVES, not on an org.
/// Minimal APIs (not Wolverine) for the same reason as the auth endpoints -
/// these flows re-issue or destroy the cookie. Credentials and MFA stay with
/// the provider (ADR 14): we sync the name, deliver the provider-minted
/// password reset link, and on deletion take the provider's user record down
/// with ours.
/// </summary>
public static class AccountEndpoints
{
    public sealed record UpdateProfileRequest(string Name);

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut(
            "/auth/profile",
            async (
                HttpContext http,
                UpdateProfileRequest request,
                IdentityDbContext db,
                IAuthProvider provider,
                CancellationToken ct
            ) =>
            {
                if (GetUserId(http) is not { } userId)
                    return Results.Unauthorized();
                if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
                    return Results.BadRequest(new { error = "name must be 1-200 characters" });

                var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
                user.Name = request.Name.Trim();
                await db.SaveChangesAsync(ct);

                if (provider is IUserLifecycle lifecycle && user.Provider == provider.Name)
                    await lifecycle.UpdateUserNameAsync(user.Subject, user.Name, ct);

                // re-issue the cookie so the display-name claim is current
                await http.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    AuthEndpoints.BuildClaimsPrincipal(
                        user,
                        ActiveOrg(http),
                        AuthEndpoints.GetSessionId(http.User)
                    )
                );
                return Results.NoContent();
            }
        );

        app.MapPost(
            "/auth/password-reset",
            async (
                HttpContext http,
                IdentityDbContext db,
                IAuthProvider provider,
                INotificationTransport transport,
                CancellationToken ct
            ) =>
            {
                if (GetUserId(http) is not { } userId)
                    return Results.Unauthorized();
                var user = await db.Users.FirstAsync(u => u.Id == userId, ct);

                // the provider MINTS the reset (the credential is theirs);
                // we only deliver the link. Null = the provider emailed it.
                if (
                    provider is IUserLifecycle lifecycle
                    && await lifecycle.BeginPasswordResetAsync(user.Email, ct) is { } url
                )
                    await transport.SendAsync(
                        EmailTemplate.Render(
                            user.Email,
                            "Reset your password",
                            "LocIntel",
                            ["A password reset was requested for this account."],
                            (url.ToString(), "Reset your password"),
                            "If you did not request this, ignore it."
                        ),
                        ct
                    );
                return Results.Accepted(value: new { status = "sent" });
            }
        );

        app.MapGet(
            "/auth/sessions",
            async (HttpContext http, IdentityDbContext db, CancellationToken ct) =>
            {
                if (GetUserId(http) is not { } userId)
                    return Results.Unauthorized();
                var current = AuthEndpoints.GetSessionId(http.User);
                var sessions = await db
                    .Sessions.Where(s => s.UserId == userId && s.RevokedAt == null)
                    .OrderByDescending(s => s.CreatedAt)
                    .Select(s => new
                    {
                        s.Id,
                        s.UserAgent,
                        s.CreatedAt,
                        current = s.Id == current,
                    })
                    .ToListAsync(ct);
                return Results.Ok(sessions);
            }
        );

        app.MapDelete(
            "/auth/sessions/{id:guid}",
            async (Guid id, HttpContext http, IdentityDbContext db, CancellationToken ct) =>
            {
                if (GetUserId(http) is not { } userId)
                    return Results.Unauthorized();
                // 404 for someone else's session: never confirm it exists
                var revoked = await db
                    .Sessions.Where(s => s.Id == id && s.UserId == userId && s.RevokedAt == null)
                    .ExecuteUpdateAsync(
                        u => u.SetProperty(s => s.RevokedAt, DateTimeOffset.UtcNow),
                        ct
                    );
                return revoked == 0 ? Results.NotFound() : Results.NoContent();
            }
        );

        app.MapPost(
            "/auth/sessions/revoke-others",
            async (
                HttpContext http,
                IdentityDbContext db,
                IAuthProvider provider,
                CancellationToken ct
            ) =>
            {
                if (GetUserId(http) is not { } userId)
                    return Results.Unauthorized();
                var current = AuthEndpoints.GetSessionId(http.User);
                var revoked = await db
                    .Sessions.Where(s =>
                        s.UserId == userId && s.RevokedAt == null && s.Id != current
                    )
                    .ExecuteUpdateAsync(
                        u => u.SetProperty(s => s.RevokedAt, DateTimeOffset.UtcNow),
                        ct
                    );

                // defense in depth: ask the provider to drop ITS sessions too.
                // Best-effort - OUR records are the enforcement point.
                var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
                if (provider is IUserLifecycle lifecycle && user.Provider == provider.Name)
                    try
                    {
                        await lifecycle.RevokeProviderSessionsAsync(user.Subject, ct);
                    }
                    catch (Exception)
                    {
                        // provider-side revocation failing must not block ours
                    }
                return Results.Ok(new { revoked });
            }
        );

        app.MapDelete(
            "/auth/account",
            async (
                HttpContext http,
                IdentityDbContext db,
                IAuthProvider provider,
                IMessageBus bus,
                CancellationToken ct
            ) =>
            {
                if (GetUserId(http) is not { } userId)
                    return Results.Unauthorized();
                var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
                var memberships = await db
                    .Memberships.Where(m => m.UserId == userId)
                    .ToListAsync(ct);

                // an org may not be orphaned by its last manager walking out:
                // transfer management or offboard the org first. The check
                // reads RLS-protected tables, so it runs in per-org scopes.
                var blockers = new List<OrgId>();
                foreach (var membership in memberships)
                    if (
                        await TenantScope.RunAsAsync(
                            http.RequestServices,
                            membership.OrgId,
                            sp =>
                                ManagerGuard.WouldOrphanAsync(
                                    sp.GetRequiredService<IdentityDbContext>(),
                                    membership.OrgId,
                                    userId,
                                    ct
                                )
                        )
                    )
                        blockers.Add(membership.OrgId);
                if (blockers.Count > 0)
                {
                    var names = await db
                        .OrgDirectory.Where(d => blockers.Contains(d.OrgId))
                        .Select(d => d.Name)
                        .ToListAsync(ct);
                    return Results.Conflict(
                        new
                        {
                            error = "you are the last manager of an organization - transfer management or offboard it first",
                            code = "last_manager",
                            organizations = names,
                        }
                    );
                }

                // per-org purge of the user's RLS-protected access rows
                foreach (var membership in memberships)
                {
                    await TenantScope.RunAsAsync(
                        http.RequestServices,
                        membership.OrgId,
                        async sp =>
                        {
                            var scoped = sp.GetRequiredService<IdentityDbContext>();
                            await scoped
                                .MembershipRoles.Where(mr => mr.MembershipId == membership.Id)
                                .ExecuteDeleteAsync(ct);
                            await scoped
                                .GrantExceptions.Where(e => e.UserId == userId)
                                .ExecuteDeleteAsync(ct);
                            await scoped
                                .Set<InvitedRole>()
                                .Where(i => i.Email == user.Email)
                                .ExecuteDeleteAsync(ct);
                        }
                    );
                    await bus.AuditAsync(
                        membership.OrgId,
                        AuditActor.User(userId),
                        "account.deleted",
                        new { }
                    );
                }

                // global rows: memberships, sessions, the user record itself
                await db.Memberships.Where(m => m.UserId == userId).ExecuteDeleteAsync(ct);
                await db.Sessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
                await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);

                // the provider's record goes with ours (GDPR reaches the IdP too)
                if (provider is IUserLifecycle lifecycle && user.Provider == provider.Name)
                {
                    try
                    {
                        await lifecycle.RevokeProviderSessionsAsync(user.Subject, ct);
                    }
                    catch (Exception)
                    {
                        // best-effort: our sessions are already gone
                    }
                    await lifecycle.DeleteUserAsync(user.Subject, ct);
                }

                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.NoContent();
            }
        );

        return app;
    }

    private static Guid? GetUserId(HttpContext http) =>
        Guid.TryParse(http.User.FindFirst(LocIntelClaims.UserId)?.Value, out var id) ? id : null;

    private static OrgId? ActiveOrg(HttpContext http) =>
        Guid.TryParse(http.User.FindFirst(LocIntelClaims.ActiveOrg)?.Value, out var org)
            ? new OrgId(org)
            : null;
}
