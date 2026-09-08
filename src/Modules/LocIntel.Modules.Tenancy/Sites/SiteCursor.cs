using System.Text;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Tenancy.Sites;

/// <summary>
/// A keyset cursor over sites ordered by (name, id) (ADR 51): the last row
/// of a page, opaque to clients. Keyset paging costs the same for page one
/// and page ten thousand, where an offset re-sorts everything it skips.
/// </summary>
public readonly record struct SiteCursor(string Name, SiteId Id, string? Term = null)
{
    /// <summary>
    /// The row after which the next page starts. A search page is ordered by
    /// the matched term first, so its cursor carries the term as well.
    /// </summary>
    public static string Encode(Site last, string? term = null) =>
        Convert
            .ToBase64String(Encoding.UTF8.GetBytes($"{last.Id.Value:N}\n{term}\n{last.Name}"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static bool TryParse(string text, out SiteCursor cursor)
    {
        cursor = default;
        try
        {
            var padded = text.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var parts = decoded.Split('\n', 3);
            if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out var id))
                return false;
            cursor = new SiteCursor(
                parts[2],
                new SiteId(id),
                parts[1].Length == 0 ? null : parts[1]
            );
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
