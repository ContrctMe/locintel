using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LocIntel.Modules.Reporting;
using LocIntel.Modules.Reporting.Data;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Sites;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

public sealed class ReportingWorkflowTests(ReportingWorkflowFixture fixture)
    : IClassFixture<ReportingWorkflowFixture>
{
    [Fact]
    public async Task Report_options_limit_counts_utf8_bytes_before_reserving_quota()
    {
        var (client, sites) = await Setup(1);
        var before = await client.GetFromJsonAsync<JsonElement>("/api/reports/quota");
        var body = JsonSerializer.Serialize(
            Request(sites, new { text = new string('界', 6000) }),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }
        );
        var response = await client.PostAsync(
            "/api/reports",
            new StringContent(body, Encoding.UTF8, "application/json")
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>("/api/reports/quota");
        Assert.Equal(
            before.GetProperty("remaining").GetInt64(),
            after.GetProperty("remaining").GetInt64()
        );
    }

    [Fact]
    public async Task Quota_rejects_whole_batches_and_survives_report_metadata_deletion()
    {
        var (client, sites) = await Setup(3);
        await SetReportLimit("2");
        try
        {
            var before = await client.GetFromJsonAsync<JsonElement>("/api/reports/quota");
            // This fixture is shared across tests: previous successful PDFs remain charged.
            var used =
                before.GetProperty("consumed").GetInt64()
                + before.GetProperty("reserved").GetInt64();
            await SetReportLimit((used + 2).ToString());
            Assert.Equal(
                HttpStatusCode.PaymentRequired,
                (await client.PostAsJsonAsync("/api/reports", Request(sites, new { }))).StatusCode
            );
            var rejected = await client.GetFromJsonAsync<JsonElement>("/api/reports/quota");
            Assert.Equal(2, rejected.GetProperty("remaining").GetInt64());
            var id = await Submit(client, sites.Take(2).ToArray(), new { });
            await Wait(client, id, "Completed");
            using var scope = fixture.Factory.Services.CreateScope();
            scope
                .ServiceProvider.GetRequiredService<TenantContext>()
                .Set(fixture.OrgA, RegionId.Default);
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            Assert.Equal(2, await db.QuotaEntries.CountAsync(x => x.JobId == id && x.Consumed));
            await db.Jobs.Where(x => x.Id == id).ExecuteDeleteAsync();
            Assert.Equal(2, await db.QuotaEntries.CountAsync(x => x.JobId == id && x.Consumed));
            Assert.Equal(
                HttpStatusCode.PaymentRequired,
                (
                    await client.PostAsJsonAsync("/api/reports", Request([sites[2]], new { }))
                ).StatusCode
            );
        }
        finally
        {
            await SetReportLimit(null);
        }
    }

    [Fact]
    public async Task Quota_releases_failed_items_and_retry_only_reserves_failed_outputs()
    {
        var (client, sites) = await Setup(2);
        var before = await client.GetFromJsonAsync<JsonElement>("/api/reports/quota");
        var used =
            before.GetProperty("consumed").GetInt64() + before.GetProperty("reserved").GetInt64();
        await SetReportLimit((used + 2).ToString());
        try
        {
            var id = await Submit(client, sites, new { failFirstSite = sites[0] });
            await Wait(client, id, "CompletedWithErrors");
            var partial = await client.GetFromJsonAsync<JsonElement>("/api/reports/quota");
            Assert.Equal(1, partial.GetProperty("remaining").GetInt64());
            using var scope = fixture.Factory.Services.CreateScope();
            scope
                .ServiceProvider.GetRequiredService<TenantContext>()
                .Set(fixture.OrgA, RegionId.Default);
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var success = await db.QuotaEntries.SingleAsync(x => x.JobId == id);
            Assert.True(success.Consumed);
            // A retry at a full quota must leave the partial job unchanged.
            await SetReportLimit((used + 1).ToString());
            Assert.Equal(
                HttpStatusCode.PaymentRequired,
                (await client.PostAsync($"/api/reports/{id}/retry", null)).StatusCode
            );
            await SetReportLimit((used + 2).ToString());
            (await client.PostAsync($"/api/reports/{id}/retry", null)).EnsureSuccessStatusCode();
            await Wait(client, id, "Completed");
            var entries = await db
                .QuotaEntries.AsNoTracking()
                .Where(x => x.JobId == id)
                .ToListAsync();
            Assert.Equal(2, entries.Count);
            Assert.All(entries, x => Assert.True(x.Consumed));
            Assert.Contains(
                entries,
                x => x.Id == success.Id && x.PeriodMonth == success.PeriodMonth
            );
        }
        finally
        {
            await SetReportLimit(null);
        }
    }

    [Fact]
    public async Task Quota_reserves_running_work_and_cancellation_releases_it()
    {
        var (client, sites) = await Setup(2);
        var before = await client.GetFromJsonAsync<JsonElement>("/api/reports/quota");
        var reservedBefore = before.GetProperty("reserved").GetInt64();
        var id = await Submit(client, sites, new { hold = true });
        await Wait(client, id, "Running");
        var during = await client.GetFromJsonAsync<JsonElement>("/api/reports/quota");
        Assert.Equal(reservedBefore + 2, during.GetProperty("reserved").GetInt64());
        (await client.PostAsync($"/api/reports/{id}/cancel", null)).EnsureSuccessStatusCode();
        var after = await client.GetFromJsonAsync<JsonElement>("/api/reports/quota");
        Assert.Equal(reservedBefore, after.GetProperty("reserved").GetInt64());
    }

    [Fact]
    public async Task Quota_serializes_concurrent_submissions_at_the_last_available_output()
    {
        var (client, sites) = await Setup(1);
        var before = await client.GetFromJsonAsync<JsonElement>("/api/reports/quota");
        var used =
            before.GetProperty("consumed").GetInt64() + before.GetProperty("reserved").GetInt64();
        await SetReportLimit((used + 1).ToString());
        try
        {
            var responses = await Task.WhenAll(
                Enumerable
                    .Range(0, 6)
                    .Select(_ => client.PostAsJsonAsync("/api/reports", Request(sites, new { })))
            );
            var accepted = Assert.Single(responses, x => x.IsSuccessStatusCode);
            Assert.All(
                responses.Where(x => !x.IsSuccessStatusCode),
                x =>
                    Assert.Contains(
                        x.StatusCode,
                        new[] { HttpStatusCode.PaymentRequired, HttpStatusCode.ServiceUnavailable }
                    )
            );
            var id = (await accepted.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id")
                .GetGuid();
            await Wait(client, id, "Completed");
            var after = await client.GetFromJsonAsync<JsonElement>("/api/reports/quota");
            Assert.Equal(0, after.GetProperty("remaining").GetInt64());
        }
        finally
        {
            await SetReportLimit(null);
        }
    }

    private async Task SetReportLimit(string? value)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var db =
            scope.ServiceProvider.GetRequiredService<LocIntel.Modules.Entitlements.Data.EntitlementsDbContext>();
        const string code = LocIntel.Platform.Entitlements.EntitlementCatalog.ReportsMonthly;
        await db
            .OrgEntitlements.Where(x => x.OrgId == fixture.OrgA && x.Code == code)
            .ExecuteDeleteAsync();
        if (value is null)
            return;
        db.OrgEntitlements.Add(
            new LocIntel.Modules.Entitlements.Data.OrgEntitlement
            {
                Id = Guid.CreateVersion7(),
                OrgId = fixture.OrgA,
                Code = code,
                Value = value,
                Source = "manual",
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Reporting_entitlement_blocks_new_generation_and_retry_but_preserves_downloads()
    {
        var (client, sites) = await Setup(1);
        var completedId = await Submit(client, sites, new { });
        var completed = await Wait(client, completedId, "Completed");
        var artifact = completed.GetProperty("artifacts")[0].GetProperty("id").GetGuid();
        var (_, failedSites) = await Setup(1);
        var failedId = await Submit(client, failedSites, new { failFirstSite = failedSites[0] });
        await Wait(client, failedId, "Failed");
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var db =
            scope.ServiceProvider.GetRequiredService<LocIntel.Modules.Entitlements.Data.EntitlementsDbContext>();
        var entitlement = new LocIntel.Modules.Entitlements.Data.OrgEntitlement
        {
            Id = Guid.CreateVersion7(),
            OrgId = fixture.OrgA,
            Code = LocIntel.Platform.Entitlements.EntitlementCatalog.ReportsEnabled,
            Value = "false",
            Source = "manual",
        };
        db.OrgEntitlements.Add(entitlement);
        await db.SaveChangesAsync();
        try
        {
            var rejected = await client.PostAsJsonAsync("/api/reports", Request(sites, new { }));
            Assert.Equal(HttpStatusCode.PaymentRequired, rejected.StatusCode);
            Assert.Contains("reports.enabled", await rejected.Content.ReadAsStringAsync());
            Assert.Equal(
                HttpStatusCode.PaymentRequired,
                (await client.PostAsync($"/api/reports/{failedId}/retry", null)).StatusCode
            );
            Assert.NotEmpty(await Download(client, completedId, artifact));
        }
        finally
        {
            db.OrgEntitlements.Remove(entitlement);
            await db.SaveChangesAsync();
        }
        (await client.PostAsync($"/api/reports/{failedId}/retry", null)).EnsureSuccessStatusCode();
        await Wait(client, failedId, "Completed");
    }

    [Fact]
    public async Task Retrying_failed_work_obeys_the_same_shared_queue_limit_as_new_submissions()
    {
        var (client, sites) = await Setup(1);
        var id = await Submit(client, sites, new { failFirstSite = sites[0] });
        await Wait(client, id, "Failed");
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var failed = await db.Jobs.SingleAsync(x => x.Id == id);
        var blockers = Enumerable
            .Range(0, 10)
            .Select(_ => new ReportJob
            {
                OrgId = fixture.OrgA,
                RequestedBy = failed.RequestedBy,
                ReportType = "workflow-test",
                DefinitionVersion = 1,
                Mode = ReportMode.Single,
                Selection = ReportSelection.Selected,
                OptionsJson = "{}",
                SiteIds = sites,
                State = ReportJobState.Running,
                LeaseOwner = Guid.CreateVersion7(),
                LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(5),
            })
            .ToArray();
        db.Jobs.AddRange(blockers);
        await db.SaveChangesAsync();
        try
        {
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await client.PostAsync($"/api/reports/{id}/retry", null)).StatusCode
            );
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await client.PostAsJsonAsync("/api/reports", Request(sites, new { }))).StatusCode
            );
            var unchanged = await client.GetFromJsonAsync<JsonElement>($"/api/reports/{id}");
            Assert.Equal("Failed", unchanged.GetProperty("state").GetString());
        }
        finally
        {
            var blockerIds = blockers.Select(x => x.Id).ToArray();
            await db.Jobs.Where(x => blockerIds.Contains(x.Id)).ExecuteDeleteAsync();
        }
        (await client.PostAsync($"/api/reports/{id}/retry", null)).EnsureSuccessStatusCode();
        await Wait(client, id, "Completed");
    }

    [Theory]
    [InlineData("fixture", true)]
    [InlineData("unavailable", false)]
    [InlineData("redirect", false)]
    [InlineData("corrupt", false)]
    public async Task Reference_map_composes_authorized_overlay_and_handles_optional_upstream_failures(
        string provider,
        bool succeeds
    )
    {
        var (client, sites) = await Setup(1);
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var tenancy = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var site = await tenancy.Sites.SingleAsync(x => x.Id == new SiteId(sites[0]));
        site.Latitude = 0;
        site.Longitude = 0;
        await tenancy.SaveChangesAsync();
        var spatial =
            scope.ServiceProvider.GetRequiredService<LocIntel.Modules.Spatial.Data.SpatialDbContext>();
        var layer = new LocIntel.Modules.Spatial.Overlays.OverlayLayer
        {
            Id = Guid.CreateVersion7(),
            OrgId = fixture.OrgA,
            Name = "Reference territory",
            Kind = "territory",
            NodeId = site.NodeId,
            HierarchyId = Guid.CreateVersion7(),
            Path = site.Path,
            CreatedBy = Guid.CreateVersion7(),
        };
        var geometry = new NetTopologySuite.IO.WKTReader().Read(
            "MULTIPOLYGON (((-0.004 -0.003, 0.004 -0.003, 0.004 0.003, -0.004 0.003, -0.004 -0.003), (-0.001 -0.001, 0.001 -0.001, 0.001 0.001, -0.001 0.001, -0.001 -0.001)), ((0.005 0.003, 0.006 0.003, 0.006 0.004, 0.005 0.004, 0.005 0.003)))"
        );
        geometry.SRID = 4326;
        spatial.Layers.Add(layer);
        spatial.Features.Add(
            new LocIntel.Modules.Spatial.Overlays.OverlayFeature
            {
                Id = Guid.CreateVersion7(),
                OrgId = fixture.OrgA,
                LayerId = layer.Id,
                Geom = geometry,
                HierarchyId = layer.HierarchyId,
                Path = site.Path,
            }
        );
        await spatial.SaveChangesAsync();
        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new
            {
                reportType = "site",
                mode = "single",
                selection = "selected",
                siteIds = sites,
                options = new
                {
                    mapProvider = provider,
                    mapZoom = 15,
                    overlayIds = new[] { layer.Id },
                },
            }
        );
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var ready = await Wait(client, id, "Completed");
        var artifact = Assert
            .Single(ready.GetProperty("artifacts").EnumerateArray())
            .GetProperty("id")
            .GetGuid();
        var bytes = await Download(client, id, artifact);
        var item = await scope
            .ServiceProvider.GetRequiredService<ReportingDbContext>()
            .Items.SingleAsync(x => x.JobId == id);
        if (succeeds)
        {
            Assert.Equal("[]", item.WarningsJson);
            Assert.Contains(layer.Id.ToString(), item.DependenciesJson);
            var output = Path.Combine(Path.GetTempPath(), "locintel-reporting-map");
            Directory.CreateDirectory(output);
            await File.WriteAllBytesAsync(Path.Combine(output, "site-map.pdf"), bytes);
            layer.DeletedAt = DateTimeOffset.UtcNow;
            await spatial.SaveChangesAsync();
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync($"/api/files/{artifact}/download")).StatusCode
            );
            var rejected = await client.PostAsJsonAsync(
                "/api/reports",
                new
                {
                    reportType = "site",
                    mode = "single",
                    selection = "selected",
                    siteIds = sites,
                    options = new { mapProvider = provider, overlayIds = new[] { layer.Id } },
                }
            );
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        }
        else
        {
            Assert.Contains("Map unavailable", item.WarningsJson);
            Assert.DoesNotContain(layer.Id.ToString(), item.DependenciesJson);
            Assert.DoesNotContain("example.invalid", item.WarningsJson);
        }
    }

    [Fact]
    public async Task One_hundred_reference_pdfs_form_a_complete_downloadable_bundle()
    {
        var (client, sites) = await Setup(100);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new
            {
                reportType = "site",
                mode = "bulk",
                selection = "selected",
                siteIds = sites,
                options = new { },
            }
        );
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        JsonElement job = default;
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                job = await client.GetFromJsonAsync<JsonElement>($"/api/reports/{id}");
                var state = job.GetProperty("state").GetString();
                Assert.DoesNotContain(state, new[] { "Failed", "CompletedWithErrors" });
                return state == "Completed";
            },
            "100 reference PDFs complete",
            TimeSpan.FromMinutes(3)
        );
        Assert.Equal(100, job.GetProperty("items").GetArrayLength());
        Assert.Equal(101, job.GetProperty("artifacts").GetArrayLength());
        var bundleId = job.GetProperty("artifacts")
            .EnumerateArray()
            .Single(x => x.GetProperty("contentType").GetString() == "application/zip")
            .GetProperty("id")
            .GetGuid();
        var bytes = await Download(client, id, bundleId);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Equal(101, zip.Entries.Count);
        var pdfs = zip
            .Entries.Where(x => x.Name.EndsWith(".pdf", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(100, pdfs.Length);
        foreach (var entry in pdfs)
        {
            using var copy = new MemoryStream();
            await using (var input = entry.Open())
                await input.CopyToAsync(copy);
            copy.Position = 0;
            using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(
                copy,
                PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import
            );
            Assert.True(pdf.PageCount > 0);
        }
        using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
        Assert.Equal(100, manifest.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(
            sites.Order(),
            manifest
                .RootElement.GetProperty("items")
                .EnumerateArray()
                .SelectMany(item =>
                    item.GetProperty("SiteIds").EnumerateArray().Select(site => site.GetGuid())
                )
                .Order()
        );
        Assert.All(
            manifest.RootElement.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("Succeeded", item.GetProperty("State").GetString())
        );
        var output = Path.Combine(Path.GetTempPath(), "locintel-reporting-100");
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, "reference-100.zip"), bytes);
        await File.WriteAllTextAsync(
            Path.Combine(output, "metrics.json"),
            JsonSerializer.Serialize(
                new
                {
                    elapsedMilliseconds = elapsed.ElapsedMilliseconds,
                    zipBytes = bytes.Length,
                    pdfCount = pdfs.Length,
                    peakTestHostWorkingSetBytes = System
                        .Diagnostics.Process.GetCurrentProcess()
                        .PeakWorkingSet64
                        is var peak
                    && peak > 0
                        ? (long?)peak
                        : null,
                    qualification = "In-process integration host, real Postgres and local storage; no provider or photos, not fleet capacity.",
                }
            )
        );
    }

    [Fact]
    public async Task Organization_purge_drains_an_active_renderer_and_removes_report_intents()
    {
        var (client, sites) = await Setup(1);
        var completedId = await Submit(client, sites, new { });
        await Wait(client, completedId, "Completed");
        using var storedScope = fixture.Factory.Services.CreateScope();
        storedScope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var storedDb = storedScope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var writtenKey = (
            await storedDb.Artifacts.SingleAsync(x => x.JobId == completedId && x.Ready)
        ).Key;
        var objectStore =
            storedScope.ServiceProvider.GetRequiredService<LocIntel.Platform.Storage.IObjectStore>();
        Assert.True(await objectStore.GetLengthAsync(writtenKey) > 0);
        var id = await Submit(client, sites, new { hold = true });
        await Wait(client, id, "Running");
        await fixture.PublishForOrgA(new LocIntel.Contracts.PurgeOrgReporting());
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                using var scope = fixture.Factory.Services.CreateScope();
                scope
                    .ServiceProvider.GetRequiredService<TenantContext>()
                    .Set(fixture.OrgA, RegionId.Default);
                var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
                return !await db.Jobs.AnyAsync(x => x.Id == id && x.LeaseOwner != null);
            },
            "purged renderer drains",
            TimeSpan.FromSeconds(10)
        );
        await fixture.PublishForOrgA(new MaintainReports());
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                using var scope = fixture.Factory.Services.CreateScope();
                scope
                    .ServiceProvider.GetRequiredService<TenantContext>()
                    .Set(fixture.OrgA, RegionId.Default);
                var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
                return !await db.Jobs.AnyAsync(x => x.Id == id)
                    && !await db.Artifacts.AnyAsync(x => x.JobId == id);
            },
            "purged reports are removed"
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/reports/{id}")).StatusCode
        );
        Assert.False(await storedDb.Jobs.AnyAsync(x => x.Id == completedId));
        Assert.Null(await objectStore.GetLengthAsync(writtenKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reference_photograph_inclusion_controls_later_download_access(bool corrupt)
    {
        var (client, sites) = await Setup(1);
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var user = await scope
            .ServiceProvider.GetRequiredService<LocIntel.Modules.Identity.Data.IdentityDbContext>()
            .Users.SingleAsync(x => x.Email == ApiFixture.UserA);
        var photoId = Guid.CreateVersion7();
        var photo = new LocIntel.Modules.Storage.Data.FileObject
        {
            Id = photoId,
            OrgId = fixture.OrgA,
            Key = $"reports-test/{fixture.OrgA.Value}/{photoId}.png",
            Name = "Photograph",
            ContentType = "image/png",
            MaxBytes = 1024 * 1024,
            CreatedBy = user.Id,
            Status = LocIntel.Modules.Storage.Data.FileStatus.Clean,
        };
        var storageDb =
            scope.ServiceProvider.GetRequiredService<LocIntel.Modules.Storage.Data.StorageDbContext>();
        storageDb.Files.Add(photo);
        await storageDb.SaveChangesAsync();
        using var bitmap = new SkiaSharp.SKBitmap(80, 40);
        bitmap.Erase(SkiaSharp.SKColors.CornflowerBlue);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        await scope
            .ServiceProvider.GetRequiredService<LocIntel.Platform.Storage.IObjectStore>()
            .WriteAsync(
                photo.Key,
                new MemoryStream(corrupt ? [1, 2, 3] : encoded.ToArray()),
                "image/png"
            );
        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new
            {
                reportType = "site",
                mode = "single",
                selection = "selected",
                siteIds = sites,
                options = new { photos = new Dictionary<Guid, Guid[]> { [sites[0]] = [photoId] } },
            }
        );
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var ready = await Wait(client, id, "Completed");
        var artifact = Assert
            .Single(ready.GetProperty("artifacts").EnumerateArray())
            .GetProperty("id")
            .GetGuid();
        var bytes = await Download(client, id, artifact);
        var item = await scope
            .ServiceProvider.GetRequiredService<ReportingDbContext>()
            .Items.SingleAsync(x => x.JobId == id);
        photo.Status = LocIntel.Modules.Storage.Data.FileStatus.Quarantined;
        await storageDb.SaveChangesAsync();
        if (corrupt)
        {
            Assert.DoesNotContain(photoId.ToString(), item.DependenciesJson);
            Assert.Contains("Photograph", item.WarningsJson);
            Assert.Equal(bytes, await Download(client, id, artifact));
            return;
        }
        Assert.Contains(photoId.ToString(), item.DependenciesJson);
        Assert.DoesNotContain("Photograph", item.WarningsJson);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/files/{artifact}/download")).StatusCode
        );
        // Site/file management still works when a captured photo is no longer readable.
        (await client.DeleteAsync($"/api/files/{artifact}")).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/files/{artifact}/restore", null)).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/files/{artifact}/download")).StatusCode
        );
        using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(
            new MemoryStream(bytes),
            PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import
        );
        Assert.Contains(
            pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>(),
            p =>
                p.Elements.GetDictionary("/Resources")
                    ?.Elements.GetDictionary("/XObject")
                    ?.Elements.Count > 0
        );
    }

    [Fact]
    public async Task Attached_photographs_require_current_access_to_their_own_sites()
    {
        var (client, sites) = await Setup(2);
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var identity =
            scope.ServiceProvider.GetRequiredService<LocIntel.Modules.Identity.Data.IdentityDbContext>();
        var user = await identity.Users.SingleAsync(x => x.Email == ApiFixture.UserA);
        var membershipIds = identity
            .Memberships.Where(x => x.OrgId == fixture.OrgA && x.UserId == user.Id)
            .Select(x => x.Id);
        var assignments = await identity
            .MembershipRoles.Where(x => membershipIds.Contains(x.MembershipId))
            .AsNoTracking()
            .ToListAsync();
        var storage =
            scope.ServiceProvider.GetRequiredService<LocIntel.Modules.Storage.Data.StorageDbContext>();
        var photoId = Guid.CreateVersion7();
        var photo = new LocIntel.Modules.Storage.Data.FileObject
        {
            Id = photoId,
            OrgId = fixture.OrgA,
            Key = $"reports-test/{fixture.OrgA.Value}/{photoId}.png",
            Name = "Site B photograph",
            ContentType = "image/png",
            MaxBytes = 1024 * 1024,
            CreatedBy = user.Id,
            Status = LocIntel.Modules.Storage.Data.FileStatus.Clean,
            SiteIds = [sites[1]],
        };
        storage.Files.Add(photo);
        await storage.SaveChangesAsync();
        using var bitmap = new SkiaSharp.SKBitmap(80, 40);
        bitmap.Erase(SkiaSharp.SKColors.CornflowerBlue);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        await scope
            .ServiceProvider.GetRequiredService<LocIntel.Platform.Storage.IObjectStore>()
            .WriteAsync(photo.Key, new MemoryStream(encoded.ToArray()), "image/png");
        var request = new
        {
            reportType = "site",
            mode = "single",
            selection = "selected",
            siteIds = new[] { sites[0] },
            options = new { photos = new Dictionary<Guid, Guid[]> { [sites[0]] = [photoId] } },
        };
        var response = await client.PostAsJsonAsync("/api/reports", request);
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var ready = await Wait(client, id, "Completed");
        var artifact = Assert
            .Single(ready.GetProperty("artifacts").EnumerateArray())
            .GetProperty("id")
            .GetGuid();
        Assert.NotEmpty(await Download(client, id, artifact));
        var siteAPath = (
            await scope
                .ServiceProvider.GetRequiredService<TenancyDbContext>()
                .Sites.SingleAsync(x => x.Id == new SiteId(sites[0]))
        ).Path.ToString();
        try
        {
            await identity
                .MembershipRoles.Where(x => membershipIds.Contains(x.MembershipId))
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.ScopePath, siteAPath));
            // The report's site remains visible, but its captured photograph belongs to site B.
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await client.PostAsJsonAsync("/api/reports", request)).StatusCode
            );
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync($"/api/files/{artifact}/download")).StatusCode
            );
        }
        finally
        {
            foreach (var assignment in assignments)
                await identity
                    .MembershipRoles.Where(x => x.Id == assignment.Id)
                    .ExecuteUpdateAsync(set =>
                        set.SetProperty(x => x.ScopePath, assignment.ScopePath)
                    );
        }
        Assert.NotEmpty(await Download(client, id, artifact));
    }

    [Theory]
    [InlineData("site", "single", 1)]
    [InlineData("sites-summary", "aggregate", 2)]
    public async Task Production_reference_definitions_generate_downloadable_real_pdfs(
        string reportType,
        string mode,
        int count
    )
    {
        var (client, sites) = await Setup(count);
        var catalogResponse = await client.GetAsync("/api/reports/types");
        Assert.True(
            catalogResponse.IsSuccessStatusCode,
            await catalogResponse.Content.ReadAsStringAsync()
        );
        var catalog = await catalogResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            catalog.EnumerateArray(),
            x => x.GetProperty("id").GetString() == reportType
        );
        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new
            {
                reportType,
                mode,
                selection = "selected",
                siteIds = sites,
                options = new { },
            }
        );
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var ready = await Wait(client, id, "Completed");
        var artifact = Assert
            .Single(ready.GetProperty("artifacts").EnumerateArray())
            .GetProperty("id")
            .GetGuid();
        var bytes = await Download(client, id, artifact);
        using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(
            new MemoryStream(bytes),
            PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import
        );
        Assert.True(pdf.PageCount > 0);
        Assert.True(bytes.Length > 5000);
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var item = await scope
            .ServiceProvider.GetRequiredService<ReportingDbContext>()
            .Items.SingleAsync(x => x.JobId == id);
        var charge = await scope
            .ServiceProvider.GetRequiredService<ReportingDbContext>()
            .QuotaEntries.SingleAsync(x => x.JobId == id);
        Assert.True(charge.Consumed);
        Assert.Equal(item.Id, charge.Id);
        Assert.Contains("no report basemap", item.WarningsJson);
        var output = Path.Combine(Path.GetTempPath(), "locintel-reporting-reference");
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, reportType + ".pdf"), bytes);
    }

    [Fact]
    public async Task Maintenance_resumes_an_expired_worker_lease()
    {
        var (client, sites) = await Setup(1);
        Guid id;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            scope
                .ServiceProvider.GetRequiredService<TenantContext>()
                .Set(fixture.OrgA, RegionId.Default);
            var user = await scope
                .ServiceProvider.GetRequiredService<LocIntel.Modules.Identity.Data.IdentityDbContext>()
                .Users.SingleAsync(x => x.Email == ApiFixture.UserA);
            var job = new ReportJob
            {
                OrgId = fixture.OrgA,
                RequestedBy = user.Id,
                ReportType = "workflow-test",
                DefinitionVersion = 1,
                Mode = ReportMode.Single,
                Selection = ReportSelection.Selected,
                OptionsJson = "{}",
                SiteIds = sites,
                State = ReportJobState.Running,
                LeaseOwner = Guid.NewGuid(),
                LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(-1),
            };
            id = job.Id;
            job.Items.Add(
                new ReportItem
                {
                    OrgId = fixture.OrgA,
                    JobId = id,
                    SiteIds = sites,
                    State = ReportItemState.Running,
                }
            );
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
        }
        await fixture.PublishForOrgA(new MaintainReports());
        var result = await Wait(client, id, "Completed");
        Assert.Single(result.GetProperty("artifacts").EnumerateArray());
    }

    [Theory]
    [InlineData("pdf-stored")]
    [InlineData("zip-partial")]
    [InlineData("zip-stored")]
    [InlineData("zip-published")]
    public async Task Recovery_from_storage_commit_boundaries_preserves_files_quota_and_one_bundle(
        string boundary
    )
    {
        var (client, sites) = await Setup(2);
        var id = await Submit(client, sites, new { });
        var original = await Wait(client, id, "Completed");
        var firstPdf = original
            .GetProperty("artifacts")
            .EnumerateArray()
            .First(x => x.GetProperty("contentType").GetString() == "application/pdf")
            .GetProperty("id")
            .GetGuid();
        var firstBytes = await Download(client, id, firstPdf);
        Guid previousZip;
        string orphanKey;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            scope
                .ServiceProvider.GetRequiredService<TenantContext>()
                .Set(fixture.OrgA, RegionId.Default);
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var storage =
                scope.ServiceProvider.GetRequiredService<LocIntel.Modules.Storage.Data.StorageDbContext>();
            var store =
                scope.ServiceProvider.GetRequiredService<LocIntel.Platform.Storage.IObjectStore>();
            var job = await db
                .Jobs.Include(x => x.Items)
                .Include(x => x.Artifacts)
                .SingleAsync(x => x.Id == id);
            var zip = job.Artifacts.Single(x => x.ItemId == null && x.Ready);
            previousZip = zip.Id;
            orphanKey = zip.Key;
            // Reconstruct the durable database/object-store snapshot left by a hard
            // crash at each boundary. No graceful-shutdown cleanup is invoked.
            job.State = ReportJobState.Running;
            job.CompletedAt = null;
            job.ExpiresAt = null;
            job.MetadataExpiresAt = null;
            job.LeaseOwner = Guid.CreateVersion7();
            job.LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
            if (boundary == "pdf-stored")
            {
                var pdf = job.Artifacts.Single(x => x.ItemId != null && x.Id != firstPdf);
                var item = job.Items.Single(x => x.Id == pdf.ItemId);
                item.State = ReportItemState.Running;
                item.GeneratedAt = null;
                item.DependenciesJson = "{}";
                pdf.Ready = false;
                pdf.FilePublished = false;
                pdf.Bytes = 0;
                orphanKey = pdf.Key;
                (await db.QuotaEntries.SingleAsync(x => x.Id == item.Id)).Consumed = false;
                await storage.Files.Where(x => x.Id == pdf.Id).ExecuteDeleteAsync();
                await store.DeleteAsync(zip.Key);
                db.Artifacts.Remove(zip);
            }
            else if (boundary != "zip-published")
            {
                zip.Ready = false;
                zip.Bytes = 0;
                if (boundary == "zip-partial")
                    await store.WriteAsync(
                        zip.Key,
                        new MemoryStream(Encoding.UTF8.GetBytes("PK interrupted ZIP")),
                        "application/zip"
                    );
            }
            await db.SaveChangesAsync();
        }
        await fixture.PublishForOrgA(new MaintainReports());
        var recovered = await Wait(client, id, "Completed");
        Assert.Equal(firstBytes, await Download(client, id, firstPdf));
        var bundleId = Assert
            .Single(
                recovered.GetProperty("artifacts").EnumerateArray(),
                x => x.GetProperty("contentType").GetString() == "application/zip"
            )
            .GetProperty("id")
            .GetGuid();
        if (boundary == "zip-published")
            Assert.Equal(previousZip, bundleId);
        else
            Assert.NotEqual(previousZip, bundleId);
        using (var zip = new ZipArchive(new MemoryStream(await Download(client, id, bundleId))))
        {
            Assert.Equal(3, zip.Entries.Count);
            using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
            Assert.All(
                manifest.RootElement.GetProperty("items").EnumerateArray(),
                item => Assert.Equal("Succeeded", item.GetProperty("State").GetString())
            );
            Assert.Equal(
                sites.Order(),
                manifest
                    .RootElement.GetProperty("items")
                    .EnumerateArray()
                    .SelectMany(item =>
                        item.GetProperty("SiteIds").EnumerateArray().Select(site => site.GetGuid())
                    )
                    .Order()
            );
        }
        using var verification = fixture.Factory.Services.CreateScope();
        verification
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var reports = verification.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var files =
            verification.ServiceProvider.GetRequiredService<LocIntel.Modules.Storage.Data.StorageDbContext>();
        Assert.Equal(2, await reports.QuotaEntries.CountAsync(x => x.JobId == id && x.Consumed));
        Assert.False(await reports.QuotaEntries.AnyAsync(x => x.JobId == id && !x.Consumed));
        Assert.Equal(2, await files.Files.CountAsync(x => x.OriginId == id));
        var attempts = await reports
            .Items.Where(x => x.JobId == id)
            .Select(x => x.Attempt)
            .ToArrayAsync();
        Assert.Equal(boundary == "pdf-stored" ? 3 : 2, attempts.Sum());
        Assert.Equal(3, await reports.Artifacts.CountAsync(x => x.JobId == id && x.Ready));
        await reports
            .Jobs.Where(x => x.Id == id)
            .ExecuteUpdateAsync(set =>
                set.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1))
            );
        await fixture.PublishForOrgA(new MaintainReports());
        await ApiFixture.WaitUntilAsync(
            async () =>
                !await reports.Artifacts.AnyAsync(x =>
                    x.JobId == id && (!x.Ready || x.ItemId == null)
                ),
            "crash orphan and ZIP cleanup"
        );
        Assert.Null(
            await verification
                .ServiceProvider.GetRequiredService<LocIntel.Platform.Storage.IObjectStore>()
                .GetLengthAsync(orphanKey)
        );
        Assert.Equal(firstBytes, await Download(client, id, firstPdf));
    }

    [Fact]
    public async Task Published_site_pdf_survives_report_expiry_and_uses_file_trash_hold_and_restore()
    {
        var (client, sites) = await Setup(1);
        var id = await Submit(client, sites, new { });
        var ready = await Wait(client, id, "Completed");
        var fileId = Assert
            .Single(ready.GetProperty("artifacts").EnumerateArray())
            .GetProperty("fileId")
            .GetGuid();
        var bytes = await Download(client, id, fileId);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            scope
                .ServiceProvider.GetRequiredService<TenantContext>()
                .Set(fixture.OrgA, RegionId.Default);
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            await db
                .Jobs.Where(x => x.Id == id)
                .ExecuteUpdateAsync(set =>
                    set.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1))
                        .SetProperty(x => x.MetadataExpiresAt, DateTimeOffset.UtcNow.AddDays(-1))
                );
        }
        await fixture.PublishForOrgA(new MaintainReports());
        Assert.Equal(bytes, await Download(client, id, fileId));
        (
            await client.PostAsJsonAsync($"/api/files/{fileId}/hold", new { hold = true })
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await client.DeleteAsync($"/api/files/{fileId}")).StatusCode
        );
        (
            await client.PostAsJsonAsync($"/api/files/{fileId}/hold", new { hold = false })
        ).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/files/{fileId}")).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/files/{fileId}/download")).StatusCode
        );
        (await client.PostAsync($"/api/files/{fileId}/restore", null)).EnsureSuccessStatusCode();
        Assert.Equal(bytes, await Download(client, id, fileId));
    }

    [Fact]
    public async Task Download_rechecks_site_scope_after_generation()
    {
        var (client, sites) = await Setup(1);
        var id = await Submit(client, sites, new { });
        var ready = await Wait(client, id, "Completed");
        var artifact = Assert
            .Single(ready.GetProperty("artifacts").EnumerateArray())
            .GetProperty("id")
            .GetGuid();
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var db =
            scope.ServiceProvider.GetRequiredService<LocIntel.Modules.Identity.Data.IdentityDbContext>();
        var user = await db.Users.SingleAsync(x => x.Email == ApiFixture.UserA);
        var memberships = db
            .Memberships.Where(x => x.OrgId == fixture.OrgA && x.UserId == user.Id)
            .Select(x => x.Id);
        var originals = await db
            .MembershipRoles.Where(x => memberships.Contains(x.MembershipId))
            .AsNoTracking()
            .ToListAsync();
        try
        {
            await db
                .MembershipRoles.Where(x => memberships.Contains(x.MembershipId))
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.ScopePath, "unrelated"));
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync($"/api/files/{artifact}/download")).StatusCode
            );
        }
        finally
        {
            foreach (var original in originals)
                await db
                    .MembershipRoles.Where(x => x.Id == original.Id)
                    .ExecuteUpdateAsync(set =>
                        set.SetProperty(x => x.ScopePath, original.ScopePath)
                    );
        }
        Assert.NotEmpty(await Download(client, id, artifact));
    }

    [Fact]
    public async Task Oversized_outputs_fail_without_an_empty_bundle_or_leftover_temporary_files()
    {
        var (client, sites) = await Setup(2);
        var id = await Submit(client, sites, new { oversize = true });
        var job = await Wait(client, id, "Failed");
        Assert.Empty(job.GetProperty("artifacts").EnumerateArray());
        Assert.All(
            job.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("generation_failed", item.GetProperty("errorCode").GetString())
        );
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var artifacts = await scope
            .ServiceProvider.GetRequiredService<ReportingDbContext>()
            .Artifacts.Where(x => x.JobId == id)
            .ToListAsync();
        Assert.Equal(2, artifacts.Count);
        Assert.All(
            artifacts,
            artifact =>
                Assert.False(
                    File.Exists(
                        Path.Combine(
                            Path.GetTempPath(),
                            "locintel-report-" + artifact.Id.ToString("N")
                        )
                    )
                )
        );
    }

    [Fact]
    public async Task Read_only_site_users_share_files_but_aggregate_access_requires_every_site()
    {
        var (owner, sites) = await Setup(2);
        var singleId = await Submit(owner, [sites[0]], new { });
        var single = await Wait(owner, singleId, "Completed");
        var singleFile = Assert
            .Single(single.GetProperty("artifacts").EnumerateArray())
            .GetProperty("fileId")
            .GetGuid();
        var response = await owner.PostAsJsonAsync(
            "/api/reports",
            new
            {
                reportType = "sites-summary",
                mode = "aggregate",
                selection = "selected",
                siteIds = sites,
                options = new { },
            }
        );
        response.EnsureSuccessStatusCode();
        var runId = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var aggregate = await Wait(owner, runId, "Completed");
        var aggregateFile = Assert
            .Single(aggregate.GetProperty("artifacts").EnumerateArray())
            .GetProperty("fileId")
            .GetGuid();
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var db =
            scope.ServiceProvider.GetRequiredService<LocIntel.Modules.Identity.Data.IdentityDbContext>();
        var user = await db.Users.SingleAsync(x => x.Email == ApiFixture.ViewerA);
        var membership = await db.Memberships.SingleAsync(x =>
            x.OrgId == fixture.OrgA && x.UserId == user.Id
        );
        var role = LocIntel.Modules.Identity.Access.Role.Create(fixture.OrgA, "Report file reader");
        db.Roles.Add(role);
        foreach (var domain in new[] { "sites", "files" })
            db.RoleGrants.Add(
                new()
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = fixture.OrgA,
                    RoleId = role.Id,
                    Domain = domain,
                    Action = "read",
                }
            );
        var assignmentId = Guid.CreateVersion7();
        db.MembershipRoles.Add(
            new()
            {
                Id = assignmentId,
                OrgId = fixture.OrgA,
                MembershipId = membership.Id,
                RoleId = role.Id,
            }
        );
        await db.SaveChangesAsync();
        try
        {
            var reader = await fixture.LoginAsync(ApiFixture.ViewerA);
            Assert.Equal(
                HttpStatusCode.OK,
                (await reader.GetAsync($"/api/reports/{runId}")).StatusCode
            );
            Assert.Equal(
                HttpStatusCode.OK,
                (await reader.GetAsync($"/api/files/{aggregateFile}/download")).StatusCode
            );
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (
                    await reader.PostAsJsonAsync("/api/reports", Request([sites[0]], new { }))
                ).StatusCode
            );
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await reader.DeleteAsync($"/api/files/{singleFile}")).StatusCode
            );
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await reader.GetAsync("/api/reports/quota")).StatusCode
            );
            foreach (var siteId in sites)
            {
                var list = await reader.GetFromJsonAsync<JsonElement>(
                    $"/api/files?siteId={siteId}"
                );
                Assert.Contains(
                    list.GetProperty("items").EnumerateArray(),
                    x => x.GetProperty("id").GetGuid() == aggregateFile
                );
            }
            var storage =
                scope.ServiceProvider.GetRequiredService<LocIntel.Modules.Storage.Data.StorageDbContext>();
            Assert.Equal(1, await storage.Files.CountAsync(x => x.Id == aggregateFile));
            var path = (
                await scope
                    .ServiceProvider.GetRequiredService<TenancyDbContext>()
                    .Sites.SingleAsync(x => x.Id == new SiteId(sites[0]))
            ).Path.ToString();
            await db
                .MembershipRoles.Where(x => x.Id == assignmentId)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.ScopePath, path));
            Assert.Equal(
                HttpStatusCode.OK,
                (await reader.GetAsync($"/api/files/{singleFile}/download")).StatusCode
            );
            foreach (
                var endpoint in new[]
                {
                    $"/api/files/{aggregateFile}",
                    $"/api/files/{aggregateFile}/download",
                    $"/api/reports/{runId}",
                }
            )
                Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync(endpoint)).StatusCode);
            var firstPage = await reader.GetFromJsonAsync<JsonElement>(
                $"/api/files?siteId={sites[0]}&limit=1"
            );
            Assert.Empty(firstPage.GetProperty("items").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, firstPage.GetProperty("total").ValueKind);
            var nextOffset = firstPage.GetProperty("nextOffset").GetInt32();
            var nextPage = await reader.GetFromJsonAsync<JsonElement>(
                $"/api/files?siteId={sites[0]}&limit=1&offset={nextOffset}"
            );
            Assert.Equal(
                singleFile,
                Assert
                    .Single(nextPage.GetProperty("items").EnumerateArray())
                    .GetProperty("id")
                    .GetGuid()
            );
            var filtered = await reader.GetFromJsonAsync<JsonElement>(
                $"/api/files?siteId={sites[0]}"
            );
            Assert.DoesNotContain(
                filtered.GetProperty("items").EnumerateArray(),
                x => x.GetProperty("id").GetGuid() == aggregateFile
            );
        }
        finally
        {
            await db.MembershipRoles.Where(x => x.Id == assignmentId).ExecuteDeleteAsync();
            await db.RoleGrants.Where(x => x.RoleId == role.Id).ExecuteDeleteAsync();
            await db.Roles.Where(x => x.Id == role.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Publication_redelivery_does_not_restore_a_trashed_file_or_recharge_quota()
    {
        var (client, sites) = await Setup(2);
        var runId = await Submit(client, sites, new { });
        var ready = await Wait(client, runId, "Completed");
        var fileId = ready
            .GetProperty("artifacts")
            .EnumerateArray()
            .First(x => x.GetProperty("contentType").GetString() == "application/pdf")
            .GetProperty("fileId")
            .GetGuid();
        var zipId = ready
            .GetProperty("artifacts")
            .EnumerateArray()
            .Single(x => x.GetProperty("contentType").GetString() == "application/zip")
            .GetProperty("id")
            .GetGuid();
        var zipBytes = await Download(client, runId, zipId);
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var db =
            scope.ServiceProvider.GetRequiredService<LocIntel.Modules.Storage.Data.StorageDbContext>();
        var file = await db.Files.AsNoTracking().SingleAsync(x => x.Id == fileId);
        (await client.DeleteAsync($"/api/files/{fileId}")).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/reports/{runId}/artifacts/{zipId}/download")).StatusCode
        );
        var reports = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        await reports
            .Artifacts.Where(x => x.Id == fileId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.FilePublished, false));
        await fixture.PublishForOrgA(
            new LocIntel.Contracts.PublishGeneratedFile(
                file.Id,
                file.Key,
                file.Name,
                file.ContentType,
                file.MaxBytes,
                file.CreatedBy,
                file.CreatedAt,
                file.SiteIds,
                file.Origin!,
                file.OriginId!.Value
            )
        );
        await ApiFixture.WaitUntilAsync(
            async () => await reports.Artifacts.AnyAsync(x => x.Id == fileId && x.FilePublished),
            "publication acknowledgement after redelivery"
        );
        Assert.Equal(
            LocIntel.Modules.Storage.Data.FileStatus.Deleted,
            (await db.Files.AsNoTracking().SingleAsync(x => x.Id == fileId)).Status
        );
        Assert.Equal(
            2,
            await scope
                .ServiceProvider.GetRequiredService<ReportingDbContext>()
                .QuotaEntries.CountAsync(x => x.JobId == runId && x.Consumed)
        );
        (await client.PostAsync($"/api/files/{fileId}/restore", null)).EnsureSuccessStatusCode();
        Assert.Equal(zipBytes, await Download(client, runId, zipId));
        await reports
            .Jobs.Where(x => x.Id == runId)
            .ExecuteUpdateAsync(set =>
                set.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1))
                    .SetProperty(x => x.MetadataExpiresAt, DateTimeOffset.UtcNow.AddDays(-1))
            );
        await fixture.PublishForOrgA(new MaintainReports());
        await ApiFixture.WaitUntilAsync(
            async () => !await reports.Artifacts.AnyAsync(x => x.Id == zipId),
            "expired ZIP cleaned up"
        );
        Assert.NotEmpty(await Download(client, runId, fileId));
        Assert.True(await reports.Jobs.AnyAsync(x => x.Id == runId));
    }

    /// <summary>
    /// The run answers in names, and one route owns each artifact. A published PDF
    /// is a site file: the reporting route 404s for it rather than redirecting, so
    /// a client that trusts the OpenAPI response shape cannot be surprised by a 302.
    /// </summary>
    [Fact]
    public async Task Run_names_its_sites_and_serves_only_its_own_bundle()
    {
        var (client, sites) = await Setup(2);
        var id = await Submit(client, sites, new { });
        var job = await Wait(client, id, "Completed");

        var named = job.GetProperty("sites").EnumerateArray().ToArray();
        Assert.Equal(
            sites.OrderBy(x => x).ToArray(),
            named.Select(x => x.GetProperty("id").GetGuid()).OrderBy(x => x).ToArray()
        );
        Assert.All(named, x => Assert.Equal("Workflow site", x.GetProperty("name").GetString()));

        var artifacts = job.GetProperty("artifacts").EnumerateArray().ToArray();
        var pdf = artifacts.First(x =>
            x.GetProperty("contentType").GetString() == "application/pdf"
        );
        var pdfId = pdf.GetProperty("id").GetGuid();
        var name = pdf.GetProperty("name").GetString();
        Assert.StartsWith("Workflow site report ", name);
        Assert.DoesNotContain(pdfId.ToString("N"), name);
        Assert.Equal(pdfId, pdf.GetProperty("fileId").GetGuid());

        // No redirect: the client follows them, so a 302 to the file route would
        // surface here as the file route's 200.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/reports/{id}/artifacts/{pdfId}/download")).StatusCode
        );
        var file = await client.GetFromJsonAsync<JsonElement>($"/api/files/{pdfId}/download");
        Assert.NotEmpty(await client.GetByteArrayAsync(file.GetProperty("url").GetString()));

        // The bundle belongs to the run, and its entries carry the same names.
        var bundle = artifacts.Single(x =>
            x.GetProperty("contentType").GetString() == "application/zip"
        );
        Assert.Equal(JsonValueKind.Null, bundle.GetProperty("fileId").ValueKind);
        var zipId = bundle.GetProperty("id").GetGuid();
        var link = await client.GetFromJsonAsync<JsonElement>(
            $"/api/reports/{id}/artifacts/{zipId}/download"
        );
        using var archive = new ZipArchive(
            new MemoryStream(await client.GetByteArrayAsync(link.GetProperty("url").GetString()))
        );
        var entries = archive.Entries.Select(x => x.FullName).ToArray();
        // Two sites share a name; the bundle still extracts unambiguously.
        Assert.Equal(entries.Length, entries.Distinct().Count());
        Assert.Equal(2, entries.Count(x => x.StartsWith("Workflow site report ")));
        Assert.Contains("manifest.json", entries);
        using var manifest = JsonDocument.Parse(
            new StreamReader(archive.GetEntry("manifest.json")!.Open()).ReadToEnd()
        );
        Assert.All(
            manifest.RootElement.GetProperty("items").EnumerateArray(),
            x =>
                Assert.All(
                    x.GetProperty("sites").EnumerateArray(),
                    s => Assert.Equal("Workflow site", s.GetProperty("name").GetString())
                )
        );
    }

    /// <summary>
    /// One unreachable object must not starve the sweep. Jobs are taken oldest
    /// first, so a job whose artifact key cannot be deleted used to throw out of
    /// the loop and be first again on the next tick: nothing behind it in that
    /// organization was ever cleaned up.
    /// </summary>
    [Fact]
    public async Task A_job_whose_bytes_cannot_be_deleted_does_not_stall_the_ones_behind_it()
    {
        var (client, sites) = await Setup(1);
        var expired = DateTimeOffset.UtcNow.AddDays(-1);
        Guid poisoned;
        Guid following;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            scope
                .ServiceProvider.GetRequiredService<TenantContext>()
                .Set(fixture.OrgA, RegionId.Default);
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var user = await scope
                .ServiceProvider.GetRequiredService<LocIntel.Modules.Identity.Data.IdentityDbContext>()
                .Users.SingleAsync(x => x.Email == ApiFixture.UserA);
            ReportJob Seed(string key, DateTimeOffset created)
            {
                var job = new ReportJob
                {
                    OrgId = fixture.OrgA,
                    RequestedBy = user.Id,
                    ReportType = "workflow-test",
                    DefinitionVersion = 1,
                    Mode = ReportMode.Single,
                    Selection = ReportSelection.Selected,
                    OptionsJson = "{}",
                    SiteIds = sites,
                    State = ReportJobState.Completed,
                    CreatedAt = created,
                    ExpiresAt = expired,
                    MetadataExpiresAt = expired,
                };
                var item = new ReportItem
                {
                    OrgId = fixture.OrgA,
                    JobId = job.Id,
                    SiteIds = sites,
                    State = ReportItemState.Failed,
                };
                job.Items.Add(item);
                job.Artifacts.Add(
                    new ReportArtifact
                    {
                        OrgId = fixture.OrgA,
                        JobId = job.Id,
                        ItemId = item.Id,
                        Revision = 0,
                        Key = key,
                        Name = "attempt.pdf",
                        ContentType = "application/pdf",
                    }
                );
                db.Jobs.Add(job);
                return job;
            }
            // The local store refuses a key that escapes its root, so deleting
            // this artifact's bytes throws where a missing object would not.
            poisoned = Seed("../escapes-the-store", expired.AddMinutes(-10)).Id;
            following = Seed(
                $"{RegionId.Default.Value}/{fixture.OrgA.Value}/reports/clean",
                expired.AddMinutes(-9)
            ).Id;
            await db.SaveChangesAsync();
        }

        await fixture.PublishForOrgA(new MaintainReports());
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                using var scope = fixture.Factory.Services.CreateScope();
                scope
                    .ServiceProvider.GetRequiredService<TenantContext>()
                    .Set(fixture.OrgA, RegionId.Default);
                var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
                return !await db.Jobs.AnyAsync(x => x.Id == following);
            },
            "the sweep to clear the job behind the poisoned one"
        );

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            scope
                .ServiceProvider.GetRequiredService<TenantContext>()
                .Set(fixture.OrgA, RegionId.Default);
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            // The job behind it was swept. The poisoned one is retained with its
            // artifact, so the next tick retries the delete rather than silently
            // dropping the row - and its revision proves the sweep got as far as
            // committing the reclaim before the object store refused.
            Assert.False(await db.Jobs.AnyAsync(x => x.Id == following));
            var kept = await db.Jobs.SingleAsync(x => x.Id == poisoned);
            Assert.Equal(1, kept.Revision);
            Assert.True(await db.Artifacts.AnyAsync(x => x.JobId == poisoned));
        }
    }

    private async Task<(HttpClient Client, Guid[] Sites)> Setup(int count)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var ids = Enumerable.Range(0, count).Select(_ => SiteId.New()).ToArray();
        foreach (var id in ids)
            db.Sites.Add(
                new Site
                {
                    Id = id,
                    OrgId = fixture.OrgA,
                    NodeId = Guid.NewGuid(),
                    Name = "Workflow site",
                    TimeZone = "UTC",
                    Path = new LTree("workflow." + Site.Label(id)),
                }
            );
        await db.SaveChangesAsync();
        return (await fixture.LoginAsync(ApiFixture.UserA), ids.Select(x => x.Value).ToArray());
    }

    private static object Request(Guid[] ids, object options) =>
        new
        {
            reportType = "workflow-test",
            mode = ids.Length == 1 ? "single" : "bulk",
            selection = "selected",
            siteIds = ids,
            options,
        };

    private static async Task<Guid> Submit(HttpClient client, Guid[] ids, object options)
    {
        var response = await client.PostAsJsonAsync("/api/reports", Request(ids, options));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
    }

    private static async Task<JsonElement> Wait(HttpClient client, Guid id, string state)
    {
        JsonElement job = default;
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                job = await client.GetFromJsonAsync<JsonElement>($"/api/reports/{id}");
                return job.GetProperty("state").GetString() == state
                    && (
                        state is "Running" or "Queued"
                        || job.GetProperty("artifacts")
                            .EnumerateArray()
                            .Where(x =>
                                x.GetProperty("contentType").GetString() == "application/pdf"
                            )
                            .All(x => x.GetProperty("fileId").ValueKind == JsonValueKind.String)
                    );
            },
            $"report {id} reaches {state}",
            TimeSpan.FromSeconds(20)
        );
        return job;
    }

    /// <summary>
    /// A published PDF is an ordinary site file and is downloaded from Storage;
    /// only the run's own bundle comes from the reporting route. The run says
    /// which is which through the artifact's fileId.
    /// </summary>
    private static async Task<byte[]> Download(HttpClient client, Guid job, Guid artifact)
    {
        var run = await client.GetFromJsonAsync<JsonElement>($"/api/reports/{job}");
        var row = run.GetProperty("artifacts")
            .EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == artifact);
        var route =
            row.GetProperty("fileId").ValueKind == JsonValueKind.Null
                ? $"/api/reports/{job}/artifacts/{artifact}/download"
                : $"/api/files/{artifact}/download";
        var link = await client.GetFromJsonAsync<JsonElement>(route);
        return await client.GetByteArrayAsync(link.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Durable_single_report_is_a_site_file_shared_with_authorized_site_users()
    {
        var (client, sites) = await Setup(1);
        var id = await Submit(client, sites, new { });
        var job = await Wait(client, id, "Completed");
        var artifact = Assert
            .Single(job.GetProperty("artifacts").EnumerateArray())
            .GetProperty("id")
            .GetGuid();
        Assert.Contains(
            sites[0].ToString(),
            Encoding.UTF8.GetString(await Download(client, id, artifact))
        );
        foreach (var email in new[] { ApiFixture.UserB })
        {
            var other = await fixture.LoginAsync(email);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await other.GetAsync($"/api/reports/{id}")).StatusCode
            );
            Assert.Equal(
                HttpStatusCode.NotFound,
                (
                    await other.GetAsync($"/api/reports/{id}/artifacts/{artifact}/download")
                ).StatusCode
            );
        }
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync($"/api/files/{artifact}/download")).StatusCode
        );
        var otherSiteUser = await fixture.LoginAsync(ApiFixture.UserBoth);
        Assert.Equal(
            HttpStatusCode.OK,
            (await otherSiteUser.GetAsync($"/api/reports/{id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (await otherSiteUser.GetAsync($"/api/files/{artifact}/download")).StatusCode
        );
        var listing = await otherSiteUser.GetFromJsonAsync<JsonElement>(
            $"/api/files?siteId={sites[0]}"
        );
        var file = Assert.Single(listing.GetProperty("items").EnumerateArray());
        Assert.Equal(artifact, file.GetProperty("id").GetGuid());
        Assert.Equal(id, file.GetProperty("originId").GetGuid());
    }

    [Fact]
    public async Task Partial_bulk_retry_retains_successful_bytes_and_rebuilds_the_bundle()
    {
        var (client, sites) = await Setup(2);
        var id = await Submit(client, sites, new { failFirstSite = sites[1] });
        var first = await Wait(client, id, "CompletedWithErrors");
        var pdf = first
            .GetProperty("artifacts")
            .EnumerateArray()
            .Single(x => x.GetProperty("contentType").GetString() == "application/pdf");
        var pdfId = pdf.GetProperty("id").GetGuid();
        var bytes = await Download(client, id, pdfId);
        (await client.PostAsync($"/api/reports/{id}/retry", null)).EnsureSuccessStatusCode();
        var second = await Wait(client, id, "Completed");
        Assert.Equal(bytes, await Download(client, id, pdfId));
        var zipId = second
            .GetProperty("artifacts")
            .EnumerateArray()
            .Single(x => x.GetProperty("contentType").GetString() == "application/zip")
            .GetProperty("id")
            .GetGuid();
        using var zip = new ZipArchive(new MemoryStream(await Download(client, id, zipId)));
        Assert.Equal(3, zip.Entries.Count);
        using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
        Assert.All(
            manifest.RootElement.GetProperty("items").EnumerateArray(),
            x => Assert.Equal("Succeeded", x.GetProperty("State").GetString())
        );
    }

    [Fact]
    public async Task Cancellation_stops_active_work_without_publishing_an_artifact()
    {
        var (client, sites) = await Setup(1);
        var id = await Submit(client, sites, new { hold = true });
        await Wait(client, id, "Running");
        (await client.PostAsync($"/api/reports/{id}/cancel", null)).EnsureSuccessStatusCode();
        var canceled = await Wait(client, id, "Canceled");
        Assert.Empty(canceled.GetProperty("artifacts").EnumerateArray());
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                using var scope = fixture.Factory.Services.CreateScope();
                scope
                    .ServiceProvider.GetRequiredService<TenantContext>()
                    .Set(fixture.OrgA, RegionId.Default);
                return await scope
                    .ServiceProvider.GetRequiredService<ReportingDbContext>()
                    .Jobs.AnyAsync(x => x.Id == id && x.LeaseOwner == null);
            },
            "canceled renderer releases its lease",
            TimeSpan.FromSeconds(10)
        );
    }

    [Fact]
    public async Task Idempotent_submission_replays_the_original_job_and_over_limit_requests_are_rejected()
    {
        var (client, sites) = await Setup(1);
        var key = Guid.NewGuid().ToString();
        async Task<Guid> Send()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/reports")
            {
                Content = JsonContent.Create(Request(sites, new { })),
            };
            request.Headers.Add("Idempotency-Key", key);
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id")
                .GetGuid();
        }
        var first = await Send();
        Assert.Equal(first, await Send());
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await client.PostAsJsonAsync(
                    "/api/reports",
                    Request(Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray(), new { })
                )
            ).StatusCode
        );
        await Wait(client, first, "Completed");
    }
}
