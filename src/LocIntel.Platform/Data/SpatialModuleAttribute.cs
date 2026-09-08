namespace LocIntel.Platform.Data;

/// <summary>
/// Marks a module DbContext whose model carries geography columns (ADR 50).
/// The NetTopologySuite EF plugin is enabled per context, not globally: the
/// plugin adds the <c>postgis</c> extension to every model it touches, which
/// would put pending model changes on seven modules that own no spatial data.
/// <see cref="ModulePersistence.Configure"/> reads this wherever a module
/// context is built - the host, the fixtures, the round-trip test - so the
/// decision lives on the context and nowhere else.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SpatialModuleAttribute : Attribute;
