using System.Diagnostics;
using System.Text.Json;
using Lumyte.Signals;
using Lumyte.UI;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

foreach (int count in new[] { 100, 1000 })
{
    Signal<float?>[] sources = Enumerable.Range(0, count).Select(_ => Signal.Create<float?>(10)).ToArray();
    using var surface = new UiSurface(Panel()[sources.Select(source => Rectangle(width: source, height: 10)).ToArray()]);
    surface.SetViewport(new(800, 600));
    surface.UpdateLayout();
    Widget initial = surface.Root;
    UiElement initialInstance = surface.RootElement;
    UiElement sibling = initialInstance.Children[0];
    Widget siblingDeclaration = sibling.Widget;
    int target = count / 2;
    UiElement changedInstance = initialInstance.Children[target];
    int tick = 0;
    void Update()
    {
        sources[target].Value = 10 + (++tick % 20);
        surface.UpdateLayout();
    }

    for (int warmup = 0; warmup < 5; warmup++)
    {
        Update();
    }

    var batches = new List<(double Nanoseconds, double Bytes)>();
    for (int batch = 0; batch < 5; batch++)
    {
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int iteration = 0; iteration < 100; iteration++)
        {
            Update();
        }

        long elapsed = Stopwatch.GetTimestamp() - start;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        batches.Add((elapsed * (1_000_000_000.0 / Stopwatch.Frequency) / 100, allocated / 100.0));
    }

    UiElement panel = surface.RootElement;
    if (panel.Children[target].Widget.Width != 10 + (tick % 20))
    {
        throw new InvalidOperationException("The changed widget was not published.");
    }

    bool retained = ReferenceEquals(initial, surface.Root) && ReferenceEquals(initialInstance, panel)
        && ReferenceEquals(sibling, panel.Children[0]) && ReferenceEquals(siblingDeclaration, sibling.Widget)
        && ReferenceEquals(changedInstance, panel.Children[target]);
    if (args.Contains("--expect-local") && !retained)
    {
        throw new InvalidOperationException("An unrelated declaration was reconstructed.");
    }

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Name = $"one-widget-of-{count}",
        RetainedUnrelatedDeclarations = retained,
        Batches = batches.Select(batch => new { batch.Nanoseconds, batch.Bytes }),
        MedianNanoseconds = batches.Select(batch => batch.Nanoseconds).Order().ElementAt(2),
        MedianBytes = batches.Select(batch => batch.Bytes).Order().ElementAt(2),
    }));
}
