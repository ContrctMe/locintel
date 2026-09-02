using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.Platform.Kernel;

/// <summary>
/// Runs work AS a target org: a fresh DI scope whose TenantContext carries
/// the org, so new connections open with the right RLS session (read-time
/// rule - an already-open connection cannot change tenant). This is how
/// operator endpoints reach into a tenant, and how pre-tenant requests (the
/// auth callback) do tenant work.
/// </summary>
public static class TenantScope
{
    public static async Task<T> RunAsAsync<T>(
        IServiceProvider services,
        OrgId org,
        Func<IServiceProvider, Task<T>> action
    )
    {
        await using var scope = services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(org, RegionId.Default);
        return await action(scope.ServiceProvider);
    }

    public static Task RunAsAsync(
        IServiceProvider services,
        OrgId org,
        Func<IServiceProvider, Task> action
    ) =>
        RunAsAsync(
            services,
            org,
            async sp =>
            {
                await action(sp);
                return true;
            }
        );
}
