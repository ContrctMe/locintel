namespace LocIntel.Modules.Reporting;

public sealed class ReportRegistry(IEnumerable<IReportDefinition> definitions)
{
    private readonly Dictionary<string, IReportDefinition> _definitions = definitions.ToDictionary(
        x => x.Id,
        StringComparer.Ordinal
    );
    public IEnumerable<IReportDefinition> All => _definitions.Values;

    public IReportDefinition? Find(string id, int? version = null) =>
        _definitions.TryGetValue(id, out var definition)
        && (version is null || definition.Version == version)
            ? definition
            : null;
}
