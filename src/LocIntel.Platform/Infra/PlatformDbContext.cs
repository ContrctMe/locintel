using Microsoft.EntityFrameworkCore;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;

namespace LocIntel.Platform.Infra;

/// <summary>
/// Platform-owned infrastructure tables (not a domain module): currently the
/// idempotency store (ADR 29). Excluded from change-diff audit - infra churn
/// is noise.
/// </summary>
public sealed class PlatformDbContext(
    DbContextOptions<PlatformDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "platform";
    public override bool AuditsOwnChanges => false;

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("idempotency_keys");
            b.HasKey(r => new { r.OrgId, r.Key });
            b.Property(r => r.OrgId).HasColumnName("org_id");
            b.Property(r => r.Key).HasColumnName("key").HasMaxLength(200);
            b.Property(r => r.Endpoint).HasColumnName("endpoint").HasMaxLength(300);
            b.Property(r => r.RequestHash).HasColumnName("request_hash").HasMaxLength(64);
            b.Property(r => r.StatusCode).HasColumnName("status_code");
            b.Property(r => r.ContentType).HasColumnName("content_type").HasMaxLength(100);
            b.Property(r => r.Body).HasColumnName("body");
            b.Property(r => r.CreatedAt).HasColumnName("created_at");
            b.HasIndex(r => r.CreatedAt);
        });
    }
}

/// <summary>
/// ADR 29: (key, org, endpoint, request hash) with the stored response.
/// Null StatusCode = the original request is still in flight (409 to a
/// concurrent retry). Deletion tier 3: expired rows are hard-deleted by the
/// cleanup job via a dedicated RLS delete policy.
/// </summary>
public sealed class IdempotencyRecord : IOrgScoped
{
    public required OrgId OrgId { get; init; }
    public required string Key { get; init; }
    public required string Endpoint { get; init; }
    public required string RequestHash { get; init; }
    public int? StatusCode { get; set; }
    public string? ContentType { get; set; }
    public byte[]? Body { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
