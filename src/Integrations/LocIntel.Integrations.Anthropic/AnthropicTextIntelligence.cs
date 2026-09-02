using System.Text.Json;
using global::Anthropic;
using global::Anthropic.Models.Messages;
using LocIntel.Platform.Intelligence;
using Microsoft.Extensions.Options;

namespace LocIntel.Integrations.Anthropic;

/// <summary>
/// The Claude adapter for the intelligence port. Suggestions come back as
/// structured JSON (output_config.format) so nothing is parsed out of prose;
/// the model chooses only from the closed sets the caller passes. Low effort
/// by default - classification is routine work. Tenant data goes to the
/// vendor only when this adapter is configured, never through "local".
/// </summary>
public sealed class AnthropicTextIntelligence(IOptions<AnthropicOptions> options)
    : ITextIntelligence
{
    private readonly AnthropicClient _client = new() { ApiKey = options.Value.ApiKey };
    private readonly AnthropicOptions _options = options.Value;

    public string Provider => "anthropic";

    public async Task<IncidentSuggestion> SuggestIncidentAsync(
        IncidentSuggestionInput input,
        CancellationToken ct = default
    )
    {
        var schema = new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(
                new
                {
                    category = new { type = "string", @enum = input.Categories },
                    severity = new { type = "string", @enum = input.Severities },
                    tags = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        maxItems = 5,
                    },
                    summary = new { type = "string" },
                    confidence = new
                    {
                        type = "number",
                        minimum = 0,
                        maximum = 1,
                    },
                }
            ),
            ["required"] = JsonSerializer.SerializeToElement(
                new[] { "category", "severity", "tags", "summary", "confidence" }
            ),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        };
        var response = await _client.Messages.Create(
            new MessageCreateParams
            {
                Model = _options.Model,
                MaxTokens = 1024,
                System =
                    "You classify retail loss-prevention incident reports for a security console. "
                    + "Choose the category and severity strictly from the allowed values. Tags are short lowercase "
                    + "keywords (method, merchandise, descriptors). Summary is one factual sentence. Confidence is your "
                    + "own estimate from 0 to 1. Never invent facts that are not in the report.",
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content = $"Title: {input.Title}\n\nReport:\n{input.Narrative}",
                    },
                ],
                OutputConfig = new OutputConfig
                {
                    Effort = ParseEffort(_options.Effort),
                    Format = new JsonOutputFormat { Schema = schema },
                },
            },
            ct
        );
        var text = string.Concat(
            response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)
        );
        var parsed =
            JsonSerializer.Deserialize<Suggestion>(text, JsonOptions)
            ?? throw new InvalidOperationException("empty suggestion");
        return new IncidentSuggestion(
            parsed.Category,
            parsed.Severity,
            parsed.Tags ?? [],
            parsed.Summary,
            Math.Clamp(parsed.Confidence, 0, 1)
        );
    }

    public async Task<string> DraftCaseBriefAsync(
        CaseBriefInput input,
        CancellationToken ct = default
    )
    {
        var facts = string.Join(
            "\n",
            [
                $"Case: {input.Title}",
                $"Summary: {input.Summary}",
                "Incidents:",
                .. input.Incidents.Select(i => $"- {i}"),
                "Persons and vehicles:",
                .. input.Entities.Select(e => $"- {e}"),
                "Evidence:",
                .. input.Evidence.Select(e => $"- {e}"),
                "Investigator notes:",
                .. input.Notes.Select(n => $"- {n}"),
            ]
        );
        var response = await _client.Messages.Create(
            new MessageCreateParams
            {
                Model = _options.Model,
                MaxTokens = 4096,
                System =
                    "You draft investigation case briefs for a retail loss-prevention team, for internal review and "
                    + "possible referral to police. Write in plain prose with short headed sections: Overview, Timeline, "
                    + "Persons and vehicles, Evidence, Recommended next steps. Use only the facts given; where facts are "
                    + "missing say so rather than guessing. Refer to persons as suspected unless the facts say confirmed.",
                Messages = [new() { Role = Role.User, Content = facts }],
                OutputConfig = new OutputConfig { Effort = ParseEffort(_options.Effort) },
            },
            ct
        );
        return string.Concat(
                response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)
            )
            .Trim();
    }

    private static Effort ParseEffort(string value) =>
        value.ToLowerInvariant() switch
        {
            "medium" => Effort.Medium,
            "high" => Effort.High,
            _ => Effort.Low,
        };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record Suggestion(
        string Category,
        string Severity,
        List<string>? Tags,
        string Summary,
        double Confidence
    );
}
