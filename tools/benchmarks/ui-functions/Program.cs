using System.Diagnostics;
using System.Text.Json;
using Lumyte.Signals;
using Lumyte.UI;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Tests.FunctionTestUi;

foreach (int count in new[] { 100, 1000 })
{
    Signal<string>[] sources = Enumerable.Range(0, count).Select(_ => Signal.Create("0")).ToArray();
    using var surface = new UiSurface(Panel()[sources.Select(source => Counter(source)).ToArray()]);
    surface.SetViewport(new(800, 600));
    surface.UpdateLayout();
    Lumyte.UI.Compose.Definitions.Widget initial = surface.Root;
    Lumyte.UI.Compose.Definitions.Widget sibling = ((Lumyte.UI.Compose.Definitions.Panel)initial).Children[0];
    int target = count / 2;
    int tick = 0;
    void Update()
    {
        sources[target].Value = (++tick).ToString(System.Globalization.CultureInfo.InvariantCulture);
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

    var panel = (Lumyte.UI.Compose.Definitions.Panel)surface.Root;
    string? actual = ((UiFunctionWidget)panel.Children[target]).Content.AutomationName;
    if (actual != tick.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":2")
    {
        throw new InvalidOperationException("The changed function was not published.");
    }

    bool retained = ReferenceEquals(initial, surface.Root) && ReferenceEquals(sibling, panel.Children[0]);
    if (args.Contains("--expect-local") && !retained)
    {
        throw new InvalidOperationException("An unrelated declaration was reconstructed.");
    }

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Name = $"one-function-of-{count}",
        RetainedUnrelatedDeclarations = retained,
        Batches = batches.Select(batch => new { batch.Nanoseconds, batch.Bytes }),
        MedianNanoseconds = batches.Select(batch => batch.Nanoseconds).Order().ElementAt(2),
        MedianBytes = batches.Select(batch => batch.Bytes).Order().ElementAt(2),
    }));
}
