using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LocIntel.Platform.Messaging;

/// <summary>
/// "The host has finished starting" as an awaitable, for background services
/// that publish messages: Wolverine cannot accept a publish until its own
/// hosted service has run, and hosted services start in registration order.
/// </summary>
public static class HostStarted
{
    public static Task WaitAsync(IServiceProvider services, CancellationToken ct)
    {
        var lifetime = services.GetRequiredService<IHostApplicationLifetime>();
        if (lifetime.ApplicationStarted.IsCancellationRequested)
            return Task.CompletedTask;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        ct.Register(() => started.TrySetCanceled(ct));
        return started.Task;
    }
}
