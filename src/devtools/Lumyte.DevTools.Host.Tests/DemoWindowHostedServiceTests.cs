using Microsoft.Extensions.Hosting;
namespace Lumyte.DevTools.Host.Tests;

public sealed class DemoWindowHostedServiceTests
{
    [Fact]
    public async Task StartsAndStopsWindowRunnerDeterministically()
    {
        DevToolsHub hub = new();
        using DemoInputDomain input = new(hub);
        var runner = new FakeRunner();
        var lifetime = new FakeLifetime();
        var service = new DemoWindowHostedService(input, runner, lifetime);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.True(runner.Started);
        Assert.True(runner.Stopped);
        Assert.False(lifetime.StopRequested);
    }
    private sealed class FakeRunner : IDemoWindowRunner { public bool Started { get; private set; } public bool Stopped { get; private set; } public void Run(DemoInputDomain input, CancellationToken token, Action closed, Action started) { Started = true; started(); token.WaitHandle.WaitOne(); Stopped = true; } }
    private sealed class FakeLifetime : IHostApplicationLifetime { private readonly CancellationTokenSource started = new(), stopping = new(), stopped = new(); public CancellationToken ApplicationStarted => started.Token; public CancellationToken ApplicationStopping => stopping.Token; public CancellationToken ApplicationStopped => stopped.Token; public bool StopRequested { get; private set; } public void StopApplication() { StopRequested = true; stopping.Cancel(); } }
}
