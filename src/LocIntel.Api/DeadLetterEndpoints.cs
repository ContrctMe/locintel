using LocIntel.Platform.Kernel;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.DeadLetterManagement;

namespace LocIntel.Api;

/// <summary>
/// The operator's window into failed background work (operability review,
/// item 1): every message that exhausted its chances lands in Wolverine's
/// dead-letter store, and until now nothing surfaced it. Replay marks the
/// envelope replayable - the durability agent re-injects it, so a transient
/// failure is FIXED from here, not just labeled. Platform-global on purpose:
/// failures are infra, the tenant id rides along for attribution.
/// </summary>
public static class DeadLetterEndpoints
{
    public static void MapOperatorDeadLetterEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/operator/dead-letters",
            async (
                IPrincipalAccessor accessor,
                LocIntel.Platform.Kernel.IOperatorContext operators,
                IMessageStore store,
                int? limit,
                CancellationToken ct
            ) =>
            {
                if (!await operators.IsOperatorAsync(accessor.Current, ct))
                    return Results.Unauthorized();
                var results = await store.DeadLetters.QueryAsync(
                    new DeadLetterEnvelopeQuery { PageSize = Math.Clamp(limit ?? 50, 1, 200) },
                    ct
                );
                return Results.Ok(
                    new
                    {
                        total = results.TotalCount,
                        items = results.Envelopes.Select(e => new
                        {
                            id = e.Id,
                            messageType = ShortTypeName(e.MessageType),
                            exceptionType = ShortTypeName(e.ExceptionType),
                            exceptionMessage = e.ExceptionMessage,
                            sentAt = e.SentAt,
                            tenantId = e.Envelope.TenantId,
                            replayable = e.Replayable,
                        }),
                    }
                );
            }
        );

        app.MapPost(
            "/api/operator/dead-letters/{id:guid}/replay",
            async (
                Guid id,
                IPrincipalAccessor accessor,
                LocIntel.Platform.Kernel.IOperatorContext operators,
                IMessageStore store,
                CancellationToken ct
            ) =>
            {
                if (!await operators.IsOperatorAsync(accessor.Current, ct))
                    return Results.Unauthorized();
                await store.DeadLetters.ReplayAsync(new DeadLetterEnvelopeQuery([id]), ct);
                return Results.Accepted();
            }
        );

        app.MapDelete(
            "/api/operator/dead-letters/{id:guid}",
            async (
                Guid id,
                IPrincipalAccessor accessor,
                LocIntel.Platform.Kernel.IOperatorContext operators,
                IMessageStore store,
                CancellationToken ct
            ) =>
            {
                if (!await operators.IsOperatorAsync(accessor.Current, ct))
                    return Results.Unauthorized();
                await store.DeadLetters.DiscardAsync(new DeadLetterEnvelopeQuery([id]), ct);
                return Results.NoContent();
            }
        );
    }

    /// <summary>"LocIntel.Modules.X.SomeMessage, Assembly" -> "SomeMessage".</summary>
    private static string ShortTypeName(string fullName) => fullName.Split(',')[0].Split('.')[^1];
}
