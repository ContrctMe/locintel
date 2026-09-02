using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Notifications;

namespace LocIntel.Modules.Identity.Users;

/// <summary>
/// Decorator over the real transport (ADR 32): a suppressed address is
/// dropped, not sent and not thrown - throwing would dead-letter the same
/// undeliverable message forever. The drop is deliberate and logged loudly;
/// callers that can tell the USER (contact-link issuance) check the list
/// themselves before ever queueing the send. Singleton over a scope factory
/// because transports are singletons and DbContexts are not. Generic over
/// the inner CONCRETE type so every registration stays by-type - Wolverine
/// refuses lambda-factory registrations (see CLAUDE.md).
/// </summary>
public sealed class SuppressingNotificationTransport<TInner>(
    TInner inner,
    IServiceScopeFactory scopes,
    ILogger<SuppressingNotificationTransport<TInner>> logger
) : INotificationTransport
    where TInner : INotificationTransport
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var email = message.To.Trim().ToLowerInvariant();
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            if (await db.EmailSuppressions.AnyAsync(s => s.Email == email, ct))
            {
                logger.LogWarning(
                    "suppressed email to {Email} ({Subject}): address previously bounced",
                    message.To,
                    message.Subject
                );
                return;
            }
        }
        await inner.SendAsync(message, ct);
    }
}
