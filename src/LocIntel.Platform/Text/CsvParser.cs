namespace LocIntel.Platform.Text;

/// <summary>
/// Minimal RFC-4180-ish CSV: quoted fields, embedded commas/quotes, CRLF or
/// LF. Header row required; header names are lower-cased and trimmed. Shared
/// by every module that stages uploads (site ingest, incident import).
/// </summary>
public static class CsvParser
{
    public static List<Dictionary<string, string>> Parse(string text)
    {
        var lines = SplitRecords(text);
        if (lines.Count == 0)
            return [];
        var headers = lines[0];
        return lines
            .Skip(1)
            .Where(fields => fields.Count > 1 || fields[0].Length > 0)
            .Select(fields =>
                headers
                    .Select((h, i) => (h, v: i < fields.Count ? fields[i] : ""))
                    .ToDictionary(x => x.h.Trim().ToLowerInvariant(), x => x.v)
            )
            .ToList();
    }

    private static List<List<string>> SplitRecords(string text)
    {
        List<List<string>> records = [];
        List<string> fields = [];
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                    quoted = false;
                else
                    current.Append(c);
            }
            else if (c == '"')
                quoted = true;
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                fields.Add(current.ToString());
                current.Clear();
                records.Add(fields);
                fields = [];
            }
            else
                current.Append(c);
        }
        if (current.Length > 0 || fields.Count > 0)
        {
            fields.Add(current.ToString());
            records.Add(fields);
        }
        return records;
    }
}
