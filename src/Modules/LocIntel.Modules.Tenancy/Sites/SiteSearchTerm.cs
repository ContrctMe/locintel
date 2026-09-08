using System.Globalization;
using System.Text;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Tenancy.Sites;

/// <summary>
/// One word of a site's name or city, lower-cased (ADR 51): the search
/// index. Search is "a word starts with", served as a leakproof text range
/// on <c>(org_id, term, name, site_id)</c>, because <c>ILIKE</c> and trigram
/// matching are not leakproof and so never reach an index for the app role
/// under row security. The site's name rides along so a page of results is
/// one ordered walk of that index - matched term, then name - however the
/// matches are spread through the org. Derived on every save
/// (TenancyDbContext), hard-deleted with its site: a join row, deletion
/// tier 3 (ADR 25).
/// </summary>
public sealed class SiteSearchTerm : IOrgScoped
{
    public required OrgId OrgId { get; init; }
    public required SiteId SiteId { get; init; }
    public required string Term { get; init; }

    /// <summary>The site's name at the time of writing: the order results come back in.</summary>
    public required string Name { get; init; }

    /// <summary>Longest term kept; a longer word is truncated, so a prefix still finds it.</summary>
    public const int MaxLength = 120;

    /// <summary>The distinct words of a name and city, folded the way queries fold them.</summary>
    public static IReadOnlyList<string> TermsOf(string name, string? city)
    {
        var terms = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var word in Words(name))
            terms.Add(word);
        if (city is not null)
            foreach (var word in Words(city))
                terms.Add(word);
        return [.. terms];
    }

    /// <summary>What a query types, folded the same way: lower case, letters and digits only.</summary>
    public static IReadOnlyList<string> Words(string text)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                current.Append(Rune.ToLowerInvariant(rune).ToString());
                continue;
            }
            Flush(words, current);
        }
        Flush(words, current);
        return words;
    }

    private static void Flush(List<string> words, System.Text.StringBuilder current)
    {
        if (current.Length == 0)
            return;
        var word = current.ToString();
        words.Add(word.Length > MaxLength ? word[..MaxLength] : word);
        current.Clear();
    }

    /// <summary>
    /// The text range every term starting with <paramref name="prefix"/> falls
    /// in, under the "C" collation: U+10FFFF is the last code point, so no
    /// continuation of the prefix sorts past it.
    /// </summary>
    public static (string lo, string hi) PrefixRange(string prefix) =>
        (prefix, prefix + char.ConvertFromUtf32(0x10FFFF));
}
