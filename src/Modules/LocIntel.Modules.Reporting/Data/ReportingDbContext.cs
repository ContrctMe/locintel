using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Reporting.Data;

public sealed class ReportingDbContext(
    DbContextOptions<ReportingDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "reporting";
    public DbSet<ReportJob> Jobs => Set<ReportJob>();
    public DbSet<ReportItem> Items => Set<ReportItem>();
    public DbSet<ReportArtifact> Artifacts => Set<ReportArtifact>();

    public DbSet<ReportQuotaEntry> QuotaEntries => Set<ReportQuotaEntry>();

    private static T Parse<T>(string value)
        where T : struct, Enum => Enum.Parse<T>(value, ignoreCase: true);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReportJob>(b =>
        {
            b.ToTable("jobs");
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.OrgId, x.Id });
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ReportType).HasMaxLength(80);
            // The columns predate the enums and keep their exact text. Mode and
            // Selection are stored lowercase, which is also their JSON name.
            b.Property(x => x.Mode)
                .HasConversion(v => v.ToString().ToLowerInvariant(), s => Parse<ReportMode>(s))
                .HasMaxLength(20);
            b.Property(x => x.Selection)
                .HasConversion(v => v.ToString().ToLowerInvariant(), s => Parse<ReportSelection>(s))
                .HasMaxLength(30);
            b.Property(x => x.State).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.ErrorCode).HasMaxLength(80);
            b.Property(x => x.OptionsJson).HasColumnType("jsonb");
            b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasIndex(x => new
            {
                x.OrgId,
                x.RequestedBy,
                x.CreatedAt,
            });
            b.HasIndex(x => new
            {
                x.OrgId,
                x.State,
                x.LeaseUntil,
            });
            b.HasIndex(x => new { x.OrgId, x.MetadataExpiresAt });
            b.HasMany(x => x.Items)
                .WithOne()
                .HasForeignKey(x => new { x.OrgId, x.JobId })
                .HasPrincipalKey(x => new { x.OrgId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Artifacts)
                .WithOne()
                .HasForeignKey(x => new { x.OrgId, x.JobId })
                .HasPrincipalKey(x => new { x.OrgId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ReportItem>(b =>
        {
            b.ToTable("items");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.State).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.ErrorCode).HasMaxLength(80);
            b.Property(x => x.WarningsJson).HasColumnType("jsonb");
            b.Property(x => x.DependenciesJson).HasColumnType("jsonb");
            b.HasIndex(x => new { x.OrgId, x.JobId });
        });
        modelBuilder.Entity<ReportArtifact>(b =>
        {
            b.ToTable("artifacts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Key).HasMaxLength(520);
            b.Property(x => x.Name).HasMaxLength(160);
            b.Property(x => x.ContentType).HasMaxLength(80);
            b.HasIndex(x => new { x.OrgId, x.JobId });
            b.HasIndex(x => x.Key).IsUnique();
        });
        modelBuilder.Entity<ReportQuotaEntry>(b =>
        {
            b.ToTable("quota_entries");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => new { x.OrgId, x.PeriodMonth });
            b.HasIndex(x => new { x.OrgId, x.JobId });
            // No job FK: expiration of report metadata must not refund spent quota.
        });
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
            property.SetColumnName(
                System
                    .Text.RegularExpressions.Regex.Replace(property.Name, "(?<!^)([A-Z])", "_$1")
                    .ToLowerInvariant()
            );
        base.OnModelCreating(modelBuilder);
    }
}
