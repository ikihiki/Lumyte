using Lumyte.Execution;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Owns the dedicated native-work owner used by public consumers.</summary>
internal sealed class PlanningHost : IAsyncDisposable
{
    private readonly ExecutionRuntime _runtime;

    internal PlanningHost()
    {
        Module = new();
        _runtime = new([Module]);
        _runtime.PrepareOnCurrentHost(OwnerPlacement.Dedicated, CpuExecutionMode.ThreadPool, 1, new ExecutionOptions());
        _runtime.StartAsync().GetAwaiter().GetResult();
    }

    internal ExecutionRuntime Runtime => _runtime;

    internal GraphicsExecutionModule Module { get; }

    public ValueTask DisposeAsync() => _runtime.StopAsync();
}
