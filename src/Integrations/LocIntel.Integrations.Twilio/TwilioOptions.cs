namespace LocIntel.Integrations.Twilio;

/// <summary>Notifications:Twilio:* - AccountSid and AuthToken are secrets; From is the E.164 sender or a messaging service id.</summary>
public sealed class TwilioOptions
{
    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string From { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.twilio.com";
}
