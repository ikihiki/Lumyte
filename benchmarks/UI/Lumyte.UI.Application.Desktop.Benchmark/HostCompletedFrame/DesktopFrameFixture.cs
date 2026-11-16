using Lumyte.Mathematics.Colors;
using Lumyte.Signals;
using Lumyte.UI.Automation;
using Lumyte.UI.Windowing;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Desktop.Benchmark.HostCompletedFrame;

/// <summary>Actual standard desktop wgpu host with one serial presentation window and real system pacing.</summary>
public sealed class DesktopFrameFixture : IAsyncDisposable
{
    private readonly FrameAcknowledgment _acknowledgment = new();
    private readonly TaskCompletionSource<UiApplicationWindowContext> _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Signal<SRgba<float>>[] _colors;
    private readonly bool[] _green;
    private readonly Signal<float?>[] _widths;
    private readonly UiApplication _application;
    private bool _narrow;
    private UiApplicationWindowContext? _context;
    private Task? _running;

    /// <summary>Initializes a new instance of the <see cref="DesktopFrameFixture"/> class without initializing graphics.</summary>
    /// <param name="itemCount">Exactly 100 or 1000 independently reactive visible rectangles.</param>
    public DesktopFrameFixture(int itemCount)
    {
        if (itemCount is not (100 or 1000))
        {
            throw new ArgumentOutOfRangeException(nameof(itemCount));
        }

        _colors = Enumerable.Range(0, itemCount).Select(_ => Signal.Create(new SRgba<float>(1, 0, 0, 1))).ToArray();
        _green = new bool[itemCount];
        _widths = Enumerable.Range(0, itemCount).Select(_ => Signal.Create<float?>(8)).ToArray();
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseDesktop(new() { Title = "Owned UI benchmark", ClientSize = new(320, 400) });

        // Deliberately keep system time and the product's default 60 Hz maximum interval.
        // This is paced integrated latency, not CPU recording cost or physical scanout timing.
        builder.UseWindows(_ => [Compose.Window(
            "main",
            "Owned UI benchmark",
            initialClientSize: new(320, 400),
            root: context =>
        {
            _context = context;
            Definitions.Stack root = Stack();
            var rows = new List<Definitions.Widget>();
            for (int offset = 0; offset < itemCount; offset += 25)
            {
                Definitions.Stack row = Stack(orientation: UiOrientation.Horizontal, height: 8);
                row.Children.Initialize(Enumerable.Range(offset, Math.Min(25, itemCount - offset)).Select(index => Rectangle(width: _widths[index], height: 8, fill: _colors[index])).ToArray());
                rows.Add(row);
            }

            root.Children.Initialize(rows.ToArray());
            return root;
        },
            onOpened: _ => _opened.TrySetResult(_context!))]);
        _application = builder.Build();
    }

    /// <summary>Gets the application lifecycle for functional verification.</summary>
    public UiApplicationState State => _application.State;

    /// <summary>Gets the original standard-host execution failure independently of subsequent cleanup errors.</summary>
    public Exception? Failure => _application.Failure;

    /// <summary>Gets the actual host-selected native AdapterInfo line after successful hardware audit, never a separate probe.</summary>
    public string? AdapterIdentity { get; private set; }

    /// <summary>Gets the actual scale-one client width, independently captured from the public window snapshot.</summary>
    public int PixelWidth { get; private set; }

    /// <summary>Gets the actual scale-one client height, independently captured from the public window snapshot.</summary>
    public int PixelHeight { get; private set; }

    /// <summary>Gets a missing explicit hardware/owned-display precondition, without native initialization.</summary>
    /// <returns>Null when explicit opt-in and owned-display prerequisites are supplied; native adapter validation occurs at startup.</returns>
    public static string? UnavailableReason()
    {
        if (Environment.GetEnvironmentVariable("LUMYTE_UI_BENCHMARK_GPU") != "1")
        {
            return "Explicit LUMYTE_UI_BENCHMARK_GPU=1 opt-in is required.";
        }

        string? display = Environment.GetEnvironmentVariable("DISPLAY");
        if (string.IsNullOrWhiteSpace(display) || display != Environment.GetEnvironmentVariable("LUMYTE_UI_BENCHMARK_OWNED_DISPLAY"))
        {
            return "DISPLAY must match LUMYTE_UI_BENCHMARK_OWNED_DISPLAY from the owned compositor launcher.";
        }

        return null;
    }

    /// <summary>Starts the ordinary host and verifies a newly accepted complete frame.</summary>
    /// <returns>The untimed startup and first completion.</returns>
    public async Task StartAsync()
    {
        if (UnavailableReason() is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        using var audit = new NativeAdapterAudit();
        Console.WriteLine($"Desktop boundary: standard UseDesktop/wgpu; serial window; default system pacing; display={Environment.GetEnvironmentVariable("DISPLAY")}; virtual Present is not physical scanout.");
        _running = _application.RunAsync();
        Task winner = await Task.WhenAny(_opened.Task, _running).WaitAsync(TimeSpan.FromSeconds(60));
        if (winner == _running)
        {
            await _running;
            throw new InvalidOperationException("Host stopped before opening.");
        }

        _context = await _opened.Task;
        AdapterIdentity = audit.VerifyHardware();
        Console.WriteLine(AdapterIdentity);
        audit.Dispose();

        // X11 Show requests are asynchronous. Readiness is untimed and is NOT used as the frame fence.
        using var visibleTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await _application.Dispatcher.InvokeAsync(() => _application.State == UiApplicationState.Running && _context.Windows.TryGetSnapshot(_context.Window, out UiWindowSnapshot? snapshot) && snapshot is { IsVisible: true }, visibleTimeout.Token))
        {
            await Task.Delay(5, visibleTimeout.Token);
        }

        await _application.Dispatcher.InvokeAsync(() =>
        {
            if (!_context.Windows.TryGetSnapshot(_context.Window, out UiWindowSnapshot? snapshot) || snapshot is null || snapshot.Scale != System.Numerics.Vector2.One)
            {
                throw new NotSupportedException("This analytic fixture requires an actual scale-one window snapshot.");
            }

            PixelWidth = checked((int)snapshot.ClientSize.X);
            PixelHeight = checked((int)snapshot.ClientSize.Y);
            if (PixelWidth < 320 || PixelHeight < 400 || PixelWidth > 4096 || PixelHeight > 4096)
            {
                throw new NotSupportedException("Actual window dimensions are outside the bounded fixture range.");
            }
        });
        Console.WriteLine($"Actual presentation target: {PixelWidth}x{PixelHeight}; requested client: 320x400; scale=1.");
        await CompleteAsync(0);
        await VerifyPixelsAsync();
    }

    /// <summary>Mutates on the owner, then awaits the same newly accepted frame's successful GPU/surface completion.</summary>
    /// <param name="dirtyCount">Zero, one, or all rectangle colors to toggle.</param>
    /// <param name="localLayout">Whether to toggle the first rectangle width and propagate actual row layout.</param>
    /// <returns>The actual completed frame ID; queueing, system pacing, GPU completion and surface release are included.</returns>
    public async Task<long> CompleteAsync(int dirtyCount, bool localLayout = false)
    {
        if (dirtyCount < 0 || dirtyCount > _colors.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(dirtyCount));
        }

        Task<long> completion = await _application.Dispatcher.InvokeAsync(() =>
        {
            for (int index = 0; index < dirtyCount; index++)
            {
                _green[index] = !_green[index];
                _colors[index].Value = _green[index] ? new(0, 1, 0, 1) : new(1, 0, 0, 1);
            }

            if (localLayout)
            {
                _narrow = !_narrow;
                _widths[0].Value = _narrow ? 6 : 8;
            }

            // Owner callback runs before slot Advance/UpdateLayout and RecordAndDrawFrame.
            // Arming here excludes all FrameAccepted events belonging to a previous in-flight frame.
            return _acknowledgment.Arm();
        });
        return await completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>Captures a fresh actual composed GPU frame and encodes PNG; intended only for screenshot diagnostics.</summary>
    /// <returns>The actual encoded presentation pixels.</returns>
    public Task<byte[]> CaptureAsync()
    {
        var page = new UiAutomationPage(_context!.Automation, new() { Timeout = TimeSpan.FromSeconds(30) });
        return page.ScreenshotAsync();
    }

    /// <summary>Untimed analytic pixel check through the actual composed presentation readback, including unchanged/background probes.</summary>
    /// <returns>The asynchronous correctness validation; never call inside a benchmark body.</returns>
    public async Task VerifyPixelsAsync()
    {
        var page = new UiAutomationPage(_context!.Automation, new() { Timeout = TimeSpan.FromSeconds(30) });
        byte[] png = await page.ScreenshotAsync();
        (bool[] expected, bool narrow) = await _application.Dispatcher.InvokeAsync(() => (_green.ToArray(), _narrow));
        PresentationPixels.Verify(png, expected, narrow, PixelWidth, PixelHeight);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _application.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));
            if (_running is not null)
            {
                await _running;
            }

            await _application.DisposeAsync();
        }
        finally
        {
            _acknowledgment.Dispose();
        }
    }
}
