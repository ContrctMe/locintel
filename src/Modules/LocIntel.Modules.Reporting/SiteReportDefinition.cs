using System.Text.Json;
using LocIntel.Modules.Reporting.Rendering;

namespace LocIntel.Modules.Reporting;

public sealed class SiteReportDefinition(
    ReferenceReportAccess access,
    ReferenceReportRenderer renderer,
    ReportBasemaps basemaps
) : IReportDefinition
{
    public string Id => "site";
    public string Name => "Site report";
    public int Version => 1;
    public bool Aggregate => false;

    public string? ValidateOptions(JsonElement options) =>
        ReferenceReportOptions.Validate(options, basemaps);

    public Task<bool> AuthorizeAsync(
        IReportDefinition.Request request,
        JsonElement? dependencies,
        CancellationToken ct
    ) => access.AuthorizeAsync(request, dependencies, ct);

    public Task<IReportDefinition.Output> RenderAsync(
        IReportDefinition.Request request,
        Stream destination,
        CancellationToken ct
    ) => renderer.RenderAsync(request, false, destination, ct);
}
