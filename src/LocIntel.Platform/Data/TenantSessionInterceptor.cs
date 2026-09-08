using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LocIntel.Platform.Data;

/// <summary>
/// Sets app.org_id as TRANSACTION state (ADR 53): once when a transaction
/// starts, and in the batch of any read that runs outside one, so the RLS
/// policies (USING org_id = current_setting('app.org_id')) see the tenant
/// for exactly that transaction - the explicit one Wolverine's frame or
/// SaveChanges opened, or the implicit one a batch forms - and nothing
/// outlives it.
///
/// It used to be session state, set once when a connection opened. That was
/// right for a connection that belonged to one client for its whole life and
/// wrong behind a transaction-mode pooler, which hands server connections
/// between clients between transactions: tenant B's next statement could run
/// on a connection still carrying tenant A's variable. Transaction state has
/// no such gap, and it costs no round trip - the SET rides in the batch.
///
/// Stateless singleton on purpose: the tenant is read from the CURRENT
/// ModuleDbContext instance via the event data, never captured at options
/// build time (options are cached from the first scope). A context with no
/// tenant sets nothing, and the policies' NULLIF turns "unset" into
/// match-nothing: platform-level work sees no tenant rows, by construction.
/// </summary>
public sealed class TenantSessionInterceptor : DbCommandInterceptor, IDbTransactionInterceptor
{
    public static readonly TenantSessionInterceptor Instance = new();

    private const string Prefix = "SET LOCAL app.org_id = '";

    private TenantSessionInterceptor() { }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        Stamp(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Stamp(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    )
    {
        Stamp(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Stamp(command, eventData);
        return ValueTask.FromResult(result);
    }

    // ---- transactions: one SET LOCAL when a transaction starts, which every
    // command inside it then sees (Wolverine's frames, and every SaveChanges:
    // ModuleDbContext runs them under AutoTransactionBehavior.Always so a
    // write batch is never prefixed - EF's batches address their statements
    // by position and a leading SET would shift every index by one)

    public InterceptionResult<DbTransaction> TransactionStarting(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result
    ) => result;

    public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(result);

    public DbTransaction TransactionStarted(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result
    )
    {
        if (Statement(eventData.Context) is { } sql)
        {
            using var set = connection.CreateCommand();
            set.Transaction = result;
            set.CommandText = sql;
            set.ExecuteNonQuery();
        }
        return result;
    }

    public async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default
    )
    {
        if (Statement(eventData.Context) is { } sql)
        {
            await using var set = connection.CreateCommand();
            set.Transaction = result;
            set.CommandText = sql;
            await set.ExecuteNonQueryAsync(cancellationToken);
        }
        return result;
    }

    public DbTransaction TransactionUsed(
        DbConnection connection,
        TransactionEventData eventData,
        DbTransaction result
    )
    {
        // a transaction handed in from outside (UseTransaction): stamp it the same way
        if (Statement(eventData.Context) is { } sql)
        {
            using var set = connection.CreateCommand();
            set.Transaction = result;
            set.CommandText = sql;
            set.ExecuteNonQuery();
        }
        return result;
    }

    public async ValueTask<DbTransaction> TransactionUsedAsync(
        DbConnection connection,
        TransactionEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default
    )
    {
        if (Statement(eventData.Context) is { } sql)
        {
            await using var set = connection.CreateCommand();
            set.Transaction = result;
            set.CommandText = sql;
            await set.ExecuteNonQueryAsync(cancellationToken);
        }
        return result;
    }

    public InterceptionResult TransactionCommitting(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result
    ) => result;

    public ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(result);

    public void TransactionCommitted(
        DbTransaction transaction,
        TransactionEndEventData eventData
    ) { }

    public Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public InterceptionResult TransactionRollingBack(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result
    ) => result;

    public ValueTask<InterceptionResult> TransactionRollingBackAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(result);

    public void TransactionRolledBack(
        DbTransaction transaction,
        TransactionEndEventData eventData
    ) { }

    public Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public InterceptionResult CreatingSavepoint(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result
    ) => result;

    public ValueTask<InterceptionResult> CreatingSavepointAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(result);

    public void CreatedSavepoint(DbTransaction transaction, TransactionEventData eventData) { }

    public Task CreatedSavepointAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public InterceptionResult RollingBackToSavepoint(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result
    ) => result;

    public ValueTask<InterceptionResult> RollingBackToSavepointAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(result);

    public void RolledBackToSavepoint(DbTransaction transaction, TransactionEventData eventData) { }

    public Task RolledBackToSavepointAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public InterceptionResult ReleasingSavepoint(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result
    ) => result;

    public ValueTask<InterceptionResult> ReleasingSavepointAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(result);

    public void ReleasedSavepoint(DbTransaction transaction, TransactionEventData eventData) { }

    public Task ReleasedSavepointAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public void TransactionFailed(
        DbTransaction transaction,
        TransactionErrorEventData eventData
    ) { }

    public Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    // ---- commands: a read outside any transaction carries its own SET LOCAL
    // in the batch, ahead of the statement. The batch is one implicit
    // transaction block, the setting ends with it, and Npgsql's reader opens
    // on the first statement that returns rows - the statement itself. SET,
    // not set_config, so no result set is added; the value is a Guid we
    // format ourselves, never text from anywhere else (SET takes no
    // parameters). Inside a transaction the transaction's SET already holds.

    private static void Stamp(DbCommand command, CommandEventData eventData)
    {
        if (command.Transaction is not null || Stamped(command))
            return;
        if (Statement(eventData.Context) is { } sql)
            command.CommandText = $"{sql};\n{command.CommandText}";
    }

    private static string? Statement(Microsoft.EntityFrameworkCore.DbContext? context) =>
        context is ModuleDbContext { Tenant.OrgId: { } orgId } ? $"{Prefix}{orgId.Value:D}'" : null;

    private static bool Stamped(DbCommand command) =>
        command.CommandText.StartsWith(Prefix, StringComparison.Ordinal);
}
