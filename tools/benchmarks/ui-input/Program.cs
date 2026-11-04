using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lumyte.UI;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

string label = args.Length > 0 ? args[0] : "current";
string? output = args.Length > 1 ? args[1] : null;
var results = new List<object>();
Action<UiElement>? invalidateInput = typeof(UiElement).GetMethod("InvalidateInput", BindingFlags.Instance | BindingFlags.NonPublic)?.CreateDelegate<Action<UiElement>>();
Console.WriteLine($"{label}: {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; CPUs {Environment.ProcessorCount}; tiered {Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"}");
foreach ((string name, int width, int depth, int pointers) in new[]
{
    ("small", 16, 4, 1),
    ("typical", 128, 4, 1),
    ("wide", 1024, 4, 1),
    ("large", 2048, 3, 1),
    ("deep", 1, 64, 1),
    ("eight-pointers", 128, 4, 8),
})
{
    var branches = new Widget[width];
    for (int index = 0; index < width; index++)
    {
        Widget child = Button()[Panel(width: 20, height: 10)];
        for (int level = 0; level < depth; level++)
        {
            child = Panel(height: 10)[child];
        }

        branches[index] = child;
    }

    using var surface = new UiSurface(Stack()[branches]);
    surface.SetViewport(new(512, Math.Max(512, width * 10)));
    surface.UpdateLayout();
    var inside = new UiPointerInput[pointers];
    var outside = new UiPointerInput[pointers];
    for (int pointer = 0; pointer < pointers; pointer++)
    {
        var position = new System.Numerics.Vector2(5, 5 + (pointer * 10));
        if (!surface.Dispatch(new UiPointerInput(pointer, UiPointerPhase.Down, position)))
        {
            throw new InvalidOperationException("Benchmark failed to acquire its capture.");
        }

        inside[pointer] = new(pointer, UiPointerPhase.Move, position);
        outside[pointer] = new(pointer, UiPointerPhase.Move, new(500, position.Y));
    }

    Measure(name, "captured-move-reuse-event", 1 + (width * (depth + 2)), 1, index =>
    {
        int pointer = index % pointers;
        if (!surface.Dispatch(((index / pointers) & 1) == 0 ? inside[pointer] : outside[pointer]))
        {
            throw new InvalidOperationException("Benchmark lost its capture.");
        }
    });
    if (name == "typical")
    {
        Measure(name, "captured-move-create-event", 1 + (width * (depth + 2)), 1, index =>
        {
            UiPointerInput source = (index & 1) == 0 ? inside[0] : outside[0];
            if (!surface.Dispatch(new UiPointerInput(source.PointerId, source.Phase, source.Position)))
            {
                throw new InvalidOperationException("Benchmark lost its capture.");
            }
        });
        surface.Dispatch(new UiFocusLostInput());
        var down = new UiPointerInput(0, UiPointerPhase.Down, new(5, 5));
        var up = new UiPointerInput(0, UiPointerPhase.Up, new(5, 5));
        Measure(name, "down-up-reuse-event", 1 + (width * (depth + 2)), 2, _ =>
        {
            if (!surface.Dispatch(down) || !surface.Dispatch(up))
            {
                throw new InvalidOperationException("Benchmark click was not consumed.");
            }
        });
        surface.Dispatch(down);
    }

    if (name is "typical" or "large")
    {
        Measure(name, "layout-and-input-refresh", 1 + (width * (depth + 2)), 1, index =>
        {
            surface.SetViewport(new(511 + (index & 1), Math.Max(512, width * 10)));
            surface.UpdateLayout();
        });
        if (invalidateInput is not null)
        {
            Measure(name, "input-only-refresh", 1 + (width * (depth + 2)), 1, _ =>
            {
                invalidateInput(surface.RootElement);
                surface.UpdateLayout();
            });
        }
    }

    surface.Dispatch(new UiFocusLostInput());
    var uncapturedInside = new UiPointerInput(0, UiPointerPhase.Move, new(5, 5));
    var uncapturedOutside = new UiPointerInput(0, UiPointerPhase.Move, new(500, 5));
    Measure(name, "uncaptured-move-in-viewport", 1 + (width * (depth + 2)), 1, index =>
    {
        if (surface.Dispatch((index & 1) == 0 ? uncapturedInside : uncapturedOutside))
        {
            throw new InvalidOperationException("Uncaptured Move unexpectedly became consumed.");
        }
    });
    if (name == "large")
    {
        var down = new UiPointerInput(0, UiPointerPhase.Down, new(5, 5));
        var up = new UiPointerInput(0, UiPointerPhase.Up, new(5, 5));
        Measure(name, "down-up-reuse-event", 1 + (width * (depth + 2)), 2, _ =>
        {
            if (!surface.Dispatch(down) || !surface.Dispatch(up))
            {
                throw new InvalidOperationException("Benchmark click was not consumed.");
            }
        });
    }
}

if (output is not null)
{
    File.WriteAllText(output, JsonSerializer.Serialize(new { label, measuredAtUtc = DateTimeOffset.UtcNow, serverGc = System.Runtime.GCSettings.IsServerGC, tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), stopwatchFrequency = Stopwatch.Frequency, framework = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription, processors = Environment.ProcessorCount, results }, new JsonSerializerOptions { WriteIndented = true }));
}

void Measure(string shape, string scenario, int widgets, int dispatches, Action<int> action)
{
    for (int index = 0; index < 128; index++)
    {
        action(index);
    }

    long pilot = Stopwatch.GetTimestamp();
    for (int index = 0; index < 32; index++)
    {
        action(index);
    }

    double seconds = Stopwatch.GetElapsedTime(pilot).TotalSeconds;
    int iterations = Math.Clamp((int)(0.10 / Math.Max(seconds / 32, 1e-9)), 16, 5000000);
    iterations = (iterations + 1) & ~1;
    double[] ns = new double[7];
    double[] bytes = new double[7];
    int[] gen0 = new int[7];
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    for (int sample = 0; sample < ns.Length; sample++)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        int collections = GC.CollectionCount(0);
        long started = Stopwatch.GetTimestamp();
        for (int index = 0; index < iterations; index++)
        {
            action(index);
        }

        long elapsed = Stopwatch.GetTimestamp() - started;
        bytes[sample] = (double)(GC.GetAllocatedBytesForCurrentThread() - allocated) / (iterations * dispatches);
        ns[sample] = elapsed * (1e9 / Stopwatch.Frequency) / (iterations * dispatches);
        gen0[sample] = GC.CollectionCount(0) - collections;
    }

    double[] ordered = ns.Order().ToArray();
    double median = ordered[ordered.Length / 2];
    double allocatedMedian = bytes.Order().ElementAt(bytes.Length / 2);
    results.Add(new { shape, scenario, widgets, dispatches, iterations, nsMedian = median, nsMin = ordered[0], nsMax = ordered[^1], bytesPerOperation = allocatedMedian, nsSamples = ns, bytesSamples = bytes, gen0Collections = gen0 });
    Console.WriteLine($"{shape,-14} {scenario,-28} N={widgets,5} {median / 1000,10:F3} us/op {allocatedMedian,12:F1} B/op [{ordered[0] / 1000:F3}, {ordered[^1] / 1000:F3}]");
}
