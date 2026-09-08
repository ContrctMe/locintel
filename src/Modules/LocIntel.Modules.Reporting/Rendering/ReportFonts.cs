using PdfSharp.Fonts;

namespace LocIntel.Modules.Reporting.Rendering;

/// <summary>Process-wide PDFsharp font setup. Forks can install their resolver before first rendering.</summary>
public sealed class ReportFonts : IFontResolver
{
    public const string Family = "Liberation Sans";
    private static readonly Lock Sync = new();
    private static bool initialized;

    public static void Configure()
    {
        lock (Sync)
        {
            if (initialized)
                return;
            GlobalFontSettings.FontResolver ??= new ReportFonts();
            MigraDoc.PredefinedFontsAndChars.ErrorFontName = Family;
            MigraDoc.PredefinedFontsAndChars.Bullets.Level1FontName = Family;
            MigraDoc.PredefinedFontsAndChars.Bullets.Level2FontName = Family;
            MigraDoc.PredefinedFontsAndChars.Bullets.Level3FontName = Family;
            initialized = true;
        }
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (familyName != Family)
            throw new InvalidOperationException("Unconfigured report font family.");
        return new FontResolverInfo(
            "LiberationSans-"
                + (
                    (bold, italic) switch
                    {
                        (true, true) => "BoldItalic",
                        (true, false) => "Bold",
                        (false, true) => "Italic",
                        _ => "Regular",
                    }
                )
        );
    }

    public byte[] GetFont(string faceName)
    {
        using var input =
            typeof(ReportFonts).Assembly.GetManifestResourceStream(
                $"LocIntel.Modules.Reporting.Rendering.Fonts.{faceName}.ttf"
            ) ?? throw new InvalidOperationException("Missing bundled report font.");
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
}
