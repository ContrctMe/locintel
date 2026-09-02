using LocIntel.Modules.Marketplace.Requests;
using LocIntel.Modules.Marketplace.Vendors;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Marketplace.Data;

/// <summary>
/// The first context with CROSS-ORG rows. The base class's convention adds
/// a single-owner "Tenant" filter to IOrgScoped entities; the two-party
/// and catalog entities here are deliberately not IOrgScoped and get their
/// own "Tenant" filter (same name, so nothing can disable one and not the
/// other), mirrored exactly by the RLS policies in the migration.
/// </summary>
public sealed class MarketplaceDbContext(
    DbContextOptions<MarketplaceDbContext> options,
    ITenantContext tenant
) : ModuleDbContext(options, tenant)
{
    public override string ModuleSchema => "marketplace";

    public DbSet<VendorProfile> Profiles => Set<VendorProfile>();
    public DbSet<VendorCredential> Credentials => Set<VendorCredential>();
    public DbSet<PreferredVendor> Preferred => Set<PreferredVendor>();
    public DbSet<ServiceRequest> Requests => Set<ServiceRequest>();
    public DbSet<RequestEvent> Events => Set<RequestEvent>();
    public DbSet<RequestRecipient> Recipients => Set<RequestRecipient>();
    public DbSet<Quote> Quotes => Set<Quote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("ltree");

        modelBuilder.Entity<VendorProfile>(b =>
        {
            b.ToTable("vendor_profiles");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
            b.Property(x => x.Description).HasColumnName("description");
            b.Property(x => x.Categories).HasColumnName("categories");
            b.Property(x => x.ServiceAreas).HasColumnName("service_areas");
            b.Property(x => x.Latitude).HasColumnName("latitude");
            b.Property(x => x.Longitude).HasColumnName("longitude");
            b.Property(x => x.ServiceRadiusKm).HasColumnName("service_radius_km");
            b.Property(x => x.ContactEmail).HasColumnName("contact_email").HasMaxLength(320);
            b.Property(x => x.ContactPhone).HasColumnName("contact_phone").HasMaxLength(40);
            b.Property(x => x.Published).HasColumnName("published");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.HasIndex(x => x.OrgId).IsUnique();
            b.HasIndex(x => x.Published);
            // catalog read: published to everyone, unpublished to the owner
            b.HasQueryFilter(TenantFilter, p => p.Published || p.OrgId == CurrentOrg);
        });

        modelBuilder.Entity<VendorCredential>(b =>
        {
            b.ToTable("vendor_credentials");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Label).HasColumnName("label").HasMaxLength(200);
            b.Property(x => x.Number).HasColumnName("number").HasMaxLength(100);
            b.Property(x => x.Jurisdiction).HasColumnName("jurisdiction").HasMaxLength(100);
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.OrgId);
            b.HasQueryFilter(
                TenantFilter,
                c => c.OrgId == CurrentOrg || Profiles.Any(p => p.OrgId == c.OrgId && p.Published)
            );
        });

        modelBuilder.Entity<PreferredVendor>(b =>
        {
            b.ToTable("preferred_vendors");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.VendorOrgId).HasColumnName("vendor_org_id");
            b.Property(x => x.Categories).HasColumnName("categories");
            b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);
            b.Property(x => x.Blocked).HasColumnName("blocked");
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => new { x.OrgId, x.VendorOrgId }).IsUnique();
        });

        modelBuilder.Entity<ServiceRequest>(b =>
        {
            b.ToTable("requests");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.VendorOrgId).HasColumnName("vendor_org_id");
            b.Property(x => x.Mode).HasColumnName("mode").HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Category)
                .HasColumnName("category")
                .HasConversion<string>()
                .HasMaxLength(30);
            b.Property(x => x.Urgency)
                .HasColumnName("urgency")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.SiteId).HasColumnName("site_id");
            b.Property(x => x.SiteName).HasColumnName("site_name").HasMaxLength(200);
            b.Property(x => x.SiteTimeZone).HasColumnName("site_time_zone").HasMaxLength(64);
            b.Property(x => x.SiteLatitude).HasColumnName("site_latitude");
            b.Property(x => x.SiteLongitude).HasColumnName("site_longitude");
            b.Property(x => x.SiteCountryCode).HasColumnName("site_country_code").HasMaxLength(2);
            b.Property(x => x.Path).HasColumnName("path");
            b.Property(x => x.RequesterName).HasColumnName("requester_name").HasMaxLength(200);
            b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
            b.Property(x => x.Details).HasColumnName("details");
            b.Property(x => x.SpecJson).HasColumnName("spec").HasColumnType("jsonb");
            b.Property(x => x.StartsAt).HasColumnName("starts_at");
            b.Property(x => x.EndsAt).HasColumnName("ends_at");
            b.Property(x => x.Rrule).HasColumnName("rrule").HasMaxLength(500);
            b.Property(x => x.BudgetAmount).HasColumnName("budget_amount").HasPrecision(14, 2);
            b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
            b.Property(x => x.IncidentId).HasColumnName("incident_id");
            b.Property(x => x.CaseId).HasColumnName("case_id");
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
            b.Property(x => x.ResponseDueAt).HasColumnName("response_due_at");
            b.Property(x => x.EscalatedAt).HasColumnName("escalated_at");
            b.Property(x => x.EscalationCount).HasColumnName("escalation_count");
            b.HasIndex(x => new { x.Status, x.ResponseDueAt });
            b.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
            b.Property(x => x.DeclineReason).HasColumnName("decline_reason").HasMaxLength(1000);
            b.Property(x => x.StartedAt).HasColumnName("started_at");
            b.Property(x => x.CompletedAt).HasColumnName("completed_at");
            b.Property(x => x.CompletionSummary)
                .HasColumnName("completion_summary")
                .HasMaxLength(4000);
            b.Property(x => x.VerifiedAt).HasColumnName("verified_at");
            b.Property(x => x.DisputeReason).HasColumnName("dispute_reason").HasMaxLength(2000);
            b.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
            b.Property(x => x.CancelReason).HasColumnName("cancel_reason").HasMaxLength(1000);
            b.Ignore(x => x.IsTerminal);
            b.HasIndex(x => new
            {
                x.OrgId,
                x.Status,
                x.UpdatedAt,
            });
            b.HasIndex(x => new
            {
                x.VendorOrgId,
                x.Status,
                x.UpdatedAt,
            });
            b.HasIndex(x => x.Path).HasMethod("gist");
            // two-party, plus the vendors a broadcast was sent to (before award)
            b.HasQueryFilter(
                TenantFilter,
                r =>
                    r.OrgId == CurrentOrg
                    || r.VendorOrgId == CurrentOrg
                    || Recipients.Any(x => x.RequestId == r.Id && x.VendorOrgId == CurrentOrg)
            );
        });

        modelBuilder.Entity<RequestEvent>(b =>
        {
            b.ToTable("request_events");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.VendorOrgId).HasColumnName("vendor_org_id");
            b.Property(x => x.RequestId).HasColumnName("request_id");
            b.Property(x => x.ActorOrgId).HasColumnName("actor_org_id");
            b.Property(x => x.ActorId).HasColumnName("actor_id");
            b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Body).HasColumnName("body").HasMaxLength(2000);
            b.Property(x => x.Latitude).HasColumnName("latitude");
            b.Property(x => x.Longitude).HasColumnName("longitude");
            b.Property(x => x.DistanceFromSiteMeters).HasColumnName("distance_from_site_m");
            b.Property(x => x.At).HasColumnName("at");
            b.HasIndex(x => new { x.RequestId, x.At });
            b.HasQueryFilter(
                TenantFilter,
                e => e.OrgId == CurrentOrg || e.VendorOrgId == CurrentOrg
            );
        });

        modelBuilder.Entity<RequestRecipient>(b =>
        {
            b.ToTable("request_recipients");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.VendorOrgId).HasColumnName("vendor_org_id");
            b.Property(x => x.RequestId).HasColumnName("request_id");
            b.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.NotifiedAt).HasColumnName("notified_at");
            b.Property(x => x.RespondedAt).HasColumnName("responded_at");
            b.HasIndex(x => new { x.RequestId, x.VendorOrgId }).IsUnique();
            b.HasQueryFilter(
                TenantFilter,
                x => x.OrgId == CurrentOrg || x.VendorOrgId == CurrentOrg
            );
        });

        modelBuilder.Entity<Quote>(b =>
        {
            b.ToTable("quotes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrgId).HasColumnName("org_id");
            b.Property(x => x.VendorOrgId).HasColumnName("vendor_org_id");
            b.Property(x => x.RequestId).HasColumnName("request_id");
            b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(14, 2);
            b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
            b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
            b.Property(x => x.ValidUntil).HasColumnName("valid_until");
            b.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.SubmittedBy).HasColumnName("submitted_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            b.HasIndex(x => new { x.RequestId, x.VendorOrgId }).IsUnique();
            b.HasQueryFilter(
                TenantFilter,
                x => x.OrgId == CurrentOrg || x.VendorOrgId == CurrentOrg
            );
        });
    }
}
