using System.Net.Http.Headers;
using System.Text;
using LocIntel.Platform.Notifications;
using Microsoft.Extensions.Options;

namespace LocIntel.Integrations.Twilio;

/// <summary>
/// Twilio Programmable Messaging over its REST API (one POST per message,
/// basic auth with the account SID and token). No SDK: the surface is one
/// endpoint and a fork should be able to read the whole adapter.
/// </summary>
public sealed class TwilioSmsTransport(HttpClient http, IOptions<TwilioOptions> options)
    : ISmsTransport
{
    public async Task SendAsync(SmsMessage message, CancellationToken ct = default)
    {
        var o = options.Value;
        if (
            string.IsNullOrWhiteSpace(o.AccountSid)
            || string.IsNullOrWhiteSpace(o.AuthToken)
            || string.IsNullOrWhiteSpace(o.From)
        )
            throw new InvalidOperationException(
                "Notifications:Twilio needs AccountSid, AuthToken, and From."
            );
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{o.BaseUrl.TrimEnd('/')}/2010-04-01/Accounts/{o.AccountSid}/Messages.json"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{o.AccountSid}:{o.AuthToken}"))
        );
        var from = o.From.StartsWith("MG", StringComparison.Ordinal)
            ? "MessagingServiceSid"
            : "From";
        request.Content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["To"] = message.To,
                [from] = o.From,
                ["Body"] = message.Body,
            }
        );
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Twilio refused the message ({(int)response.StatusCode}): {body}"
            );
        }
    }
}
