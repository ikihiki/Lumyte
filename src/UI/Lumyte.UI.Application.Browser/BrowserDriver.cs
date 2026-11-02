using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Browser;
using Lumyte.Platform.Abstractions;
using Lumyte.Platform.Browser;
using Lumyte.Platform.Browser.TextInput;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.UI.Application.Browser;

internal sealed class BrowserDriver(UiBrowserApplicationOptions options) : UiPlatformApplicationDriver
{
    private JSObject? _canvas;
    private IDisposable? _bridge;
    private bool _imported;

    internal override bool CurrentThread => true;

    public override async ValueTask DisposeAsync()
    {
        _bridge?.Dispose();
        _bridge = null;
        await base.DisposeAsync();
        _canvas?.Dispose();
        _canvas = null;
    }

    internal override ValueTask NextTurnAsync(TimeSpan interval) => _imported ? new(BrowserApplicationInterop.NextTurnAsync(Math.Min(interval.TotalMilliseconds, 50))) : base.NextTurnAsync(TimeSpan.FromMilliseconds(10));

    protected override async ValueTask<(IPlatform Platform, ITextInputService? Text)> CreatePlatformAsync(bool textInput, CancellationToken cancellationToken)
    {
        using JSObject location = JSHost.GlobalThis.GetPropertyAsJSObject("location") ?? throw new UiApplicationException("platform-unavailable");
        string href = location.GetPropertyAsString("href") ?? throw new UiApplicationException("platform-unavailable");
        await JSHost.ImportAsync("Lumyte.UI.Application", new Uri(new Uri(href), options.ModulePath).AbsoluteUri, cancellationToken);
        _imported = true;
        _canvas = BrowserApplicationInterop.Canvas(options.CanvasId);
        BrowserPlatform platform = await BrowserPlatform.CreateAsync(_canvas);
        try
        {
            ITextInputService? text = null;
            return (platform, text);
        }
        catch
        {
            platform.Dispose();
            throw;
        }
    }

    protected override async ValueTask<ITextInputService?> CreateTextServiceAsync(IPlatform platform, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await BrowserTextInputService.CreateAsync((BrowserPlatform)platform);
    }

    protected override IGraphicsSurface CreateGraphicsSurface(IGraphicDevice device, IWindow window) => ((BrowserDevice)device).CreateSurface(((BrowserWindow)window).CanvasContext);

    protected override bool IsTargetVisible(IWindow window) => _canvas is not null && BrowserApplicationInterop.Visible(_canvas);

    protected override bool IsTargetConnected(IWindow window) => _canvas is not null && BrowserApplicationInterop.Connected(_canvas);

    protected override void ConfigureTextBridge()
    {
        if (TextInput is BrowserTextInputService text)
        {
            _bridge = text.RegisterFocusBridge(PumpInput, () => SynchronizeFocus?.Invoke(), () => !Application.IsTurning && !Application.HasInputLease && Application.State != UiApplicationState.Recovering);
        }
    }
}
