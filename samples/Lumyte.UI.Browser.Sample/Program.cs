using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Browser;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Input;
using Lumyte.Platform.Abstractions;
using Lumyte.Platform.Browser;
using Lumyte.UI.Automation;
using Lumyte.UI.SampleSupport;
using Lumyte.UI.Windowing;
using Lumyte.Windowing;
using static Lumyte.UI.Compose;

[assembly: SupportedOSPlatform("browser")]

namespace Lumyte.UI.Browser.Sample;

internal static partial class Program
{
    private static readonly Queue<InputRecord> _pending = [];

    private static readonly TaskCompletionSource _stopped = new();

    private static readonly TaskCompletionSource _initialized = new();
    private static BrowserPlatform? _platform;
    private static WindowSystem? _windows;
    private static InputSystem? _input;
    private static IWindowInputSource? _source;
    private static UiHostInputRouter? _router;
    private static UiHostPresenter? _presenter;
    private static BrowserDevice? _device;
    private static IGraphicsSurface? _graphics;
    private static IGraphicsSwapchain? _swapchain;
    private static IDisposable? _retained;
    private static WindowNode? _root;
    private static WindowNode? _hosted;
    private static UiSurface? _mainUi;
    private static UiPopupExercise? _popupExercise;
    private static UiSurface? _hostedUi;
    private static UiSampleResources? _resources;
    private static bool _graph;
    private static bool _drawing;
    private static bool _ready;
    private static bool _inputOnly;
    private static bool _automation;
    private static bool _diagnosticMode;
    private static BrowserUiDiagnostics? _diagnostics;
    private static int _clicks;
    private static int _frames;
    private static Definitions.Button? _mainButton;
    private static Definitions.Button? _hostedButton;

    [JSExport]
    internal static async Task RunAutomationAsync()
    {
        try
        {
            await RunAutomationCoreAsync();
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine(failure.ToString());
            throw;
        }
    }

    [JSExport]
    internal static void OpenPopup(bool contextMenu) => _popupExercise?.Open(contextMenu);

    [JSExport]
    internal static void OutsidePopupPress() => _popupExercise?.OutsidePress();

    [JSExport]
    internal static string GetPopupState() => $"{_popupExercise?.Ready},{_popupExercise?.Current?.State},{_popupExercise?.Clicks},{_popupExercise?.Popups.CapturePopups().Count},{_popupExercise?.Popups.CapturePopups().Count(p => p.State == UiPopupState.Open)}";

    [JSExport]
    internal static string GetState() => $"{_clicks},{_frames},{_mainButton?.IsPressed},{_hostedButton?.IsPressed}";

    [JSExport]
    internal static async Task FrameAsync()
    {
        if (_drawing || !_ready || _platform is null)
        {
            return;
        }

        _drawing = true;
        try
        {
            _platform.PumpEvents();
            _windows!.Update();
            _mainUi!.SetViewport(_root!.ClientSize);
            _mainUi.UpdateLayout();
            _hostedUi!.SetViewport(_hosted!.ClientSize);
            _hostedUi.UpdateLayout();
            _diagnostics?.Pump();
            _popupExercise?.Advance();
            _input!.Update();
            while (_pending.TryDequeue(out InputRecord input))
            {
                if (!_automation && !_diagnosticMode)
                {
                    _router!.Dispatch(input);
                }
            }

            if (!UiHostDrawing.Drawable(_root.NativeWindow.State))
            {
                return;
            }

            DrawingCanvas2D canvas = _graph ? new RenderGraphCanvas2D() : new Canvas2D();
            if (canvas is RenderGraphCanvas2D graphCanvas)
            {
                graphCanvas.Begin();
            }
            else
            {
                ((Canvas2D)canvas).Begin();
            }

            if (_popupExercise is not null)
            {
                _popupExercise.Source.Record(canvas);
            }
            else
            {
                UiHostDrawing.Record(canvas, _root, _mainUi);
            }

            foreach (WindowNode node in _popupExercise is null ? _root.GetHostedOrder() : Array.Empty<WindowNode>())
            {
                if (node == _hosted)
                {
                    UiHostDrawing.Record(canvas, node, _hostedUi);
                }
            }

            if (_inputOnly)
            {
                _ = ((Canvas2D)canvas).End();
                _popupExercise?.Source.CompleteFrame();
                _frames++;
                return;
            }

            SwapchainDesc configuration = UiHostPresenter.Configuration(_graphics!, _root.PixelSize.Width, _root.PixelSize.Height);
            if (_swapchain is null)
            {
                _swapchain = _graphics!.CreateSwapchain(configuration);
            }
            else if (_swapchain.Configuration != configuration)
            {
                _swapchain.Reconfigure(configuration);
            }

            if (canvas is RenderGraphCanvas2D graph)
            {
                await _presenter!.DrawGraphAsync(_swapchain, graph.End(), _resources?.Reference);
            }
            else
            {
                await _presenter!.DrawAsync(_swapchain, ((Canvas2D)canvas).End());
            }

            _popupExercise?.Source.CompleteFrame();
            _frames++;
        }
        finally
        {
            _popupExercise?.Source.CompleteFrame(false);
            _drawing = false;
        }
    }

    [JSExport]
    internal static async Task StopAsync()
    {
        if (_drawing)
        {
            throw new InvalidOperationException("Wait for the current frame before stopping.");
        }

        _ready = false;
        if (_diagnostics is not null)
        {
            await _diagnostics.DisposeAsync();
        }

        _router!.Detach(_hostedUi!);
        _router.Detach(_mainUi!);
        _input!.StopSource(_source!);
        if (_popupExercise is not null)
        {
            await _popupExercise.DisposeAsync();
            _popupExercise = null;
        }

        _input.Dispose();
        _source!.Dispose();
        _swapchain?.Dispose();
        _presenter?.Dispose();
        if (_resources is not null)
        {
            await _resources.DisposeAsync();
        }

        _graphics?.Dispose();
        _device?.Dispose();
        _retained!.Dispose();
        _hostedUi!.Dispose();
        _mainUi!.Dispose();
        _windows!.Dispose();
        _platform!.Dispose();
        _platform = null;
        _ready = false;
        _stopped.SetResult();
    }

    private static async Task RunAutomationCoreAsync()
    {
        await _initialized.Task;
        await using var host = new UiSampleAutomationHost(_mainUi!, async token =>
        {
            token.ThrowIfCancellationRequested();
            await FrameAsync();
        });
        if (_inputOnly)
        {
            await UiAutomationExercise.RunAsync(host);
        }
        else
        {
            var screenshot = new UiSampleScreenshotHost(host, token => UiSampleScreenshot.CaptureAsync(_device!, _mainUi!, _root!.Scale, token));
            await UiAutomationExercise.RunAsync(screenshot);
            var page = new UiAutomationPage(screenshot, new() { Timeout = TimeSpan.FromSeconds(30) });
            JSHost.GlobalThis.SetProperty("lumyteAutomationPng", Convert.ToBase64String(await page.ScreenshotAsync()));
        }

        _automation = false;
    }

    private static async Task StartDiagnosticsAsync()
    {
        if (!_diagnosticMode)
        {
            return;
        }

        _mainUi!.SetViewport(_root!.ClientSize);
        _mainUi.UpdateLayout();
        Func<CancellationToken, ValueTask<byte[]>>? capture = _inputOnly ? null : token => UiSampleScreenshot.CaptureAsync(_device!, _mainUi, _root.Scale, token);
        _diagnostics = await BrowserUiDiagnostics.CreateAsync(
            _mainUi,
            JSHost.GlobalThis.GetPropertyAsString("lumyteDiagnosticService")!,
            JSHost.GlobalThis.GetPropertyAsString("lumyteDiagnosticGameToken")!,
            capture);
    }

    private static async Task Main()
    {
        using JSObject canvas = JSHost.GlobalThis.GetPropertyAsJSObject("lumyteUiCanvas") ?? throw new InvalidOperationException("The host must supply its canvas.");
        _platform = await BrowserPlatform.CreateAsync(canvas);
        _windows = new(_platform);
        _root = _windows.CreateNativeWindow(new() { ClientSize = new(480, 320), Title = "UI" }, WindowRole.Normal);
        _hosted = _windows.CreateHostedWindow(new() { Parent = _root, Bounds = new(220, 100, 200, 140) });
        _source = (IWindowInputSource)_platform.CreateInputSource();
        _input = new([_source]);
        _router = new(_source, _source);
        var saved = new UiSavedIndicator();
        _mainButton = Button(testId: "save", automationName: "保存", onClick: Save)[Rectangle(width: 160, height: 80, fill: new Color2D(0.2f, 0.5f, 0.9f, 1))];
        _mainUi = new(Panel()[Stack()[_mainButton, saved]]);
        void Save()
        {
            saved.Show();
            Console.WriteLine($"Main clicks: {++_clicks}");
        }

        _hostedButton = Button(onClick: () => Console.WriteLine($"Hosted clicks: {++_clicks}"))[Rectangle(width: 180, height: 120, fill: new Color2D(0.7f, 0.3f, 0.1f, 1))];
        _hostedUi = new(Panel()[_hostedButton]);
        _router.Attach(_root, _mainUi);
        _router.Attach(_hosted, _hostedUi);
        _input.Recorded += _pending.Enqueue;
        _root.Show();
        _hosted.Show();
        _platform.PumpEvents();
        _windows.Update();
        _retained = _root.NativeWindow.RetainNativeTarget();
        _diagnosticMode = JSHost.GlobalThis.GetPropertyAsBoolean("lumyteDiagnostics");
        _automation = JSHost.GlobalThis.GetPropertyAsBoolean("lumyteAutomation");
        _inputOnly = JSHost.GlobalThis.GetPropertyAsBoolean("lumyteInputOnly");
        if (JSHost.GlobalThis.GetPropertyAsBoolean("lumytePopups"))
        {
            if (_automation || _diagnosticMode)
            {
                throw new InvalidOperationException("Choose one sample mode.");
            }

            var scope = Guid.NewGuid();
            _popupExercise = new(
                _mainUi,
                _mainButton,
                () =>
            {
                Lumyte.Windowing.Abstractions.WindowState state = _root.NativeWindow.State;
                return new UiWindowSourceSnapshot(1, _source.TryGetInputWindowId(state.Id, out Lumyte.Input.Abstractions.InputWindowId origin) ? origin : null, state.ClientSize, state.Scale, state.IsVisible, state.IsActive, state.Lifetime == Lumyte.Windowing.Abstractions.WindowLifetime.Closed, scope);
            },
                nativeUnavailableCode: "browser-hosted-only");
            _router = new(_source, _source, _popupExercise.Dispatch);
            _router.Attach(_root, _mainUi);
        }

        if (_inputOnly)
        {
            await StartDiagnosticsAsync();
            _ready = true;
            _initialized.SetResult();
            await _stopped.Task;
            return;
        }

        _device = await BrowserDevice.CreateAsync("./lumyte-graphics.js");
        _graph = JSHost.GlobalThis.GetPropertyAsBoolean("lumyteRenderGraph") || JSHost.GlobalThis.GetPropertyAsBoolean("lumyteResources");
        if (JSHost.GlobalThis.GetPropertyAsBoolean("lumyteResources"))
        {
            _resources = await UiSampleResources.CreateAsync(_device);
            Console.WriteLine("UI Resources initialized with cooperative CPU execution.");
        }

        _graphics = _device.CreateSurface(((BrowserWindow)_root.NativeWindow).CanvasContext);
        _presenter = new(_device);
        await StartDiagnosticsAsync();
        _ready = true;
        _initialized.SetResult();
        await _stopped.Task;
    }
}
