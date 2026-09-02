using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;

namespace LocIntel.Platform.Audit;

/// <summary>
/// Row-level change capture (ADR 12), automatic for every module: on
/// SavingChanges, materialize before/after diffs for added/modified/deleted
/// entities and append them to the SAME context - they commit or roll back
/// with the change they describe. Redacted properties log that they changed,
/// never their values.
/// </summary>
/// <remarks>
/// SINGLETON by design: DbContext options are singletons, so anything they
/// capture must be too. Actor comes from the singleton IPrincipalAccessor;
/// tenant is read at save time from the CONTEXT instance (read-time rule).
/// </remarks>
public sealed class AuditSaveChangesInterceptor(IPrincipalAccessor accessor)
    : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is ModuleDbContext { AuditsOwnChanges: true } context)
            Capture(context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        if (eventData.Context is ModuleDbContext { AuditsOwnChanges: true } context)
            Capture(context);
        return base.SavingChanges(eventData, result);
    }

    private void Capture(ModuleDbContext context)
    {
        var (tier, actorId, label) = accessor.Current switch
        {
            Principal.User u => ("user", (Guid?)u.UserId, u.Email),
            Principal.Contact c => ("contact", c.ContactId, null),
            Principal.Guest => ("guest", null, null),
            _ => ("system", null, null),
        };
        // message-scope work with an envelope tenant but no principal is system work
        if (tier == "guest" && accessor.Current is Principal.Guest { Org: null })
            tier = "system";

        var now = DateTimeOffset.UtcNow;
        List<AuditChangeLog>? entries = null;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (
                entry.Entity is AuditChangeLog
                || entry.State
                    is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)
            )
                continue;

            var diff = BuildDiff(entry);
            if (diff.Count == 0 && entry.State == EntityState.Modified)
                continue;

            (entries ??= []).Add(
                new AuditChangeLog
                {
                    Id = Guid.CreateVersion7(),
                    OrgId =
                        (entry.Entity as IOrgScoped)?.OrgId.Value ?? context.Tenant.OrgId?.Value,
                    ActorTier = tier,
                    ActorId = actorId,
                    ActorLabel = label,
                    SchemaName = entry.Metadata.GetSchema() ?? context.ModuleSchema,
                    TableName = entry.Metadata.GetTableName() ?? entry.Metadata.ShortName(),
                    RowId = string.Join(
                        '|',
                        entry
                            .Properties.Where(p => p.Metadata.IsPrimaryKey())
                            .Select(p =>
                                p.CurrentValue?.ToString() ?? p.OriginalValue?.ToString() ?? ""
                            )
                    ),
                    Operation = entry.State.ToString().ToLowerInvariant(),
                    Diff = JsonSerializer.Serialize(diff),
                    OccurredAt = now,
                }
            );
        }
        if (entries is not null)
            context.Set<AuditChangeLog>().AddRange(entries);
    }

    private static Dictionary<string, object?[]> BuildDiff(EntityEntry entry)
    {
        var diff = new Dictionary<string, object?[]>();
        foreach (var property in entry.Properties)
        {
            if (property.Metadata.IsPrimaryKey())
                continue;
            var redacted =
                property.Metadata.PropertyInfo?.IsDefined(
                    typeof(AuditRedactedAttribute),
                    inherit: false
                ) == true;
            object? Mask(object? value) => redacted && value is not null ? "***" : Plain(value);
            switch (entry.State)
            {
                case EntityState.Added:
                    diff[property.Metadata.Name] = [null, Mask(property.CurrentValue)];
                    break;
                case EntityState.Deleted:
                    diff[property.Metadata.Name] = [Mask(property.OriginalValue), null];
                    break;
                case EntityState.Modified
                    when property.IsModified
                        && !Equals(property.OriginalValue, property.CurrentValue):
                    diff[property.Metadata.Name] =
                    [
                        Mask(property.OriginalValue),
                        Mask(property.CurrentValue),
                    ];
                    break;
            }
        }
        return diff;
    }

    private static object? Plain(object? value) =>
        value switch
        {
            null => null,
            OrgId org => org.Value,
            SiteId site => site.Value,
            RegionId region => region.Value,
            Microsoft.EntityFrameworkCore.LTree ltree => ltree.ToString(),
            string or bool or int or long or double or decimal or Guid => value,
            _ => value.ToString(),
        };
}
