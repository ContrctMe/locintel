using LocIntel.Modules.Identity.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Identity.Users;

public sealed record BounceReport(string Email, string? Reason);

/// <summary>
/// The provider-neutral bounce intake (ADR 32): every email provider can
/// report bounces somewhere - SES via SNS, Postmark and Mailgun via JSON
/// webhooks - and each speaks its own dialect. The template takes ONE tiny
/// shape and a shared-secret header; a fork adapts its provider's webhook
/// into this (a five-line function in their infra), and suppression itself
/// stays provider-independent. No token configured = intake disabled, 404.
/// </summary>
public static class BounceWebhookEndpoint
{
    [Transactional(typeof(IdentityDbContext))]
    [WolverinePost("/notifications/bounce")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public static async Task<IResult> Receive(
        BounceReport report,
        HttpContext http,
        IdentityDbContext db,
        IConfiguration configuration,
        CancellationToken ct
    )
    {
        if (configuration["Notifications:BounceToken"] is not { Length: > 0 } token)
            return Results.NotFound();
        // constant-time: a timing oracle here would let an attacker recover
        // the token and forge suppressions (targeted email deliverability DoS)
        if (
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(
                    http.Request.Headers["X-Bounce-Token"].ToString()
                ),
                System.Text.Encoding.UTF8.GetBytes(token)
            )
        )
            return Results.Unauthorized();
        var email = report.Email.Trim().ToLowerInvariant();
        if (email.Length == 0)
            return Results.BadRequest();
        if (!await db.EmailSuppressions.AnyAsync(s => s.Email == email, ct))
        {
            db.EmailSuppressions.Add(
                new EmailSuppression
                {
                    Id = Guid.CreateVersion7(),
                    Email = email,
                    Reason = report.Reason ?? "bounce",
                }
            );
            await db.SaveChangesAsync(ct);
        }
        return Results.Accepted();
    }
}
