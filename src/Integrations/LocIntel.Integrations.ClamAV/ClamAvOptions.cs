namespace LocIntel.Integrations.ClamAV;

/// <summary>Where clamd listens. The daemon must have TCPSocket enabled (clamd.conf).</summary>
public sealed class ClamAvOptions
{
    public required string Host { get; set; }
    public int Port { get; set; } = 3310;

    /// <summary>Whole-scan budget; a slow or hung daemon fails the scan rather than the upload lingering.</summary>
    public int TimeoutSeconds { get; set; } = 60;
}
