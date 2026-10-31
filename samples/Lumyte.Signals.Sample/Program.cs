using Lumyte.Signals;

var count = Signal.Create(0);
Computed<int> doubled = Signal.Computed(() => count.Value * 2);
var observed = new List<int>();
using (SignalEffect effect = Signal.Effect(() => observed.Add(doubled.Value)))
{
    Signal.Batch(() =>
    {
        count.Value = 1;
        count.Value = 2;
    });
}

count.Value = 3;
if (!observed.SequenceEqual([0, 4]) || doubled.Value != 6)
{
    throw new InvalidOperationException("Independent graph delivery or cleanup failed.");
}

var pending = new TaskCompletionSource<string>();
using AsyncSignal<string> asynchronous = Signal.FromAsync(async cancellationToken =>
{
    await Task.Yield();
    return await pending.Task.WaitAsync(cancellationToken);
});
Computed<bool> loading = Signal.Computed(() => asynchronous.Value.IsLoading);
if (!loading.Value)
{
    throw new InvalidOperationException("The asynchronous operation should start pending.");
}

pending.SetResult("ready");
AsyncSignalState<string> completed = await asynchronous.Completion;
if (!completed.HasValue || completed.Result != "ready" || loading.Value)
{
    throw new InvalidOperationException("Asynchronous conversion or computed delivery failed.");
}

AsyncSignalState<string> refreshed = await asynchronous.ReloadAsync();
if (refreshed.Result != "ready")
{
    throw new InvalidOperationException("Asynchronous reloading failed.");
}

Console.WriteLine("Signals smoke passed: async conversion, reload, independent graph, computed, batch, observer cleanup and shared values.");
