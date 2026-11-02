using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Lumyte.Signals;
using Lumyte.TextEditing;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.TextEditing;
using Lumyte.UI.TextEditing.Tests;
using Lumyte.UI.Windowing;
using Lumyte.UI.Windowing.Tests;
using Lumyte.UI.Windowing.TextInput;
using static Lumyte.UI.Compose;

foreach (int length in args.Contains("--signal-only") ? Array.Empty<int>() : new[] { 100, 1000, 5000, 10000 })
{
    Type fixtureType = typeof(ConnectionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!;
    using var fixture = (IDisposable)Activator.CreateInstance(fixtureType, [false])!;
    object Get(string name) => fixtureType.GetProperty(name)!.GetValue(fixture)!;
    var surface = (UiSurface)Get("Surface");
    var editor = (TextBoxElement)Get("Editor");
    var document = (TextDocument)Get("Document");
    var connection = (UiTextInputConnection)Get("Connection");
    Action pump = fixtureType.GetMethod("Pump")!.CreateDelegate<Action>(fixture);
    document.TryReplace(document.CaptureSnapshot().Revision, new(0, 0), new string('a', length), new(length, length), TextEditOrigin.Keyboard);
    surface.TryFocus(editor);
    pump();
    if (connection.Geometry?.DocumentRevision != document.CaptureSnapshot().Revision)
    {
        throw new InvalidOperationException("Missing IME geometry.");
    }

    Measure($"ime-idle-{length}", connection.PublishGeometry, 5);
    UiTextLayout layout = editor.CurrentLayout!;
    int[] boundaries = TextDocument.GraphemeBoundaries(document.CaptureSnapshot().Text);
    Measure(
        $"range-generation-{length}",
        () =>
    {
        for (int index = 0; index + 1 < boundaries.Length; index++)
        {
            if (layout.GetRangeBounds(new(boundaries[index], boundaries[index + 1] - boundaries[index])).Count == 0)
            {
                throw new InvalidOperationException("Missing grapheme bounds.");
            }
        }
    },
        5);
}

foreach (int count in args.Contains("--signal-only") ? Array.Empty<int>() : new[] { 0, 1000, 10000 })
{
    using var native = new HostedUiWindowManager();
    using var hosted = new HostedUiWindowManager();
    using var windows = new FallbackUiWindowManager(native, hosted);
    var source = new TestSource();
    using var root = new UiSurface(Panel());
    UiWindowRegistration registration = windows.Register(source);
    void Pump()
    {
        for (int index = 0; index < 12; index++)
        {
            windows.Advance();
            source.Present();
        }
    }

    Task<UiWindowBinding> bindingTask = windows.BindPresentationAsync(registration.Window, root).AsTask();
    Pump();
    UiWindowBinding binding = bindingTask.GetAwaiter().GetResult();
    UiWindowId child = windows.Create(new(registration.Window, new(80, 60), UiWindowRole.Normal, UiWindowInputMode.Interactive, new(registration.Window, new(0, 0, 0, 0), UiWindowPlacementSide.Below), UiWindowPolicy.HostedOnly));
    using var content = new UiSurface(Button());
    Task<UiWindowBinding> childTask = windows.BindPresentationAsync(child, content).AsTask();
    Pump();
    UiWindowBinding childBinding = childTask.GetAwaiter().GetResult();
    windows.Show(child);
    Pump();
    var requests = new List<UiWindowRequestId>();
    for (int index = 0; index < count; index++)
    {
        requests.Add(windows.Resize(child, new(70 + (index % 2), 50)));
    }

    Pump();
    foreach (UiWindowRequestId request in requests)
    {
        if (!windows.TryGetRequest(request, out UiWindowRequestResult? result) || result?.Status == UiWindowRequestStatus.Pending)
        {
            throw new InvalidOperationException("Unfinished setup operation.");
        }
    }

    Measure($"window-idle-history-{count}", windows.Advance, 100);
    Task childRetired = childBinding.DisposeAsync().AsTask();
    Pump();
    childRetired.GetAwaiter().GetResult();
    windows.Close(child);
    Pump();
    windows.Release(child);
    Task rootRetired = binding.DisposeAsync().AsTask();
    Pump();
    rootRetired.GetAwaiter().GetResult();
    registration.Dispose();
    Pump();
    windows.BeginStop();
    Pump();
}

Definitions.Panel staticRoot = Panel()[Enumerable.Range(0, 10000).Select(_ => Rectangle(width: 1, height: 1)).ToArray()];
await using UiApplication app = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => staticRoot).Build();
Action<Definitions.Widget> validate = typeof(UiApplication).GetMethod("ValidateEditors", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Action<Definitions.Widget>>(app);
Measure("validate-editors-10000", () => validate(staticRoot), 100);

var width = Signal.Create<float?>(10);
Definitions.Panel animatedRoot = Panel()[Enumerable.Range(0, 1000).Select(_ => Rectangle(width: width, height: 1)).ToArray()];
using var animated = new UiSurface(animatedRoot);
animated.SetViewport(new(1920, 1080));
animated.UpdateLayout();
int tick = 0;
Measure(
    "signal-rebuild-1000",
    () =>
{
    width.Value = 20 + (++tick % 10);
    animated.UpdateLayout();
},
    args.Contains("--signal-only") ? 100 : 10);

static void Measure(string name, Action action, int iterations)
{
    for (int index = 0; index < 5; index++)
    {
        action();
    }

    var samples = new List<(double Nanoseconds, double Bytes)>();
    for (int batch = 0; batch < 5; batch++)
    {
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int index = 0; index < iterations; index++)
        {
            action();
        }

        double time = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
        samples.Add((time, (GC.GetAllocatedBytesForCurrentThread() - before) / (double)iterations));
    }

    Console.WriteLine(JsonSerializer.Serialize(new { Name = name, Batches = samples.Select(sample => new { sample.Nanoseconds, sample.Bytes }), MedianNanoseconds = samples.Select(sample => sample.Nanoseconds).Order().ElementAt(2), MedianBytes = samples.Select(sample => sample.Bytes).Order().ElementAt(2) }));
}
