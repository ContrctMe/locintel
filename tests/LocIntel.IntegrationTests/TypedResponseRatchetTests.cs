using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The typed-response RATCHET (maturity review design debt): an endpoint
/// whose OpenAPI schema is the IResult stub generates a client that looks
/// safe while accepting anything. Every such operation is pinned below; a
/// NEW one fails this test (declare the response type - a named record plus
/// [ProducesResponseType], see SiteAttributeEndpoints), and converting an
/// old one fails it too until its line is DELETED here. The list only
/// shrinks.
/// </summary>
public class TypedResponseRatchetTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly string[] Grandfathered =
    [
        "GET /api/admin/audit-config",
        "PUT /api/admin/audit-config",
        "GET /api/api-keys",
        "POST /api/api-keys",
        "DELETE /api/api-keys/{id}",
        "POST /api/api-keys/{id}/rotate",
        "POST /api/audit/export",
        "GET /api/audit/{kind}",
        "POST /api/billing/checkout",
        "POST /api/billing/portal",
        "POST /api/checklists/check",
        "POST /api/checklists/templates",
        "DELETE /api/checklists/templates/{id}",
        "GET /api/connectors",
        "POST /api/connectors",
        "DELETE /api/connectors/{id}",
        "PUT /api/connectors/{id}",
        "POST /api/connectors/{id}/sync",
        "GET /api/contacts",
        "DELETE /api/contacts/{id}",
        "GET /api/entitlements",
        "POST /api/files",
        "DELETE /api/files/{id}",
        "POST /api/files/{id}/complete",
        "GET /api/files/{id}/download",
        "POST /api/files/{id}/hold",
        "POST /api/files/{id}/restore",
        "GET /api/grant-exceptions",
        "POST /api/grant-exceptions",
        "DELETE /api/grant-exceptions/{id}",
        "GET /api/hierarchy",
        "POST /api/hierarchy",
        "POST /api/hierarchy/nodes",
        "DELETE /api/hierarchy/nodes/{id}",
        "PUT /api/hierarchy/nodes/{id}",
        "POST /api/hierarchy/nodes/{id}/move",
        "GET /api/ingest/batches",
        "GET /api/ingest/batches/{id}",
        "POST /api/ingest/batches/{id}/commit",
        "POST /api/ingest/batches/{id}/discard",
        "POST /api/ingest/uploads",
        "GET /api/members/invitations",
        "POST /api/members/invitations",
        "DELETE /api/members/invitations/{invitationId}",
        "POST /api/members/leave",
        "DELETE /api/members/{userId}",
        "GET /api/operator/orgs",
        "GET /api/operator/orgs/{orgId}/entitlements",
        "PUT /api/operator/orgs/{orgId}/entitlements/{code}",
        "POST /api/operator/orgs/{orgId}/entitlements/{code}/exceptions",
        "POST /api/operator/orgs/{orgId}/export",
        "POST /api/operator/orgs/{orgId}/offboard",
        "POST /api/operator/orgs/{orgId}/reactivate",
        "POST /api/operator/orgs/{orgId}/suspend",
        "GET /api/operator/suppressions",
        "DELETE /api/operator/suppressions/{id}",
        "GET /api/operator/users",
        "PUT /api/org",
        "POST /api/org/close/cancel",
        "POST /api/org/export",
        "POST /api/orgs",
        "GET /api/roles",
        "POST /api/roles",
        "DELETE /api/roles/{id}",
        "PUT /api/roles/{id}",
        "POST /api/roles/{id}/assign",
        "DELETE /api/roles/{id}/assign/{userId}",
        "GET /api/settings/{id}",
        "DELETE /api/sites/attributes/{id}",
        "GET /api/sites/{id}/closures",
        "POST /api/sites/{id}/closures",
        "DELETE /api/sites/{id}/closures/{date}",
        "GET /api/sites/{id}/schedules",
        "POST /api/sites/{id}/schedules",
        "DELETE /api/sites/{id}/schedules/{scheduleId}",
        "GET /api/sites/{id}/windows",
        "GET /api/webhooks",
        "POST /api/webhooks",
        "DELETE /api/webhooks/{id}",
        "GET /api/webhooks/{id}/deliveries",
        "POST /api/webhooks/{id}/ping",
        "POST /api/webhooks/{id}/rotate-secret",
        "POST /auth/directory/webhook",
        "POST /auth/impersonation/stop",
        "POST /billing/webhook",
        "POST /notifications/bounce",
        "GET /public/org",
    ];

    [Fact]
    public async Task Untyped_operations_only_ever_shrink()
    {
        var spec = await fixture.GuestClient().GetStringAsync("/openapi/v1.json");
        using var doc = JsonDocument.Parse(spec);
        var untyped = new HashSet<string>();
        foreach (var path in doc.RootElement.GetProperty("paths").EnumerateObject())
        foreach (var op in path.Value.EnumerateObject())
        {
            if (!op.Value.TryGetProperty("responses", out var responses))
                continue;
            foreach (var response in responses.EnumerateObject())
                if (
                    response.Value.TryGetProperty("content", out var content)
                    && content.TryGetProperty("application/json", out var json)
                    && json.TryGetProperty("schema", out var schema)
                    && schema.TryGetProperty("$ref", out var reference)
                    && reference.GetString()!.EndsWith("/IResult")
                )
                {
                    untyped.Add($"{op.Name.ToUpperInvariant()} {path.Name}");
                    break;
                }
        }

        var newcomers = untyped.Except(Grandfathered).Order().ToArray();
        Assert.True(
            newcomers.Length == 0,
            "new UNTYPED endpoints (declare a response type instead of grandfathering): "
                + string.Join(", ", newcomers)
        );
        var converted = Grandfathered.Except(untyped).Order().ToArray();
        Assert.True(
            converted.Length == 0,
            "these are typed now - delete their lines from Grandfathered so the ratchet holds: "
                + string.Join(", ", converted)
        );
    }
}
