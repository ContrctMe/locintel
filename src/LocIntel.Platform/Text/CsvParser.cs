namespace LocIntel.Platform.Text;

/// <summary>Bounded CSV parsing shared by upload consumers. Headers are trimmed, lowercase and unique.</summary>
public static class CsvParser
{
    public static List<Dictionary<string, string>> Parse(string text, CsvLimits limits)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(text) > limits.MaxBytes)
            throw new InvalidDataException("CSV exceeds byte limit.");
        var lines = SplitRecords(text, limits);
        if (lines.Count == 0)
            return [];
        var headers = lines[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        if (headers.Any(string.IsNullOrEmpty) || headers.Distinct().Count() != headers.Count)
            throw new InvalidDataException("CSV headers must be nonempty and distinct.");
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

    private static List<List<string>> SplitRecords(string text, CsvLimits limits)
    {
        List<List<string>> records = [];
        List<string> fields = [];
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (
                current.Length > limits.MaxFieldChars
                || fields.Count >= limits.MaxColumns
                || records.Count > limits.MaxRows
            )
                throw new InvalidDataException("CSV exceeds row, column or field limits.");
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
        if (
            current.Length > limits.MaxFieldChars
            || fields.Count > limits.MaxColumns
            || records.Count > limits.MaxRows + 1
            || quoted
        )
            throw new InvalidDataException(
                "CSV exceeds its limits or has an unclosed quoted field."
            );
        return records;
    }
}
