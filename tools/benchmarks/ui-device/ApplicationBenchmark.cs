using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Desktop;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

internal static class ApplicationBenchmark
{
    internal static async Task RunAsync(int count, int warmup, int frames, string output, string label, bool changing, bool allChanging, bool layoutChanging, bool detailed)
    {
        PropertyInfo callback = typeof(UiHostedDrawingSource).GetProperty("FrameCompleted", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Apply application-instrumentation.patch before building this mode.");
        var color = Signal.Create(new Color2D(1, 0, 0, 1));
        var width = Signal.Create<float?>(800f / Math.Min(count, 100));
        UiApplicationBuilder builder = UiApplication.CreateBuilder().Configure(new() { FrameInterval = TimeSpan.FromSeconds(1d / 120) }).UseDesktop(new() { Title = "Lumyte UI 120 Hz sustained benchmark", ClientSize = new(800, 600) });
        builder.UseRoot(_ =>
        {
            int columns = Math.Min(count, 100);
            int rows = (count + columns - 1) / columns;
            Widget Item(int column, int row) => layoutChanging
                ? Rectangle(width: width, height: 600f / rows, fill: color)
                : Rectangle(width: 800f / columns, height: 600f / rows, fill: allChanging || column * rows + row == count - 1 ? color : new Color2D(1, 0, 0, 1));
            Widget[] children = Enumerable.Range(0, columns).Select(column => (Widget)Stack()[Enumerable.Range(0, rows).Where(row => column * rows + row < count).Select(row => Item(column, row)).ToArray()]).ToArray();
            return Stack(orientation: Lumyte.UI.UiOrientation.Horizontal)[children];
        });
        await using UiApplication app = builder.Build();
        using DetailedDiagnostics? details = detailed ? new() : null;
        var samples = new List<Sample>(frames);
        long previous = 0;
        long allocated = 0;
        int completed = 0;
        int skipped = 0;
        Guid? primary = null;
        object? environment = null;
        var phases = new Dictionary<string, double>();
        PropertyInfo? phaseCallback = typeof(UiHostPresenter).GetProperty("Phase", BindingFlags.NonPublic | BindingFlags.Static);
        phaseCallback?.SetValue(null, (Action<string, double>)((name, value) => phases[name] = value));
        callback.SetValue(null, (Action<UiHostedDrawingSource, bool>)((source, presented) =>
        {
            if (app.State != UiApplicationState.Running) return;
            primary ??= source.SourceId;
            if (source.SourceId != primary) return;
            if (!presented) { skipped++; return; }
            if (environment is null)
            {
                object device = typeof(UiPlatformApplicationDriver).GetField("_device", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app.Driver)!;
                object adapter = device.GetType().GetField("_adapter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(device)!;
                object info = adapter.GetType().GetMethod("GetInfo", Type.EmptyTypes)!.Invoke(adapter, null)!;
                environment = new { Adapter = info.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(info)?.ToString()), Size = source.CaptureState().ClientSize.ToString(), Scale = source.CaptureState().Scale.ToString() };
            }
            long now = Stopwatch.GetTimestamp();
            long bytes = GC.GetTotalAllocatedBytes(false);
            DetailedDiagnostics.Snapshot? detail = details?.Capture();
            if (previous != 0 && completed >= warmup && samples.Count < frames)
            {
                DeviceBenchmarkEvents.Log.Frame(samples.Count);
                samples.Add(new(samples.Count, Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds, bytes - allocated, phaseCallback is null ? null : new(phases), detail, now));
            }
            else if (completed == warmup - 1) DeviceBenchmarkEvents.Log.Frame(-1);
            phases.Clear();
            previous = now;
            allocated = bytes;
            completed++;
            long modelStarted = details is null ? 0 : Stopwatch.GetTimestamp();
            long modelAllocated = details is null ? 0 : GC.GetAllocatedBytesForCurrentThread();
            if (changing) color.Value = completed % 2 == 0 ? new(1, 0, 0, 1) : new(0, 0, 1, 1);
            if (changing && layoutChanging) width.Value = (800f / Math.Min(count, 100)) * (completed % 2 == 0 ? 1 : 0.95f);
            details?.SourceUpdated(modelStarted, modelAllocated);
            if (samples.Count == frames) app.RequestStop();
        }));
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await app.RunAsync(timeout.Token);
            if (app.Failure is not null) throw app.Failure;
            if (samples.Count != frames) throw new InvalidOperationException($"Expected {frames} completed frames, got {samples.Count}.");
            string json = JsonSerializer.Serialize(new { Kind = "application", Label = label, Count = count, Changing = changing, LayoutChanging = layoutChanging, ChangedElements = changing ? allChanging || layoutChanging ? count : 1 : 0, RequestedHz = 120, Warmup = warmup, Skipped = skipped, Runtime = Environment.Version.ToString(), OS = Environment.OSVersion.ToString(), TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), ServerGc = System.Runtime.GCSettings.IsServerGC, Environment = environment, Samples = samples });
            File.WriteAllText(output, json + Environment.NewLine);
            Console.WriteLine(json);
        }
        finally
        {
            callback.SetValue(null, null);
            phaseCallback?.SetValue(null, null);
        }
    }

    private readonly record struct Sample(int Frame, double CompletionIntervalMilliseconds, long AllocatedBytes, Dictionary<string, double>? Phases, DetailedDiagnostics.Snapshot? Details, long Timestamp);
}
