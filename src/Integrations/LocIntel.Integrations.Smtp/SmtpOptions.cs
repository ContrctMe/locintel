namespace LocIntel.Integrations.Smtp;

public sealed class SmtpOptions
{
    public required string Host { get; set; }
    public int Port { get; set; } = 587;

    /// <summary>Null disables AUTH - relays inside a private network, and test sinks.</summary>
    public string? UserName { get; set; }
    public string? Password { get; set; }

    /// <summary>STARTTLS on the submission port; false only for local sinks.</summary>
    public bool UseStartTls { get; set; } = true;

    public required string FromAddress { get; set; }
    public string? FromName { get; set; }
}
