using System.Diagnostics;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Api;

/// <summary>
/// Dependency probes for the on-call human (maturity review, hole 5):
/// /healthz stays a cheap liveness answer; THIS asks each self-hosted
/// dependency to actually respond - database round trip, object-store
/// write/read/delete, SMTP connect when smtp is the configured transport.
/// Operator-gated: dependency status is an internal fact, not a public one.
/// Vendor SaaS dependencies (WorkOS, Stripe) are deliberately absent -
/// their status pages and our traces cover them, and probing them burns
/// rate limits to learn less.
/// </summary>
public static class OperatorHealthEndpoint
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    public static void MapOperatorHealthEndpoint(this WebApplication app)
    {
        app.MapGet(
            "/api/operator/health",
            async (
                IPrincipalAccessor accessor,
                IOperatorContext operators,
                LocIntel.Modules.Identity.Data.IdentityDbContext db,
                IObjectStore store,
                IConfiguration configuration,
                CancellationToken ct
            ) =>
            {
                if (!await operators.IsOperatorAsync(accessor.Current, ct))
                    return Results.Unauthorized();

                var checks = new List<object>
                {
                    await ProbeAsync(
                        "database",
                        async token => await db.Database.ExecuteSqlRawAsync("SELECT 1", token),
                        ct
                    ),
                    await ProbeAsync(
                        "objectStore",
                        async token =>
                        {
                            var key = $"healthcheck/{Guid.CreateVersion7()}";
                            using var probe = new MemoryStream("ok"u8.ToArray());
                            await store.WriteAsync(key, probe, "text/plain", token);
                            await using (var read = await store.OpenReadAsync(key, token))
                                await read.CopyToAsync(Stream.Null, token);
                            await store.DeleteAsync(key, token);
                        },
                        ct
                    ),
                };
                if (configuration["Notifications:Transport"] == "smtp")
                    checks.Add(
                        await ProbeAsync(
                            "smtp",
                            async token =>
                            {
                                var smtp = configuration
                                    .GetSection("Notifications:Smtp")
                                    .Get<LocIntel.Integrations.Smtp.SmtpOptions>()!;
                                using var client = new MailKit.Net.Smtp.SmtpClient();
                                await client.ConnectAsync(
                                    smtp.Host,
                                    smtp.Port,
                                    smtp.UseStartTls
                                        ? MailKit.Security.SecureSocketOptions.StartTls
                                        : MailKit.Security.SecureSocketOptions.None,
                                    token
                                );
                                await client.DisconnectAsync(quit: true, token);
                            },
                            ct
                        )
                    );
                return Results.Ok(new { checks });
            }
        );
    }

    private static async Task<object> ProbeAsync(
        string name,
        Func<CancellationToken, Task> probe,
        CancellationToken ct
    )
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProbeTimeout);
            await probe(timeout.Token);
            return new
            {
                name,
                ok = true,
                latencyMs = stopwatch.ElapsedMilliseconds,
                error = (string?)null,
            };
        }
        catch (Exception exception)
        {
            return new
            {
                name,
                ok = false,
                latencyMs = stopwatch.ElapsedMilliseconds,
                error = exception is OperationCanceledException
                    ? $"timed out after {ProbeTimeout.TotalSeconds}s"
                    : exception.Message,
            };
        }
    }
}
