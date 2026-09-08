using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using LocIntel.Modules.Reporting.Rendering;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Pdf.IO;

// Runnable renderer qualification, deliberately not a production report endpoint.
if (args.Length != 1)
    throw new ArgumentException("Usage: LocIntel.Tools.ReportingProof <output-directory>");
var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
ReportFonts.Configure();
var elapsed = Stopwatch.StartNew();
var generatedAt = DateTimeOffset.UtcNow;
var pageCounts = new Dictionary<string, int>();
foreach (var aggregate in new[] { false, true })
{
    var name = aggregate ? "aggregate-reference.pdf" : "site-reference.pdf";
    var document = new Document();
    document.Info.Title = aggregate ? "Selected sites summary" : "Riverside site report";
    document.Info.Author = "LocIntel renderer qualification";
    var normal = document.Styles[StyleNames.Normal]!;
    normal.Font.Name = "Liberation Sans";
    normal.Font.Size = 10;
    normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(6);
    document.Styles[StyleNames.Heading1]!.Font.Size = 22;
    document.Styles[StyleNames.Heading1]!.Font.Color = Colors.DarkSlateBlue;
    document.Styles[StyleNames.Heading2]!.Font.Size = 14;
    document.Styles[StyleNames.Heading2]!.ParagraphFormat.SpaceBefore = Unit.FromPoint(12);
    var section = document.AddSection();
    section.PageSetup.PageFormat = PageFormat.A4;
    section.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
    section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);
    section.PageSetup.LeftMargin = Unit.FromCentimeter(2);
    section.PageSetup.RightMargin = Unit.FromCentimeter(2);
    var header = section.Headers.Primary.AddParagraph("LOCINTEL / REPORTING PROOF");
    header.Format.Font.Size = 8;
    header.Format.Font.Color = Colors.Gray;
    var footer = section.Footers.Primary.AddParagraph();
    footer.Format.Font.Size = 8;
    footer.AddText("Synthetic data | Page ");
    footer.AddPageField();
    footer.AddText(" of ");
    footer.AddNumPagesField();
    section.AddParagraph(document.Info.Title, StyleNames.Heading1);
    section.AddParagraph($"Generated {generatedAt:yyyy-MM-dd HH:mm:ss} UTC");
    section.AddParagraph(
        "Renderer proof only. No live site data, map provider, authorization or background job is exercised."
    );
    section.AddParagraph("Reference map", StyleNames.Heading2);
    AddImage(section, "map.png", 16);
    section.AddParagraph(
        "Synthetic map fixture: site markers and polygon with a hole. Attribution: LocIntel test fixture, no external map data."
    );
    if (aggregate)
    {
        section.AddParagraph("Selected sites - 100 records", StyleNames.Heading2);
        var table = NewTable(section, [1.2, 8.8, 4, 3]);
        var heading = table.AddRow();
        heading.HeadingFormat = true;
        Fill(heading, ["No.", "Site", "City", "Status"]);
        heading.Shading.Color = Colors.LightGray;
        for (var i = 1; i <= 100; i++)
            Fill(
                table.AddRow(),
                [
                    i.ToString(),
                    $"Site {i:000} - Montréal / São Paulo",
                    "Québec",
                    i % 7 == 0 ? "Closed" : "Active",
                ]
            );
    }
    else
    {
        section.AddParagraph("Site details", StyleNames.Heading2);
        var table = NewTable(section, [4, 13]);
        foreach (
            var row in new[]
            {
                new[] { "Name", "Riverside - Montréal" },
                new[] { "Address", "125 Rue de l'Église, Québec" },
                new[] { "Hierarchy", "Canada / Québec / Riverside" },
                new[] { "Status", "Active" },
                new[] { "Hours", "Monday-Friday 09:00-17:00 (America/Toronto)" },
                new[]
                {
                    "Custom attributes",
                    "Accessible entrance; café; façade inspection complete.",
                },
            }
        )
            Fill(table.AddRow(), row);
        section.AddPageBreak();
        section.AddParagraph("Selected photograph", StyleNames.Heading2);
        AddImage(section, "photo.jpg", 16);
        section.AddParagraph(
            "Synthetic JPEG fixture. This checks image embedding, not a real site's appearance."
        );
        section.AddParagraph("Optional visual warning", StyleNames.Heading2);
        section.AddParagraph(
            "[Photograph unavailable] The optional second photograph could not be loaded."
        );
        section.AddParagraph("Long text and pagination", StyleNames.Heading2);
        for (var i = 1; i <= 24; i++)
            section.AddParagraph(
                $"Observation {i:00}: "
                    + string.Join(
                        " ",
                        Enumerable.Repeat(
                            "The accessible entrance is on the north side of the building.",
                            4
                        )
                    )
            );
    }
    var renderer = new PdfDocumentRenderer { Document = document };
    renderer.RenderDocument();
    var path = Path.Combine(output, name);
    renderer.PdfDocument.Save(path);
    using var reopened = PdfReader.Open(path, PdfDocumentOpenMode.Import);
    if (reopened.PageCount < 2)
        throw new InvalidOperationException("Pagination proof must produce multiple pages.");
    pageCounts[name] = reopened.PageCount;
    renderer.PdfDocument.Dispose();
}

// Write from files rather than retaining a batch's PDFs and ZIP in RAM.
var zipPath = Path.Combine(output, "reference-bundle.zip");
using (var zipFile = File.Create(zipPath))
using (var zip = new ZipArchive(zipFile, ZipArchiveMode.Create))
{
    foreach (var name in pageCounts.Keys)
        zip.CreateEntryFromFile(Path.Combine(output, name), name);
    using var manifest = zip.CreateEntry("manifest.json").Open();
    JsonSerializer.Serialize(
        manifest,
        new
        {
            generatedAt,
            files = pageCounts,
            warnings = new[] { "Optional second photograph unavailable (fixture)." },
        }
    );
}
using (var zip = ZipFile.OpenRead(zipPath))
{
    if (zip.Entries.Count != 3 || zip.Entries.Any(e => e.Length == 0))
        throw new InvalidOperationException("Bundle entries are missing or empty.");
}
Console.WriteLine(
    JsonSerializer.Serialize(
        new
        {
            runtime = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            elapsedMilliseconds = elapsed.ElapsedMilliseconds,
            peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
            pageCounts,
            zipBytes = new FileInfo(zipPath).Length,
        }
    )
);

static void AddImage(Section section, string name, double widthCm)
{
    var bytes = ReportImages.Normalize(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", name))
    );
    var image = section.AddImage("base64:" + Convert.ToBase64String(bytes));
    image.LockAspectRatio = true;
    image.Width = Unit.FromCentimeter(widthCm);
}

static Table NewTable(Section section, double[] widths)
{
    var table = section.AddTable();
    table.Borders.Width = Unit.FromPoint(0.3);
    table.Borders.Color = Colors.LightGray;
    table.TopPadding = Unit.FromPoint(4);
    table.BottomPadding = Unit.FromPoint(4);
    foreach (var width in widths)
        table.AddColumn(Unit.FromCentimeter(width));
    return table;
}

static void Fill(Row row, string[] values)
{
    for (var i = 0; i < values.Length; i++)
        row.Cells[i].AddParagraph(values[i]);
}
