using LocIntel.Platform.Kernel;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace LocIntel.Platform.Data;

/// <summary>
/// Resolves the NpgsqlDataSource for a region. There is NO ambient connection
/// string anywhere in the codebase (ADR 35 precondition): all data access asks
/// this resolver, even while v1 has exactly one region.
/// </summary>
public interface IRegionDataSources
{
    NpgsqlDataSource For(RegionId region);
}

/// <summary>v1: one silo. The seam multi-region routing later slots into.</summary>
public sealed class SingleRegionDataSources : IRegionDataSources, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public SingleRegionDataSources(IConfiguration configuration)
    {
        var cs =
            configuration.GetConnectionString("locintel")
            ?? throw new InvalidOperationException(
                "Missing connection string 'locintel'. Set ConnectionStrings__locintel."
            );
        // spatial types are NetTopologySuite end to end (ADR 50): the ADO
        // mapping lives on the data source, the EF mapping in ModulePersistence
        _dataSource = new NpgsqlDataSourceBuilder(cs).UseNetTopologySuite().Build();
    }

    public NpgsqlDataSource For(RegionId region) => _dataSource;

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
