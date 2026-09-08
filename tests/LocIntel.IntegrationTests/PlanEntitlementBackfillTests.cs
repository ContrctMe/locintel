using LocIntel.Modules.Entitlements;
using LocIntel.Modules.Entitlements.Data;
using LocIntel.Platform.Billing;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.IntegrationTests;

public sealed class PlanEntitlementBackfillTests(MigrationDbFixture fixture)
    : IClassFixture<MigrationDbFixture>
{
    [Fact]
    public async Task Upgrade_fills_missing_plan_codes_across_pages_without_changing_custody_or_subscription_truth()
    {
        var module = LocIntel.Api.ModuleCatalog.AllWithPlatform.Single(x =>
            x.DbContextType == typeof(EntitlementsDbContext)
        );
        await using var db = (EntitlementsDbContext)
            ApiFixture.CreateCatalogContext(module, fixture.ConnectionString);
        await db.Database.MigrateAsync();
        var subscriptions = Enumerable
            .Range(0, 205)
            .Select(_ => new OrgSubscription
            {
                Id = Guid.CreateVersion7(),
                OrgId = OrgId.New(),
                PlanId = "growth",
                Status = SubscriptionStatus.Active,
            })
            .ToList();
        subscriptions[0].PlanId = "scale";
        subscriptions[0].Status = SubscriptionStatus.Trialing;
        subscriptions[1].Status = SubscriptionStatus.PastDue;
        subscriptions[2].Status = SubscriptionStatus.Canceled;
        subscriptions[3].PlanId = "retired-fork-plan";
        subscriptions[4].Status = "Unknown";
        db.Subscriptions.AddRange(subscriptions);
        // Model the pre-reporting release: eligible subscriptions already have
        // their old plan bundle, but reports.monthly did not exist in it yet.
        foreach (
            var subscription in subscriptions.Where(s =>
                s.Status
                    is SubscriptionStatus.Active
                        or SubscriptionStatus.Trialing
                        or SubscriptionStatus.PastDue
            )
        )
            if (PlanCatalog.Find(subscription.PlanId) is { } priorPlan)
                foreach (
                    var (code, value) in priorPlan.Entitlements.Where(x =>
                        x.Key != EntitlementCatalog.ReportsMonthly
                    )
                )
                    db.OrgEntitlements.Add(
                        new OrgEntitlement
                        {
                            Id = Guid.CreateVersion7(),
                            OrgId = subscription.OrgId,
                            Code = code,
                            Value = value,
                            Source = $"plan:{priorPlan.Id}",
                        }
                    );
        var before = DateTimeOffset.UtcNow.AddDays(-30);
        foreach (
            var (index, source) in new[] { (5, "operator"), (6, "plan:growth"), (7, "manual") }
        )
            db.OrgEntitlements.Add(
                new OrgEntitlement
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = subscriptions[index].OrgId,
                    Code = EntitlementCatalog.ReportsMonthly,
                    Value = "123",
                    Source = source,
                    UpdatedAt = before,
                }
            );
        var exception = new EntitlementException
        {
            Id = Guid.CreateVersion7(),
            OrgId = subscriptions[8].OrgId,
            Code = EntitlementCatalog.ReportsMonthly,
            Value = "7777",
            Reason = "Existing support exception",
            GrantedBy = Guid.CreateVersion7(),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        };
        db.Exceptions.Add(exception);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var original = await db
            .Subscriptions.IgnoreQueryFilters()
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.PlanId,
                x.Status,
                x.UpdatedAt,
            })
            .ToArrayAsync();
        Assert.Equal(199, await PlanEntitlementBackfill.RunAsync(db));
        Assert.Equal(0, await PlanEntitlementBackfill.RunAsync(db));
        var values = await db
            .OrgEntitlements.IgnoreQueryFilters()
            .Where(x => x.Code == EntitlementCatalog.ReportsMonthly)
            .ToDictionaryAsync(x => x.OrgId);
        Assert.Equal("50000", values[subscriptions[0].OrgId].Value);
        Assert.Equal("5000", values[subscriptions[1].OrgId].Value);
        Assert.Equal("5000", values[subscriptions[^1].OrgId].Value);
        foreach (var index in new[] { 2, 3, 4 })
            Assert.False(values.ContainsKey(subscriptions[index].OrgId));
        foreach (
            var (index, source) in new[] { (5, "operator"), (6, "plan:growth"), (7, "manual") }
        )
        {
            Assert.Equal("123", values[subscriptions[index].OrgId].Value);
            Assert.Equal(source, values[subscriptions[index].OrgId].Source);
            Assert.True(
                (values[subscriptions[index].OrgId].UpdatedAt - before).Duration()
                    < TimeSpan.FromMilliseconds(1)
            );
        }
        Assert.Equal("5000", values[subscriptions[8].OrgId].Value);
        ((TenantContext)db.Tenant).Set(subscriptions[8].OrgId, RegionId.Default);
        var service = new EntitlementsService(db, TimeProvider.System);
        Assert.Equal(
            "7777",
            await service.ValueAsync(subscriptions[8].OrgId, EntitlementCatalog.ReportsMonthly)
        );
        Assert.Equal(exception.Id, (await db.Exceptions.SingleAsync()).Id);
        Assert.Equal(
            original,
            await db
                .Subscriptions.IgnoreQueryFilters()
                .OrderBy(x => x.Id)
                .Select(x => new
                {
                    x.Id,
                    x.PlanId,
                    x.Status,
                    x.UpdatedAt,
                })
                .ToArrayAsync()
        );
    }
}
