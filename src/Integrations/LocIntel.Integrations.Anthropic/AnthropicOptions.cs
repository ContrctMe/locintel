namespace LocIntel.Integrations.Anthropic;

/// <summary>Intelligence:Anthropic:* - the key is a secret (never in appsettings); the model defaults to Claude Opus 5.</summary>
public sealed class AnthropicOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-opus-5";
    public string Effort { get; set; } = "low";
}
