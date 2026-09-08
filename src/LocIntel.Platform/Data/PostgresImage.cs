namespace LocIntel.Platform.Data;

/// <summary>
/// The one PostgreSQL image reference (ADR 50). PostGIS is a template
/// dependency, and the official postgis/postgis images publish no arm64
/// manifests, so the template pins the multi-arch community build by digest.
/// The AppHost links this file as source, the integration fixtures reference
/// it, and <c>tools/postgres-image.sh</c> carries the same string for the
/// shell scripts - an architecture test keeps the two in step. A fork that
/// prefers a first-party image changes these constants and that file.
/// </summary>
public static class PostgresImage
{
    public const string Repository = "imresamu/postgis";
    public const string Tag = "17-3.5-alpine";

    /// <summary>Manifest-list digest: resolves to linux/amd64 or linux/arm64.</summary>
    public const string Sha256 = "2b52785e156e5fe881c0841a8032694bbdf22a46e42a3e26aac6eebb03ab182c";

    /// <summary>What docker and Testcontainers pull: tag for humans, digest for truth.</summary>
    public const string Reference = Repository + ":" + Tag + "@sha256:" + Sha256;
}
