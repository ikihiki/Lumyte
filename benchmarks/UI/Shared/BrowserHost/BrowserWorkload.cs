using System.Runtime.InteropServices.JavaScript;
using Lumyte.Mathematics.Colors;
using Lumyte.Signals;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Browser;
using Lumyte.UI.Automation;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.BrowserBenchmarkHost;

/// <summary>Real retained application workload; the bridge contains no timing engine.</summary>
public static partial class BrowserWorkload
{
    private static readonly PresentedFrameListener _frames = new();
    private static readonly Signal<SRgba<float>> _local = Signal.Create(new SRgba<float>(1, 0, 0, 1));
    private static readonly Signal<SRgba<float>> _full = Signal.Create(new SRgba<float>(1, 0, 0, 1));
    private static UiApplication? _application;
    private static UiApplicationWindowContext? _context;
    private static bool _alternate;
    private static bool _busy;
    private static string _profile = "Local100";

    /// <summary>Gets readiness without using an admission barrier as a presentation fence.</summary>
    /// <returns>Whether the owner is running.</returns>
    [JSExport]
    public static bool IsReady() => _application?.State == UiApplicationState.Running;

    /// <summary>Gets public lifecycle failure text for bounded setup diagnostics.</summary>
    /// <returns>The current public failure.</returns>
    [JSExport]
    public static string GetFailure() => _application?.Failure?.ToString() ?? string.Empty;

    /// <summary>Changes model state on its owner, then awaits a new matched successful presented frame.</summary>
    /// <returns>The actual completed frame identity.</returns>
    [JSExport]
    public static async Task<double> AdvanceAsync()
    {
        if (_busy)
        {
            throw new InvalidOperationException("Concurrent operations are not supported.");
        }

        _busy = true;
        try
        {
            Task<long>? completed = null;
            await _application!.Dispatcher.InvokeAsync(() =>
            {
                _alternate = !_alternate;
                SRgba<float> color = _alternate ? new(0, 1, 0, 1) : new(1, 0, 0, 1);
                _local.Value = color;
                if (_profile == "Full1000")
                {
                    _full.Value = color;
                }

                // Armed in the same owner callback as the update: any previous
                // accepted/in-flight frame is below the listener's admission floor.
                completed = _frames.Arm();
            });
            try
            {
                return await completed!.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException error)
            {
                throw new InvalidOperationException($"Frame completion timed out: {_frames.Diagnostic}; {_application.Failure}", error);
            }
        }
        finally
        {
            _frames.Disarm();
            _busy = false;
        }
    }

    /// <summary>Captures GPU pixels only for untimed correctness verification.</summary>
    /// <returns>The application's real capture PNG.</returns>
    [JSExport]
    public static async Task<string> CaptureAsync()
    {
        var page = new UiAutomationPage(_context!.Automation, new() { Timeout = TimeSpan.FromSeconds(30) });
        return Convert.ToBase64String(await page.ScreenshotAsync());
    }

    /// <summary>Stops through the public application lifecycle.</summary>
    /// <returns>Ordered shutdown.</returns>
    [JSExport]
    public static async Task StopAsync() => await _application!.StopAsync();

    internal static async Task RunAsync()
    {
        using JSObject location = JSHost.GlobalThis.GetPropertyAsJSObject("location")!;
        _profile = (location.GetPropertyAsString("search") ?? string.Empty).TrimStart('?');
        if (_profile is not ("Local100" or "Full1000"))
        {
            throw new ArgumentException("Unknown browser benchmark profile.");
        }

        int elements = _profile == "Full1000" ? 1000 : 100;
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseBrowser(new() { CanvasId = "lumyte" });
        builder.UseRoot(context =>
        {
            _context = context;
            Definitions.Stack root = Stack();
            var rows = new List<Definitions.Widget>();
            for (int row = 0; row < elements / 20; row++)
            {
                Definitions.Stack line = Stack(orientation: UiOrientation.Horizontal);
                line.Children.Initialize(Enumerable.Range(0, 20).Select(column => Rectangle(
                    width: 20,
                    height: 10,
                    fill: row == 0 && column == 0 ? _local : _full)).ToArray());
                rows.Add(line);
            }

            root.Children.Initialize(rows.ToArray());
            return root;
        });
        await using UiApplication application = builder.Build();
        _application = application;
        await application.RunAsync();
    }
}
