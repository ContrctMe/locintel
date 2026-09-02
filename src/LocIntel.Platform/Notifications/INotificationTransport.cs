namespace LocIntel.Platform.Notifications;

/// <summary>
/// Outbound notification port (ADR 32). Adapters: SES/SendGrid/Postmark/SMTP
/// in forks; the local catcher for dev and tests. Sends are enqueued as
/// Wolverine messages through the outbox, so a notification is transactional
/// with the change that caused it - handlers call this transport, application
/// code never does.
/// </summary>
public interface INotificationTransport
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed record EmailMessage(
    string To,
    string Subject,
    string TextBody,
    string? HtmlBody = null
);

/// <summary>
/// Dev/test transport: captures instead of sending. Email is on the auth
/// critical path (magic links, ADR 7/32) - a fork must replace this before
/// contact links work outside dev.
/// </summary>
public sealed class LocalMailCatcher : INotificationTransport
{
    private readonly List<EmailMessage> _sent = [];
    public IReadOnlyList<EmailMessage> Sent
    {
        get
        {
            lock (_sent)
                return [.. _sent];
        }
    }

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        lock (_sent)
            _sent.Add(message);
        return Task.CompletedTask;
    }
}
