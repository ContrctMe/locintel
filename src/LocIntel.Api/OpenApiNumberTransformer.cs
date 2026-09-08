using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LocIntel.Api;

/// <summary>
/// ASP.NET's web JSON defaults accept a number written as a string, and the
/// OpenAPI generator faithfully types every numeric property as
/// <c>integer | string</c>. The API never sends a number as a string, so the
/// contract should not say it might: without this, the generated client
/// typed every count as <c>number | string</c> and the console wrapped
/// fifty values in <c>Number()</c> to read them.
/// </summary>
public sealed class OpenApiNumberTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (
            schema.Type is { } type
            && type.HasFlag(JsonSchemaType.String)
            && (type.HasFlag(JsonSchemaType.Integer) || type.HasFlag(JsonSchemaType.Number))
        )
            schema.Type = type & ~JsonSchemaType.String;
        return Task.CompletedTask;
    }
}
