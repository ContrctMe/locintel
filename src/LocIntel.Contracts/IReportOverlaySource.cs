using LocIntel.Platform.Kernel;

namespace LocIntel.Contracts;

/// <summary>Spatial-owned data. Coordinates are WGS84; rings preserve polygon holes.</summary>
public interface IReportOverlaySource
{
    Task<IReadOnlyList<Layer>> ReadAsync(
        OrgId org,
        NodeScope scope,
        Guid[] ids,
        CancellationToken ct = default
    );

    public sealed record Layer(Guid Id, string Name, string? Style, double[][][][] Polygons);
}
