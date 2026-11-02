using Lumyte.UI.Windowing;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.UI.Application;

internal abstract class UiDriverWindow : IAsyncDisposable
{
    private readonly UiHostedDrawingSource _source;

    protected UiDriverWindow(string key)
    {
        Key = key;
        _source = new(CaptureState);
    }

    internal string Key { get; }

    internal UiHostedDrawingSource Source => _source;

    internal virtual IWindow? NativeWindow => null;

    internal virtual IUiWindowSource PresentationSource => Source;

    public abstract ValueTask DisposeAsync();

    internal abstract UiWindowSourceSnapshot CaptureState();

    internal abstract void Show();

    internal abstract void SetTitle(string title);
}
