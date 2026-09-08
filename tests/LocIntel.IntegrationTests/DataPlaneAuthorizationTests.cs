using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Identity.Access;
using LocIntel.Modules.Identity.Data;
using LocIntel.Modules.Ingest.Data;
using LocIntel.Modules.Storage;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Secrets;
using LocIntel.Platform.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace LocIntel.IntegrationTests;

public sealed class DataPlaneAuthorizationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Delayed_exports_cannot_recreate_files_after_organization_purge()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var org = OrgId.New();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(org, RegionId.Default);
        var db = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        db.PurgedOrganizations.Add(new PurgedFileOrganization { OrgId = org });
        await db.SaveChangesAsync();
        var admission = Assert.IsType<Guid>(
            await ExportAdmission.TryReserveAsync(db, org, default)
        );
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await ExportOrgDataHandler.Handle(
            new ExportOrgData(Guid.NewGuid(), admission),
            db,
            scope.ServiceProvider.GetServices<IOrgDataExporter>(),
            store,
            tenant,
            bus,
            default
        );
        await ExportAuditTrailHandler.Handle(
            new ExportAuditTrail(Guid.NewGuid()),
            db,
            scope.ServiceProvider.GetRequiredService<IAuditTrailExporter>(),
            store,
            tenant,
            bus,
            default
        );
        Assert.False(await db.Files.AnyAsync());
        Assert.Equal(
            0,
            await CapacityReservations.PendingAsync(db, org, ExportAdmission.Code, default)
        );
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Export_admission_is_shared_by_audit_self_service_and_target_org_operator_exports()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var db = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        Guid admission;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            admission = Assert.IsType<Guid>(
                await ExportAdmission.TryReserveAsync(db, fixture.OrgA, default)
            );
            await tx.CommitAsync();
        }
        try
        {
            using var owner = await fixture.LoginAsync(ApiFixture.UserA);
            using var op = await fixture.OperatorClient();
            foreach (var path in new[] { "/api/org/export", "/api/audit/export" })
                Assert.Equal(
                    HttpStatusCode.TooManyRequests,
                    (await owner.PostAsync(path, null)).StatusCode
                );
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (
                    await op.PostAsync($"/api/operator/orgs/{fixture.OrgA.Value}/export", null)
                ).StatusCode
            );
            // The operator must reserve against the target tenant, not its platform org.
            Assert.Equal(
                HttpStatusCode.Accepted,
                (
                    await op.PostAsync($"/api/operator/orgs/{fixture.OrgB.Value}/export", null)
                ).StatusCode
            );
        }
        finally
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await CapacityReservations.ConsumeAsync(
                db,
                fixture.OrgA,
                ExportAdmission.Code,
                admission,
                default
            );
            await tx.CommitAsync();
        }
    }

    [Fact]
    public async Task Export_materialization_refuses_excess_rows_and_bytes_before_archive_assembly()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var db = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            db
                .Database.SqlQuery<int>(
                    $"SELECT generate_series(1, {ExportLimits.MaxRows + 1}) AS \"Value\""
                )
                .ToBoundedExportListAsync(default)
        );
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            db
                .Database.SqlQuery<string>(
                    $"SELECT repeat('x', {ExportLimits.MaxSectionBytes + 1}) AS \"Value\""
                )
                .ToBoundedExportListAsync(default)
        );
        Assert.Throws<InvalidDataException>(() =>
            ExportLimits.AddSection(ExportLimits.MaxArchiveBytes, "x")
        );
    }

    [Fact]
    public async Task Changing_connector_origin_requires_replacement_credentials()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var created = await owner.PostAsJsonAsync(
            "/api/connectors",
            new
            {
                name = "origin-bound",
                url = "https://supplier.example/sites",
                apiKey = "supplier-key",
            }
        );
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        foreach (var replacement in new string?[] { null, "", " " })
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (
                    await owner.PutAsJsonAsync(
                        $"/api/connectors/{id}",
                        new
                        {
                            name = "origin-bound",
                            url = "https://attacker.example/sites",
                            apiKey = replacement,
                        }
                    )
                ).StatusCode
            );

        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
        var connector = await db.Connectors.SingleAsync(c => c.Id == id);
        Assert.Equal("https://supplier.example/sites", connector.Url);
        var originalCredentials = connector.EncryptedCredentials.ToArray();
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await owner.PutAsJsonAsync(
                    $"/api/connectors/{id}",
                    new { name = "origin-bound", url = "https://SUPPLIER.example:443/updated" }
                )
            ).StatusCode
        );
        await db.Entry(connector).ReloadAsync();
        Assert.Equal(originalCredentials, connector.EncryptedCredentials);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await owner.PutAsJsonAsync(
                    $"/api/connectors/{id}",
                    new
                    {
                        name = "origin-bound",
                        url = "https://replacement.example/sites",
                        apiKey = "replacement-key",
                    }
                )
            ).StatusCode
        );
        await db.Entry(connector).ReloadAsync();
        Assert.Equal("https://replacement.example/sites", connector.Url);
        Assert.Equal(
            "replacement-key",
            await EnvelopeCrypto.DecryptAsync(
                connector.EncryptedCredentials,
                scope.ServiceProvider.GetRequiredService<IKeyWrapper>()
            )
        );
    }

    [Fact]
    public async Task Scoped_ingest_cannot_read_or_commit_org_wide_data_and_exports_retain_source_authority()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var membership = await identity.Memberships.SingleAsync(m =>
            identity.Users.Any(u => u.Id == m.UserId && u.Email == ApiFixture.ViewerA)
        );
        var filesRole = Role.Create(fixture.OrgA, "file-only-" + Guid.NewGuid().ToString("N"));
        var ingestRole = Role.Create(fixture.OrgA, "scoped-import-" + Guid.NewGuid().ToString("N"));
        identity.Roles.AddRange(filesRole, ingestRole);
        foreach (var action in new[] { "read", "manage" })
            identity.RoleGrants.Add(
                new RoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = fixture.OrgA,
                    RoleId = filesRole.Id,
                    Domain = "files",
                    Action = action,
                }
            );
        identity.RoleGrants.Add(
            new RoleGrant
            {
                Id = Guid.CreateVersion7(),
                OrgId = fixture.OrgA,
                RoleId = ingestRole.Id,
                Domain = "ingest",
                Action = "manage",
            }
        );
        identity.MembershipRoles.AddRange(
            new MembershipRole
            {
                Id = Guid.CreateVersion7(),
                OrgId = fixture.OrgA,
                MembershipId = membership.Id,
                RoleId = filesRole.Id,
            },
            new MembershipRole
            {
                Id = Guid.CreateVersion7(),
                OrgId = fixture.OrgA,
                MembershipId = membership.Id,
                RoleId = ingestRole.Id,
                ScopePath = "region_a",
            }
        );
        await identity.SaveChangesAsync();
        using var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.GetAsync("/api/ingest/batches")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.GetAsync("/api/connectors")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await viewer.PostAsync($"/api/ingest/batches/{Guid.NewGuid()}/commit", null)
            ).StatusCode
        );

        var storage = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();
        foreach (var origin in new string?[] { "org-export", "audit-export", null })
        {
            var id = Guid.CreateVersion7();
            var key = $"{RegionId.Default.Value}/{fixture.OrgA.Value}/files/{id}";
            await store.WriteAsync(
                key,
                new MemoryStream("sensitive archive"u8.ToArray()),
                "application/zip"
            );
            storage.Files.Add(
                new FileObject
                {
                    Id = id,
                    OrgId = fixture.OrgA,
                    Key = key,
                    Name = (origin ?? "org-export") + "-security-test.zip",
                    ContentType = "application/zip",
                    MaxBytes = 17,
                    Status = FileStatus.Clean,
                    Origin = origin,
                    CreatedBy = membership.UserId,
                    ScannedAt = DateTimeOffset.UtcNow,
                }
            );
            await storage.SaveChangesAsync();
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await viewer.GetAsync($"/api/files/{id}")).StatusCode
            );
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await viewer.GetAsync($"/api/files/{id}/download")).StatusCode
            );
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await viewer.DeleteAsync($"/api/files/{id}")).StatusCode
            );
            using var owner = await fixture.LoginAsync(ApiFixture.UserA);
            Assert.Equal(
                HttpStatusCode.OK,
                (await owner.GetAsync($"/api/files/{id}/download")).StatusCode
            );
        }
        var listed = await ApiFixture.GetItemsAsync(viewer, "/api/files");
        Assert.DoesNotContain(
            listed.EnumerateArray(),
            f => f.GetProperty("name").GetString()!.EndsWith("-security-test.zip")
        );
    }

    [Fact]
    public async Task Repeated_connector_sync_is_rejected_while_durable_work_is_pending()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = WebApplication.CreateSlimBuilder().Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapGet(
            "/sites",
            async () =>
            {
                entered.TrySetResult();
                await release.Task;
                return Results.Json(Array.Empty<object>());
            }
        );
        await app.StartAsync();
        try
        {
            using var owner = await fixture.LoginAsync(ApiFixture.UserA);
            var created = await owner.PostAsJsonAsync(
                "/api/connectors",
                new
                {
                    name = "bounded-" + Guid.NewGuid().ToString("N"),
                    url = app.Urls.Single() + "/sites",
                    apiKey = "test-only",
                }
            );
            created.EnsureSuccessStatusCode();
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id")
                .GetGuid();
            Assert.Equal(
                HttpStatusCode.Accepted,
                (await owner.PostAsync($"/api/connectors/{id}/sync", null)).StatusCode
            );
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await owner.PostAsync($"/api/connectors/{id}/sync", null)).StatusCode
            );
        }
        finally
        {
            release.TrySetResult();
            await app.DisposeAsync();
        }
    }
}
