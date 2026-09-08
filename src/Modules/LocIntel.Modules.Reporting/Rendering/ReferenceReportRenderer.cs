using System.Globalization;
using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace LocIntel.Modules.Reporting.Rendering;

public sealed class ReferenceReportRenderer(
    IReportRequester requesters,
    IScopeResolver scopes,
    ISiteSource sites,
    IReportFileSource files,
    IReportOverlaySource overlays,
    IObjectStore storage,
    ReferenceReportAccess access,
    ReportBasemaps basemaps,
    ReportMap maps
)
{
    public async Task<IReportDefinition.Output> RenderAsync(
        IReportDefinition.Request request,
        bool aggregate,
        Stream destination,
        CancellationToken ct
    )
    {
        if (!await access.AuthorizeAsync(request, null, ct))
            throw new UnauthorizedAccessException();
        var user =
            await requesters.GetActiveAsync(request.Org, request.UserId, ct)
            ?? throw new UnauthorizedAccessException();
        var scope = ReportAccess.Intersect(
            await scopes.ScopeForAsync(user, Capabilities.SitesRead, ct),
            await scopes.ScopeForAsync(user, Capabilities.ReportsGenerate, ct)
        );
        var selected = await sites.SelectAsync(
            request.Org,
            scope,
            request.SiteIds,
            request.SiteIds.Length,
            ct
        );
        if (
            selected.Count != request.SiteIds.Length
            || selected.Count == 0
            || !aggregate && selected.Count != 1
        )
            throw new UnauthorizedAccessException();
        if (JsonSerializer.SerializeToUtf8Bytes(selected).Length > 300_000)
            throw new InvalidDataException("Report text exceeds the reference layout budget.");
        var options = ReferenceReportOptions.Parse(request.Options);
        var warnings = new List<string>();
        var usedPhotos = new List<Guid>();
        var usedOverlays = new List<Guid>();
        ReportFonts.Configure();
        var document = new Document();
        document.Info.Title = aggregate ? "Sites summary" : "Site report";
        document.Info.Author = "LocIntel";
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = ReportFonts.Family;
        normal.Font.Size = 10;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(6);
        document.Styles[StyleNames.Heading1]!.Font.Size = 22;
        document.Styles[StyleNames.Heading2]!.Font.Size = 14;
        document.Styles[StyleNames.Heading2]!.ParagraphFormat.KeepWithNext = true;
        document.Styles[StyleNames.Heading2]!.ParagraphFormat.SpaceBefore = Unit.FromPoint(12);
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(2);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 8;
        footer.AddText("Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
        section.AddParagraph(document.Info.Title, StyleNames.Heading1);
        var now = DateTimeOffset.UtcNow;
        section.AddParagraph(
            $"Generated {now:yyyy-MM-dd HH:mm:ss} UTC. Data read during generation."
        );
        section.AddParagraph(
            request.Selection switch
            {
                ReportSelection.Organization =>
                    "Organization-wide selection captured at submission.",
                ReportSelection.Accessible => "Accessible sites selection captured at submission.",
                _ => "Explicitly selected sites captured at submission.",
            }
        );
        if (aggregate)
        {
            section.AddParagraph($"{selected.Count} sites", StyleNames.Heading2);
            var table = Table(section, [6, 5, 3, 3]);
            var heading = table.AddRow();
            heading.HeadingFormat = true;
            Fill(heading, ["Site", "Hierarchy", "City", "Status"]);
            heading.Shading.Color = Colors.LightGray;
            foreach (var site in selected)
            {
                ct.ThrowIfCancellationRequested();
                Fill(table.AddRow(), [site.Name, site.Hierarchy, site.City ?? "—", site.Status]);
            }
        }
        else
        {
            var site = selected[0];
            section.AddParagraph(site.Name, StyleNames.Heading2);
            var table = Table(section, [4, 13]);
            Fill(table.AddRow(), ["Site ID", site.Id.ToString()]);
            Fill(
                table.AddRow(),
                [
                    "Address",
                    string.Join(
                        ", ",
                        new[] { site.Address, site.City, site.PostalCode, site.Country }.Where(x =>
                            !string.IsNullOrWhiteSpace(x)
                        )
                    ),
                ]
            );
            Fill(table.AddRow(), ["Hierarchy", site.Hierarchy]);
            Fill(table.AddRow(), ["Status", site.Status]);
            Fill(table.AddRow(), ["Time zone", site.TimeZone]);
            section.AddParagraph("Custom attributes", StyleNames.Heading2);
            using var attributes = JsonDocument.Parse(site.AttributesJson);
            if (
                attributes.RootElement.ValueKind == JsonValueKind.Object
                && attributes.RootElement.EnumerateObject().Any()
            )
            {
                var attrs = Table(section, [6, 11]);
                foreach (var attribute in attributes.RootElement.EnumerateObject())
                    Fill(attrs.AddRow(), [attribute.Name, attribute.Value.ToString()]);
            }
            else
                section.AddParagraph("No custom attributes recorded.");
            var zone = TimeZoneInfo.FindSystemTimeZoneById(site.TimeZone);
            var from = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
            var through = from.AddDays(6);
            section.AddParagraph(
                $"Operating windows: {from:yyyy-MM-dd} to {through:yyyy-MM-dd}",
                StyleNames.Heading2
            );
            var windows = await sites.HoursAsync(request.Org, scope, [site.Id], from, through, ct);
            section.AddParagraph(
                "Materialized operating windows in the site's time zone; no windows listed does not establish that a site is closed."
            );
            var hours = Table(section, [5, 6, 6]);
            Fill(hours.AddRow(), ["Business date", "Starts (local)", "Ends (local)"]);
            foreach (var window in windows)
                Fill(
                    hours.AddRow(),
                    [
                        window.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        TimeZoneInfo
                            .ConvertTime(window.StartsAtUtc, zone)
                            .ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture),
                        TimeZoneInfo
                            .ConvertTime(window.EndsAtUtc, zone)
                            .ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture),
                    ]
                );
            if (windows.Count == 0)
                section.AddParagraph(
                    "No materialized operating windows available for this period."
                );
        }
        section.AddParagraph(aggregate ? "Overview map" : "Location map", StyleNames.Heading2);
        var provider = basemaps.Find(options.MapProvider);
        if (provider is null)
            Warn("Map unavailable: no report basemap configured or selected.");
        else
        {
            var layers = await overlays.ReadAsync(
                request.Org,
                await scopes.ScopeForAsync(user, Capabilities.OverlaysRead, ct),
                options.OverlayIds,
                ct
            );
            if (layers.Count != options.OverlayIds.Length)
                throw new UnauthorizedAccessException();
            try
            {
                var bytes = await maps.RenderAsync(provider, options.MapZoom, selected, layers, ct);
                AddImage(section, bytes);
                section.AddParagraph(provider.Attribution).Format.KeepWithNext = true;
                var extent = section.AddParagraph(
                    "Extent fits captured sites; maximum requested zoom " + options.MapZoom + "."
                );
                extent.Format.KeepWithNext = layers.Count > 0;
                if (layers.Count > 0)
                    section.AddParagraph(
                        "Selected overlays: " + string.Join(", ", layers.Select(x => x.Name))
                    );
                usedOverlays.AddRange(layers.Select(x => x.Id));
            }
            catch (Exception ex)
                when (ex is HttpRequestException or IOException or InvalidDataException
                    || ex is OperationCanceledException && !ct.IsCancellationRequested
                )
            {
                Warn("Map unavailable: the configured provider or map image could not be loaded.");
            }
        }
        var photoIds = options.PhotosFor(request.SiteIds);
        if (photoIds.Length > 0)
        {
            if (!await scopes.CanAsync(user, Capabilities.FilesRead, ct))
                throw new UnauthorizedAccessException();
            var photographs = await files.ReadAsync(request.Org, photoIds, ct);
            if (photographs.Count != photoIds.Length)
                throw new UnauthorizedAccessException();
            section.AddParagraph("Selected photographs", StyleNames.Heading2);
            long imageBytes = 0;
            foreach (var photo in photographs)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await using var source = await storage.OpenReadAsync(photo.Key, ct);
                    var bytes = await ReportImages.ReadAsync(source, ct);
                    imageBytes += bytes.Length;
                    if (imageBytes > 16 * 1024 * 1024)
                        throw new InvalidDataException("Report image budget exceeded.");
                    AddImage(section, bytes);
                    usedPhotos.Add(photo.Id);
                    section.AddParagraph("Photograph " + photo.Id);
                }
                catch (Exception ex)
                    when (ex is IOException or HttpRequestException or InvalidDataException)
                {
                    Warn(
                        "Photograph "
                            + photo.Id
                            + " unavailable: image could not be loaded within report limits."
                    );
                }
            }
        }
        ct.ThrowIfCancellationRequested();
        var renderer = new PdfDocumentRenderer { Document = document };
        try
        {
            renderer.RenderDocument();
            ct.ThrowIfCancellationRequested();
            renderer.PdfDocument.Save(destination, closeStream: false);
        }
        finally
        {
            renderer.PdfDocument?.Dispose();
        }
        return new IReportDefinition.Output(
            JsonSerializer.SerializeToElement(
                new ReportVisualDependencies([.. usedPhotos], [.. usedOverlays])
            ),
            [.. warnings]
        );

        void Warn(string warning)
        {
            warnings.Add(warning);
            section.AddParagraph("[" + warning + "]");
        }
    }

    private static void AddImage(Section section, byte[] bytes)
    {
        var paragraph = section.AddParagraph();
        paragraph.Format.KeepWithNext = true;
        var image = paragraph.AddImage("base64:" + Convert.ToBase64String(bytes));
        image.LockAspectRatio = true;
        // Bound portrait height as well as width to stay inside the printable page.
        // The header carries the dimensions: decoding every pixel a second time
        // only to compute an aspect ratio doubled the work for every image.
        using var stream = new MemoryStream(bytes, writable: false);
        using var codec =
            SkiaSharp.SKCodec.Create(stream) ?? throw new InvalidDataException("Invalid image.");
        var size = codec.Info;
        image.Width = Unit.FromCentimeter(Math.Min(16, 20d * size.Width / size.Height));
    }

    private static Table Table(Section section, double[] widths)
    {
        var table = section.AddTable();
        table.Borders.Width = Unit.FromPoint(0.3);
        table.Borders.Color = Colors.LightGray;
        table.TopPadding = table.BottomPadding = Unit.FromPoint(4);
        foreach (var width in widths)
            table.AddColumn(Unit.FromCentimeter(width));
        return table;
    }

    private static void Fill(Row row, string[] values)
    {
        for (int i = 0; i < values.Length; i++)
            row.Cells[i].AddParagraph(values[i]);
    }
}
