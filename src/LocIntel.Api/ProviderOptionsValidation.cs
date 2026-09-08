namespace LocIntel.Api;

internal static class ProviderOptionsValidation
{
    public static bool IsHttpUrl(string? value) =>
        value is null
        || Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    public static bool IsDevelopmentOrTesting(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    public static bool IsProviderUrl(string? value, IHostEnvironment environment) =>
        IsHttpUrl(value)
        && (
            value is null || IsDevelopmentOrTesting(environment) || new Uri(value).Scheme == "https"
        );

    public static bool CredentialsMatch(string? first, string? second) =>
        first is null && second is null
        || !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second);
}
