using Microsoft.Extensions.Logging;

namespace LocIntel.Platform.Notifications;

/// <summary>
/// The SMS egress port, mirroring <see cref="INotificationTransport"/> for
/// email (ADR 32). It is a SEAM, not a feature: the template ships an off
/// transport and a dev catcher, and a fork that needs texting writes an
/// adapter (Twilio, SNS, a carrier gateway) and flips
/// <c>Notifications:Sms</c>.
///
/// Deliberately NOT shipped with it: who should receive a text, per-org
/// notification preferences, phone verification, quiet hours, and consent
/// records. Those are product and, for SMS, compliance decisions (opt-in and
/// STOP handling are legal requirements in most jurisdictions) that no
/// template should make on a fork's behalf.
/// </summary>
public interface ISmsTransport
{
    Task SendAsync(SmsMessage message, CancellationToken ct = default);
}

/// <param name="To">E.164 recipient (+15551234567). Validation belongs to the adapter.</param>
/// <param name="Body">Plain text. Long bodies are segmented and billed per segment.</param>
public sealed record SmsMessage(string To, string Body);

/// <summary>
/// The DEFAULT, and the only one allowed in Production without a fork
/// adapter: SMS is off, and a send is logged and dropped rather than
/// throwing. Silence is the right failure mode here - a texting outage must
/// never take down the flow that wanted to text.
/// </summary>
public sealed class NoSmsTransport(ILogger<NoSmsTransport> logger) : ISmsTransport
{
    public Task SendAsync(SmsMessage message, CancellationToken ct = default)
    {
        logger.LogInformation(
            "SMS transport is off; dropped a {Length}-character message",
            message.Body.Length
        );
        return Task.CompletedTask;
    }
}

/// <summary>Dev/test transport: captures instead of sending, like LocalMailCatcher.</summary>
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
