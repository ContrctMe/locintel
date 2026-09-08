namespace LocIntel.Modules.Ingest;

public static class IngestLimits
{
    public const int MaxBytes = 4 * 1024 * 1024;
    public const int MaxRows = 10_000;
    public const int MaxStagedRows = 50_000;
    public const int MaxColumns = 16;
    public const int MaxFieldChars = 500;
    public const int MaxConnectors = 20;
}
