using Microsoft.Extensions.Logging;

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

/// <summary>
/// Outbound SMS port (blueprint: the transport ADR 32 left to forks).
/// Adapters: Twilio in LocIntel.Integrations.Twilio, the local catcher for
/// dev and tests, and NoSmsTransport when SMS is off. Same contract as
/// email: handlers call it from the outbox, never application code.
/// </summary>
public interface ISmsTransport
{
    Task SendAsync(SmsMessage message, CancellationToken ct = default);
}

/// <summary>To is E.164; Body is plain text, kept short by the caller.</summary>
public sealed record SmsMessage(string To, string Body);

/// <summary>Dev/test SMS transport: captures instead of sending.</summary>
public sealed class LocalSmsCatcher : ISmsTransport
{
    private readonly List<SmsMessage> _sent = [];

    public IReadOnlyList<SmsMessage> Sent
    {
        get
        {
            lock (_sent)
                return [.. _sent];
        }
    }

    public Task SendAsync(SmsMessage message, CancellationToken ct = default)
    {
        lock (_sent)
            _sent.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>SMS is off (the default): sends are dropped loudly enough to see in logs, never thrown.</summary>
public sealed class NoSmsTransport(Microsoft.Extensions.Logging.ILogger<NoSmsTransport> logger)
    : ISmsTransport
{
    public Task SendAsync(SmsMessage message, CancellationToken ct = default)
    {
        logger.LogInformation(
            "SMS transport is off; dropping a {Length}-character message",
            message.Body.Length
        );
        return Task.CompletedTask;
    }
}
