using System.Text.Json;
using LocIntel.Modules.Reporting.Rendering;

namespace LocIntel.Modules.Reporting;

public sealed class AggregateReportDefinition(
    ReferenceReportAccess access,
    ReferenceReportRenderer renderer,
    ReportBasemaps basemaps
) : IReportDefinition
{
    public string Id => "sites-summary";
    public string Name => "Sites summary";
    public int Version => 1;
    public bool Aggregate => true;

    public string? ValidateOptions(JsonElement options)
    {
        var error = ReferenceReportOptions.Validate(options, basemaps);
        if (error is not null)
            return error;
        return ReferenceReportOptions.Parse(options).Photos.Count == 0
            ? null
            : "The summary report accepts maps and overlays; use site reports for photographs.";
    }

    public Task<bool> AuthorizeAsync(
        IReportDefinition.Request request,
        JsonElement? dependencies,
        CancellationToken ct
    ) => access.AuthorizeAsync(request, dependencies, ct);

    public Task<IReportDefinition.Output> RenderAsync(
        IReportDefinition.Request request,
        Stream destination,
        CancellationToken ct
    ) => renderer.RenderAsync(request, true, destination, ct);
}
