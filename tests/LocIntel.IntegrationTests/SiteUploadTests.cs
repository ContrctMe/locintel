using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Modules.Identity.Access;
using LocIntel.Modules.Identity.Data;
using LocIntel.Modules.Storage.Data;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

public sealed class SiteUploadTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Site_uploads_require_both_scopes_and_appear_as_clean_site_photographs()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        using var otherOwner = await fixture.LoginAsync(ApiFixture.UserB);
        var site = await ApiFixture.EnsureSiteAsync(owner, "Photo site");
        var hiddenSite = await ApiFixture.EnsureSiteAsync(owner, "Other photo site");
        var foreignSite = await ApiFixture.EnsureSiteAsync(otherOwner, "Foreign photo site");
        using var scope = fixture.Factory.Services.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<TenantContext>()
            .Set(fixture.OrgA, RegionId.Default);
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        var tenancy = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var path = (await tenancy.Sites.SingleAsync(x => x.Id == new SiteId(site))).Path.ToString();
        var hiddenPath = (
            await tenancy.Sites.SingleAsync(x => x.Id == new SiteId(hiddenSite))
        ).Path.ToString();
        var user = await identity.Users.SingleAsync(x => x.Email == ApiFixture.ViewerA);
        var membership = await identity.Memberships.SingleAsync(x =>
            x.UserId == user.Id && x.OrgId == fixture.OrgA
        );
        var assignments = new Dictionary<string, Guid>();
        var roles = new List<Guid>();
        foreach (var domain in new[] { "sites", "files" })
        {
            var role = Role.Create(fixture.OrgA, "Photo uploader " + domain);
            roles.Add(role.Id);
            identity.Roles.Add(role);
            identity.RoleGrants.Add(
                new()
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = fixture.OrgA,
                    RoleId = role.Id,
                    Domain = domain,
                    Action = domain == "files" ? "*" : "read",
                }
            );
            var assignment = Guid.CreateVersion7();
            assignments[domain] = assignment;
            identity.MembershipRoles.Add(
                new()
                {
                    Id = assignment,
                    OrgId = fixture.OrgA,
                    MembershipId = membership.Id,
                    RoleId = role.Id,
                    ScopePath = path,
                }
            );
        }
        var exportRole = Role.Create(fixture.OrgA, "Export reader");
        roles.Add(exportRole.Id);
        identity.Roles.Add(exportRole);
        foreach (var (domain, action) in new[] { ("org", "manage"), ("audit", "read") })
            identity.RoleGrants.Add(
                new()
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = fixture.OrgA,
                    RoleId = exportRole.Id,
                    Domain = domain,
                    Action = action,
                }
            );
        identity.MembershipRoles.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                OrgId = fixture.OrgA,
                MembershipId = membership.Id,
                RoleId = exportRole.Id,
            }
        );
        await identity.SaveChangesAsync();
        try
        {
            using var uploader = await fixture.LoginAsync(ApiFixture.ViewerA);
            var bytes = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="
            );
            async Task<HttpResponseMessage> Create(Guid siteId, string name = "site-photo.png") =>
                await uploader.PostAsJsonAsync(
                    "/api/files",
                    new
                    {
                        name,
                        contentType = "image/png",
                        sizeBytes = bytes.Length,
                        siteId,
                    }
                );
            var before = await storage.Files.CountAsync();
            foreach (
                var inaccessible in new[] { Guid.Empty, Guid.NewGuid(), hiddenSite, foreignSite }
            )
            foreach (
                var name in new[]
                {
                    "site-photo.png",
                    "org-export-photo.png",
                    "audit-export-photo.png",
                }
            )
                Assert.Equal(
                    HttpStatusCode.NotFound,
                    (await Create(inaccessible, name)).StatusCode
                );
            foreach (var domain in new[] { "sites", "files" })
            {
                var assignment = assignments[domain];
                await identity
                    .MembershipRoles.Where(x => x.Id == assignment)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopePath, hiddenPath));
                Assert.Equal(HttpStatusCode.NotFound, (await Create(site)).StatusCode);
                await identity
                    .MembershipRoles.Where(x => x.Id == assignment)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopePath, path));
            }
            Assert.Equal(before, await storage.Files.CountAsync());
            var created = await Create(site);
            created.EnsureSuccessStatusCode();
            var body = await created.Content.ReadFromJsonAsync<JsonElement>();
            var fileId = body.GetProperty("fileId").GetGuid();
            (
                await uploader.PutAsync(
                    body.GetProperty("ticket").GetProperty("url").GetString(),
                    new ByteArrayContent(bytes)
                )
            ).EnsureSuccessStatusCode();
            (
                await uploader.PostAsync($"/api/files/{fileId}/complete", null)
            ).EnsureSuccessStatusCode();
            await ApiFixture.WaitUntilAsync(
                async () =>
                    (await uploader.GetFromJsonAsync<JsonElement>($"/api/files/{fileId}"))
                        .GetProperty("status")
                        .GetString() == "Clean",
                "the site photograph to be scanned"
            );
            var photo = Assert.Single(
                (
                    await ApiFixture.GetItemsAsync(uploader, $"/api/files?siteId={site}")
                ).EnumerateArray()
            );
            Assert.Equal(fileId, photo.GetProperty("id").GetGuid());
            Assert.Equal(
                site,
                Assert.Single(photo.GetProperty("siteIds").EnumerateArray()).GetGuid()
            );
            Assert.Equal("image/png", photo.GetProperty("contentType").GetString());
            Assert.Equal(
                HttpStatusCode.OK,
                (await uploader.GetAsync($"/api/files/{fileId}/download")).StatusCode
            );
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await otherOwner.GetAsync($"/api/files/{fileId}")).StatusCode
            );
            Assert.Empty(
                (
                    await ApiFixture.GetItemsAsync(uploader, $"/api/files?siteId={hiddenSite}")
                ).EnumerateArray()
            );
        }
        finally
        {
            await identity
                .MembershipRoles.Where(x => roles.Contains(x.RoleId))
                .ExecuteDeleteAsync();
            await identity.RoleGrants.Where(x => roles.Contains(x.RoleId)).ExecuteDeleteAsync();
            await identity.Roles.Where(x => roles.Contains(x.Id)).ExecuteDeleteAsync();
        }
    }
}
