using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LocIntel.Platform.Data;

/// <summary>
/// Sets app.org_id on the Postgres session when a connection opens, so RLS
/// policies (USING org_id = current_setting('app.org_id')::uuid) apply.
///
/// Stateless singleton on purpose: the tenant is read from the CURRENT
/// ModuleDbContext instance via the event data. Capturing an ITenantContext at
/// options-build time is a bug - DbContext options can be cached from the
/// first (startup/codegen) scope, freezing an empty tenant forever.
///
/// The GUC is set on EVERY open - the tenant's org id, or '' when no tenant
/// is resolved (NULLIF in the policies turns '' into match-nothing). Setting
/// it unconditionally is the invariant: a pooled connection's previous
/// borrower can never leak context into this one, by construction rather than
/// by trusting the pool's reset behavior (ADR 38). Transaction-scoped
/// SET LOCAL would be stronger still, but Wolverine's transactional frames
/// own transaction lifecycles here - per-request ambient transactions would
/// fight them, so the always-set session GUC is the deliberate mechanic.
/// </summary>
public sealed class TenantSessionInterceptor : DbConnectionInterceptor
{
    public static readonly TenantSessionInterceptor Instance = new();

    private TenantSessionInterceptor() { }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = MakeCommand(connection, OrgIdOf(eventData));
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        await using var cmd = MakeCommand(connection, OrgIdOf(eventData));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Guid? OrgIdOf(ConnectionEndEventData eventData) =>
        eventData.Context is ModuleDbContext { Tenant.OrgId: { } orgId } ? orgId.Value : null;

    private static DbCommand MakeCommand(DbConnection connection, Guid? orgId)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.org_id', $1, false)";
        var p = cmd.CreateParameter();
        p.Value = orgId?.ToString() ?? "";
        cmd.Parameters.Add(p);
        return cmd;
    }
}
