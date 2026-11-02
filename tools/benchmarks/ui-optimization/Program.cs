using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;
using Lumyte.Signals;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Windowing;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

bool smoke = args.Contains("--smoke");
string section = args.FirstOrDefault(arg => arg.StartsWith("--section="))?[10..] ?? "all";
bool Include(string name) => section == "all" || section == name;

if (Include("allocation"))
{
    var declaration = new AllocationWidget();
    var retained = new UiElement[10000];
    int cursor = 0;
    Measure("empty-element-construction", () => retained[cursor++ % retained.Length] = new AllocationElement(declaration),
        () => Check(retained[(cursor - 1) % retained.Length] is AllocationElement, "Element was not retained."), smoke ? 10 : 10000, 10);
}

if (Include("animation"))
{
    foreach (int count in smoke ? new[] { 10 } : new[] { 100, 1000, 5000 })
    {
        foreach (bool running in new[] { false, true })
        {
            var clock = new ManualClock();
            var visible = Signal.Create(true);
            Signal<float?>[] targets = Enumerable.Range(0, count).Select(_ => Signal.Create<float?>(10)).ToArray();
            Widget[] children = targets.Select(target => (Widget)Rectangle(width: Animate(target, Duration.FromSeconds(10), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float)))).ToArray();
            using var surface = new UiSurface(Panel(isVisible: visible)[children]);
            surface.SetAnimationClock(clock);
            surface.SetViewport(new(1920, 1080));
            surface.UpdateLayout();
            foreach (Signal<float?> target in targets) target.Value = 20;
            if (running) surface.AdvanceAnimations();
            visible.Value = false;
            surface.UpdateLayout();
            bool observed = false;
            Measure($"hidden-{(running ? "running" : "pending")}-{count}", () => observed |= running ? surface.HasRunningAnimations : surface.HasPendingAnimationUpdates,
                () => Check(!observed, "Hidden work was reported as visible."), smoke ? 2 : 5, smoke ? 1 : 5);
            visible.Value = true;
            surface.UpdateLayout();
            Check(running ? surface.HasRunningAnimations : surface.HasPendingAnimationUpdates, "Hidden animation state was lost.");
        }
    }

    foreach (bool dense in new[] { false, true })
    {
        int count = smoke ? 10 : 1000;
        var clock = new ManualClock();
        var target = Signal.Create<float?>(10);
        UiAnimated<float?> width = Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear);
        using var surface = new UiSurface(Panel()[Enumerable.Range(0, count).Select(i => Rectangle(width: dense || i == count / 2 ? width : 10, height: 10)).ToArray()]);
        surface.SetAnimationClock(clock);
        surface.SetViewport(new(1920, 1080));
        surface.UpdateLayout();
        int tick = 0;
        Measure($"animation-{(dense ? "dense" : "sparse")}-{count}", () =>
        {
            target.Value = (++tick / 60) % 2 == 0 ? 100 : 10;
            clock.Advance(Duration.FromSeconds(1d / 60));
            surface.AdvanceAnimations();
            surface.UpdateLayout();
        }, () => Check(surface.RootElement.Children.Count == count && surface.RootElement.Children[count / 2].Widget.Width == width.Value, "Animation value was not published."), smoke ? 2 : 120, smoke ? 1 : 120);
    }
}

if (Include("layout"))
{
    foreach (bool grouped in new[] { false, true })
    {
        int count = smoke ? 100 : 10000;
        int groups = smoke ? 10 : 100;
        Signal<float?>[] widths = Enumerable.Range(0, count).Select(_ => Signal.Create<float?>(10)).ToArray();
        Widget[] leaves = widths.Select(width => (Widget)Rectangle(width: width, height: 10)).ToArray();
        Widget root = grouped ? Panel()[Enumerable.Range(0, groups).Select(group => Panel()[leaves.Skip(group * (count / groups)).Take(count / groups).ToArray()]).ToArray()] : Panel()[leaves];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(1920, 1080));
        surface.UpdateLayout();
        UiElement initial = surface.RootElement;
        UiElement changed = grouped ? initial.Children[groups / 2].Children[0] : initial.Children[count / 2];
        int tick = 0;
        Measure($"width-{(grouped ? "grouped" : "flat")}-{count}", () =>
        {
            widths[count / 2].Value = 10 + (++tick % 20);
            surface.UpdateLayout();
        }, () => Check(ReferenceEquals(surface.RootElement, initial) && changed.Widget.Width == 10 + (tick % 20), "Local update did not preserve its element."), smoke ? 2 : 100, smoke ? 1 : 10);
    }
}

if (Include("host"))
{
    foreach (int count in smoke ? new[] { 10 } : new[] { 1000, 10000 })
    {
        foreach (string mode in new[] { "static", "color", "custom" })
        {
            var color = Signal.Create(new Color2D(1, 0, 0, 1));
            Widget[] leaves = Enumerable.Range(0, count).Select(i => (Widget)Rectangle(width: 10, height: 10, fill: i == count - 1 ? color : new Color2D(1, 0, 0, 1))).ToArray();
            Definitions.Panel root = mode == "custom" ? new CustomPanel() : Panel();
            root.Children.Initialize(leaves);
            using var fixture = new HostFixture(root);
            int tick = 0;
            fixture.Step();
            DrawList2D reference = fixture.Driver.Last!;
            void Frame()
            {
                if (mode == "color") color.Value = (++tick % 2) == 0 ? new(1, 0, 0, 1) : new(0, 1, 0, 1);
                fixture.Step();
            }

            int startedDraws = fixture.Driver.Draws;
            int startedReuses = fixture.Driver.Reuses;
            int expectedSubmissions = fixture.Driver.Draws;
            Measure($"host-{mode}-{count}", Frame, () =>
            {
                Check(fixture.App.Failure is null, "Host failed.");
                Check(fixture.Driver.Last is not null && fixture.Driver.Last.Commands.Count == count, "Missing frame commands.");
                VerifyCommands(reference, fixture.Driver.Last!, color.Value, count);
                if (mode == "color") VerifyCommands(reference, fixture.Driver.Previous!, (tick % 2) == 0 ? new(0, 1, 0, 1) : new(1, 0, 0, 1), count);
                Check(fixture.Driver.Draws == expectedSubmissions + (smoke ? 2 : 120), "A frame did not reach the driver.");
                expectedSubmissions = fixture.Driver.Draws;
            }, smoke ? 2 : 120, smoke ? 2 : 120,
                () => new { Draws = fixture.Driver.Draws - startedDraws, Reuses = fixture.Driver.Reuses - startedReuses });
        }
    }
}

if (Include("shared"))
{
    foreach (int count in smoke ? new[] { 100 } : new[] { 1000, 10000 })
    {
        var keys = Enumerable.Range(0, 8).Select(i => new UiSharedElementKey($"key-{i}")).ToArray();
        using var surface = new UiSurface(Panel()[Enumerable.Range(0, count).Select(i => Rectangle(width: 10, height: 10, sharedElementKey: i < keys.Length ? keys[i] : null)).ToArray()]);
        surface.SetViewport(new(1920, 1080));
        surface.UpdateLayout();
        using var fixture = new HostFixture(Panel());
        var coordinator = new UiVisualCoordinator(fixture.App, surface, static () => null);
        var flight = new UiPageVisualFlight(fixture.App, coordinator, null, null, static () => false);
        var selected = (HashSet<UiSharedElementKey>)typeof(UiPageVisualFlight).GetField("_sharedKeys", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flight)!;
        selected.UnionWith(keys);
        var resolve = typeof(UiPageVisualFlight).GetMethod("ResolveTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Func<UiSharedElementKey, UiElement?>>(flight);
        var resolveMany = typeof(UiPageVisualFlight).GetMethod("ResolveTargets", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Func<IReadOnlySet<UiElement>>>(flight);
        Measure($"shared-{count}-8", () =>
        {
            for (int round = 0; round < 3; round++) foreach (UiSharedElementKey key in keys) _ = resolve(key);
            _ = resolveMany();
        }, () =>
        {
            for (int index = 0; index < keys.Length; index++) Check(ReferenceEquals(surface.RootElement.Children[index], resolve(keys[index])), "Wrong shared element.");
            Check(resolveMany().SetEquals(surface.RootElement.Children.Take(keys.Length)), "Wrong shared set.");
        }, smoke ? 2 : 100, smoke ? 1 : 10);
    }
}

static void Measure(string name, Action action, Action verify, int iterations, int warmup, Func<object>? details = null)
{
    for (int i = 0; i < warmup; i++) action();
    verify();
    var batches = new List<object>();
    var times = new List<double>();
    var bytes = new List<double>();
    for (int batch = 0; batch < 5; batch++)
    {
        GC.Collect();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) action();
        double nanos = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
        double allocation = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)iterations;
        verify();
        batches.Add(new { Nanoseconds = nanos, Bytes = allocation });
        times.Add(nanos);
        bytes.Add(allocation);
    }

    Console.WriteLine(JsonSerializer.Serialize(new { Name = name, Iterations = iterations, Warmup = warmup, Batches = batches, MedianNanoseconds = times.Order().ElementAt(2), MedianBytes = bytes.Order().ElementAt(2), Details = details?.Invoke() }));
}

static void VerifyCommands(DrawList2D reference, DrawList2D actual, Color2D changedColor, int count)
{
    Check(actual.Commands.Count == count, "Wrong command count.");
    for (int index = 0; index < count; index++)
    {
        var before = (FillRectCommand2D)reference.Commands[index];
        var after = (FillRectCommand2D)actual.Commands[index];
        Check(after.Rect == before.Rect && after.State.Transform == before.State.Transform && after.State.Clip.Elements.Count == before.State.Clip.Elements.Count && after.State.Clip.Elements.Zip(before.State.Clip.Elements).All(pair => pair.First.Transform == pair.Second.Transform && pair.First.Rule == pair.Second.Rule && pair.First.Path.Segments.SequenceEqual(pair.Second.Path.Segments)), "Geometry, transform or clip changed.");
        Check(((SolidPaintNode2D)after.Paint.Root).Color == (index == count - 1 ? changedColor : new Color2D(1, 0, 0, 1)), "Stale color was recorded.");
    }
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class CustomPanel : Definitions.Panel { }
sealed class AllocationWidget : Definitions.Widget { }
sealed class AllocationElement(AllocationWidget widget) : UiElement<AllocationWidget>(widget) { }

sealed class HostFixture : IDisposable
{
    private readonly FrameClock _clock = new();
    private readonly Action _turn;

    public HostFixture(Widget root)
    {
        Driver = new RecordingDriver();
        App = UiApplication.CreateBuilder().Configure(new() { TimeProvider = _clock, FrameInterval = TimeSpan.FromMilliseconds(1) }).UseDriver(() => Driver).UseRoot(_ => root).Build();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(UiApplication).GetField("_driver", flags)!.SetValue(App, Driver);
        typeof(UiApplication).GetField("_started", flags)!.SetValue(App, true);
        typeof(UiApplication).GetField("_state", flags)!.SetValue(App, (int)UiApplicationState.Starting);
        _turn = typeof(UiApplication).GetMethod("Turn", flags)!.CreateDelegate<Action>(App);
        typeof(UiApplication).GetMethod("InitializeOwner", flags)!.CreateDelegate<Action>(App)();
        for (int iteration = 0; App.State != UiApplicationState.Running && iteration < 100; iteration++) Step();
        if (App.State != UiApplicationState.Running || App.Failure is not null) throw new InvalidOperationException("Host startup failed.", App.Failure);
    }

    public UiApplication App { get; }
    public RecordingDriver Driver { get; }

    public void Step()
    {
        _clock.Advance();
        _turn();
    }

    public void Dispose()
    {
        App.RequestStop();
        for (int i = 0; !App.Completion.IsCompleted && i < 1000; i++) Step();
        if (!App.Completion.IsCompleted) throw new InvalidOperationException("Host failed to stop.");
        App.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

sealed class FrameClock : TimeProvider
{
    private long _timestamp;
    public override long TimestampFrequency => 1000000;
    public override long GetTimestamp() => _timestamp;
    public void Advance() => _timestamp += 16667;
}

sealed class RecordingDriver : UiApplicationDriver
{
    public int Draws { get; private set; }
    public int Reuses { get; private set; }
    public DrawList2D? Last { get; private set; }
    public DrawList2D? Previous { get; private set; }
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    internal override ValueTask InitializeAsync(UiApplication application, IServiceProvider services, bool textInput, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    internal override ValueTask<UiDriverWindow> CreateWindowAsync(string key, string title, Vector2 size, UiDriverWindow? owner, bool primary, CancellationToken cancellationToken) => ValueTask.FromResult<UiDriverWindow>(new Window(key, size));
    internal override void Pump() { }
    internal override Task<bool> DrawAsync(UiDriverWindow window, DrawList2D commands)
    {
        Draws++;
        if (ReferenceEquals(Last, commands)) Reuses++;
        Previous = Last;
        Last = commands;
        return Task.FromResult(true);
    }

    internal override ValueTask CloseWindowAsync(UiDriverWindow window) => window.DisposeAsync();

    private sealed class Window(string key, Vector2 size) : UiDriverWindow(key)
    {
        private readonly Guid _scope = Guid.NewGuid();
        private readonly InputWindowId _origin = new(Guid.NewGuid());
        private bool _visible;
        private bool _closed;
        private ulong _revision = 1;
        public override ValueTask DisposeAsync() { _closed = true; _revision++; return ValueTask.CompletedTask; }
        internal override UiWindowSourceSnapshot CaptureState() => new(_revision, _origin, size, Vector2.One, _visible, _visible, _closed, _scope);
        internal override void Show() { _visible = true; _revision++; }
        internal override void SetTitle(string title) { }
    }
}
