using System.Text.Json;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Reporting;

/// <summary>
/// Fork extension point. Authorize checks current access at admission, generation
/// and download. Output dependencies describe every protected resource actually
/// included; validation and rendering must bound application-specific inputs.
/// </summary>
public interface IReportDefinition
{
    string Id { get; }
    string Name { get; }
    int Version { get; }
    bool Aggregate { get; }
    string? ValidateOptions(JsonElement options);
    Task<bool> AuthorizeAsync(Request request, JsonElement? dependencies, CancellationToken ct);
    Task<Output> RenderAsync(Request request, Stream destination, CancellationToken ct);

    public sealed record Request(
        OrgId Org,
        Guid UserId,
        Guid[] SiteIds,
        ReportSelection Selection,
        JsonElement Options
    );

    public sealed record Output(JsonElement Dependencies, string[] Warnings);
}
