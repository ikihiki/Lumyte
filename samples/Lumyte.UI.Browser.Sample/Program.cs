using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Browser;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input;
using Lumyte.Platform.Abstractions;
using Lumyte.Platform.Browser;
using Lumyte.UI.SampleSupport;
using Lumyte.Windowing;
using static Lumyte.UI.Compose;

[assembly: SupportedOSPlatform("browser")]

namespace Lumyte.UI.Browser.Sample;

internal static partial class Program
{
    private static readonly Queue<InputRecord> _pending = [];
    private static readonly TaskCompletionSource _stopped = new();
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
    private static UiSurface? _hostedUi;
    private static bool _drawing;
    private static bool _ready;
    private static bool _inputOnly;
    private static int _clicks;
    private static int _frames;
    private static ButtonElement? _mainButton;
    private static ButtonElement? _hostedButton;

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
            _input!.Update();
            while (_pending.TryDequeue(out InputRecord input))
            {
                _router!.Dispatch(input);
            }

            if (!UiHostDrawing.Drawable(_root.NativeWindow.State))
            {
                return;
            }

            var canvas = new Canvas2D();
            canvas.Begin();
            UiHostDrawing.Record(canvas, _root, _mainUi);
            foreach (WindowNode node in _root.GetHostedOrder())
            {
                if (node == _hosted)
                {
                    UiHostDrawing.Record(canvas, node, _hostedUi);
                }
            }

            if (_inputOnly)
            {
                _ = canvas.End();
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

            await _presenter!.DrawAsync(_swapchain, canvas.End());
            _frames++;
        }
        finally
        {
            _drawing = false;
        }
    }

    [JSExport]
    internal static void Stop()
    {
        if (_drawing)
        {
            throw new InvalidOperationException("Wait for the current frame before stopping.");
        }

        _router!.Detach(_hostedUi!);
        _router.Detach(_mainUi!);
        _input!.StopSource(_source!);
        _input.Dispose();
        _source!.Dispose();
        _swapchain?.Dispose();
        _presenter?.Dispose();
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
        Definitions.Button mainButton = Button(onClick: () => Console.WriteLine($"Main clicks: {++_clicks}"))[Rectangle(width: 160, height: 80, fill: new Color2D(0.2f, 0.5f, 0.9f, 1))];
        _mainUi = new(Panel()[mainButton]);
        _mainButton = (ButtonElement)_mainUi.RootElement.Children[0];
        Definitions.Button hostedButton = Button(onClick: () => Console.WriteLine($"Hosted clicks: {++_clicks}"))[Rectangle(width: 180, height: 120, fill: new Color2D(0.7f, 0.3f, 0.1f, 1))];
        _hostedUi = new(Panel()[hostedButton]);
        _hostedButton = (ButtonElement)_hostedUi.RootElement.Children[0];
        _router.Attach(_root, _mainUi);
        _router.Attach(_hosted, _hostedUi);
        _input.Recorded += _pending.Enqueue;
        _root.Show();
        _hosted.Show();
        _platform.PumpEvents();
        _windows.Update();
        _retained = _root.NativeWindow.RetainNativeTarget();
        _inputOnly = JSHost.GlobalThis.GetPropertyAsBoolean("lumyteInputOnly");
        if (_inputOnly)
        {
            _ready = true;
            await _stopped.Task;
            return;
        }

        _device = await BrowserDevice.CreateAsync("./lumyte-graphics.js");
        _graphics = _device.CreateSurface(((BrowserWindow)_root.NativeWindow).CanvasContext);
        _presenter = new(_device);
        _ready = true;
        await _stopped.Task;
    }
}
