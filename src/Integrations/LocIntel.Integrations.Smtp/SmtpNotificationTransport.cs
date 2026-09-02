using LocIntel.Platform.Notifications;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LocIntel.Integrations.Smtp;

/// <summary>
/// The built-in production transport (ADR 32): plain SMTP submission via
/// MailKit, which reaches every mainstream provider (SES, Postmark, Mailgun,
/// SendGrid all take SMTP credentials) and any self-hosted relay without a
/// vendor SDK. Callers are Wolverine handlers behind the outbox - a throw
/// here is retried there, so this class stays connection-per-send simple.
/// </summary>
public sealed class SmtpNotificationTransport(IOptions<SmtpOptions> options)
    : INotificationTransport
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var smtp = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(smtp.FromName ?? smtp.FromAddress, smtp.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = message.HtmlBody is { } htmlBody
            ? new BodyBuilder { TextBody = message.TextBody, HtmlBody = htmlBody }.ToMessageBody()
            : new TextPart("plain") { Text = message.TextBody };

        using var client = new SmtpClient();
        await client.ConnectAsync(
            smtp.Host,
            smtp.Port,
            smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            ct
        );
        if (smtp.UserName is { } user)
            await client.AuthenticateAsync(user, smtp.Password ?? "", ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}
