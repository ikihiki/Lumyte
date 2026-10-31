using System.Collections.Concurrent;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Checks asynchronous conversion uses the surface's ordinary owner delivery boundary.</summary>
public sealed class AsyncSignalTests
{
    /// <summary>Checks a completion queues UI work and reconstructs the declaration on delivery.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task CompletionUsesTheSurfaceDispatcherAsync()
    {
        var pending = new TaskCompletionSource<string>();
        using AsyncSignal<string> signal = Signal.FromAsync(() => pending.Task);
        Computed<string?> caption = Signal.Computed(() => signal.Value.HasValue ? signal.Value.Result : "loading");
        var dispatcher = new Dispatcher();
        using var surface = new UiSurface(Button(automationName: caption), dispatcher);
        Definitions.Widget before = surface.Root;
        Assert.Equal("loading", before.AutomationName);
        pending.SetResult("ready");
        await signal.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(before, surface.Root);
        dispatcher.Drain();
        Assert.NotSame(before, surface.Root);
        Assert.Equal("ready", surface.Root.AutomationName);
        Assert.Equal("loading", before.AutomationName);
    }

    private sealed class Dispatcher : IUiDispatcher
    {
        private readonly ConcurrentQueue<Action> _pending = new();

        public bool CheckAccess() => true;

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            return new(InvokeAsync(
                () =>
                {
                    action();
                    return true;
                },
                cancellationToken).AsTask());
        }

        public ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Enqueue(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    completion.SetResult(action());
                }
                catch (Exception error)
                {
                    completion.SetException(error);
                }
            });
            return new(completion.Task);
        }

        internal void Drain()
        {
            while (_pending.TryDequeue(out Action? action))
            {
                action();
            }
        }
    }
}
