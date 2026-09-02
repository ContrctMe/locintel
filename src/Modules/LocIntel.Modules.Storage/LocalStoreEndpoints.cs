using LocIntel.Platform.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace LocIntel.Modules.Storage;

/// <summary>
/// The local adapter's "storage host": tokenized PUT/GET, mirroring what S3
/// presigned URLs / Azure SAS do. Mapped only when LocalObjectStore is the
/// registered adapter. Tokens are single-use and short-lived; no auth on
/// purpose - that is the ticket model (ADR 19).
/// </summary>
public static class LocalStoreEndpoints
{
    public static IEndpointRouteBuilder MapLocalObjectStore(this IEndpointRouteBuilder app)
    {
        app.MapPut(
            "/objects/upload/{token}",
            async (string token, HttpContext http, IObjectStore store, CancellationToken ct) =>
            {
                if (store is not LocalObjectStore local || local.Redeem(token) is not { } ticket)
                    return Results.NotFound();
                if (http.Request.ContentLength > ticket.maxBytes)
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                await store.WriteAsync(
                    ticket.key,
                    http.Request.Body,
                    http.Request.ContentType ?? "application/octet-stream",
                    ct
                );
                return Results.NoContent();
            }
        );

        app.MapGet(
            "/objects/download/{token}",
            (string token, IObjectStore store) =>
            {
                if (store is not LocalObjectStore local || local.Redeem(token) is not { } ticket)
                    return Results.NotFound();
                return Results.File(local.PathFor(ticket.key));
            }
        );

        return app;
    }
}
