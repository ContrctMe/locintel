namespace LocIntel.Platform.Text;

public sealed record CsvLimits(int MaxBytes, int MaxRows, int MaxColumns, int MaxFieldChars);
