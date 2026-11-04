using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Platform.Abstractions;
using Lumyte.Windowing;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.UI.Windowing.WindowSystem;

internal sealed class NativeBackend : IUiNativeWindowBackend, IDisposable
{
    private readonly Lumyte.Windowing.WindowSystem _windows;

    private readonly IWindowInputSource _input;

    private readonly IUiNativePresentationFactory _presentations;

    private readonly UiNativeWindowHostCapabilities _capabilities;

    private readonly Dictionary<WindowNodeRequestId, TaskCompletionSource> _pending = [];

    internal NativeBackend(Lumyte.Windowing.WindowSystem windows, IWindowInputSource input, IUiNativePresentationFactory presentations, UiNativeWindowHostCapabilities capabilities)
    {
        _windows = windows;
        _input = input;
        _presentations = presentations;
        _capabilities = capabilities;
        windows.RequestCompleted += Completed;
    }

    public UiNativeWindowSupport GetSupport(IUiWindowSource source)
    {
        if (UiWindowSourceProjection.Unwrap(source) is not WindowSystemUiWindowSource native)
        {
            return new(false, "native-features-unavailable");
        }

        const WindowFeatures Required = WindowFeatures.OwnedWindow | WindowFeatures.NonActivating | WindowFeatures.AnchorPlacement;
        if ((native.Window.State.Features & Required) != Required)
        {
            return new(false, "native-features-unavailable");
        }

        if (!_input.TryGetInputWindowId(native.Window.Id, out _))
        {
            return new(false, "origin-input-unavailable");
        }

        if (!_capabilities.CanCancelCrossWindowPointers || !_capabilities.CanObserveOutsidePress)
        {
            return new(false, "native-observation-unavailable");
        }

        return new(true, string.Empty);
    }

    public Vector2 GetLimit(IUiWindowSource source, Rect2D anchor) => _presentations.GetClientLimit(((WindowSystemUiWindowSource)UiWindowSourceProjection.Unwrap(source)).Window, anchor);

    public IUiNativeWindow Create(IUiWindowSource owner, UiWindowCreateRequest request)
    {
        var source = (WindowSystemUiWindowSource)UiWindowSourceProjection.Unwrap(owner);
        WindowRole role = request.Role switch
        {
            UiWindowRole.ContextMenu => WindowRole.ContextMenu,
            UiWindowRole.ImeCandidates => WindowRole.ImeCandidates,
            _ => WindowRole.Popup,
        };
        WindowNode node;
        try
        {
            node = _windows.CreateNativeWindow(new() { Owner = source.Window, ClientSize = request.ClientSize, Kind = WindowKind.Popup, Activation = WindowActivation.PreserveFocus, IsResizable = false, }, role, source.Node);
        }
        catch (Exception error)
        {
            throw new UiWindowOperationException("window-create-failed", error);
        }

        var child = new WindowSystemUiWindowSource(node, _input, _presentations, source.CaptureState().KeyboardScopeId);
        return new Native(this, node, owner is IUiWindowSourceAdapter adapter ? adapter.AdaptSource(child) : child);
    }

    public void Dispose() => _windows.RequestCompleted -= Completed;

    private ValueTask Wait(WindowNodeRequestId request)
    {
        var completion = new TaskCompletionSource();
        _pending.Add(request, completion);
        return new(completion.Task);
    }

    private void Completed(WindowNodeRequestResult result)
    {
        if (_pending.Remove(result.Request, out TaskCompletionSource? task))
        {
            if (result.Status == WindowRequestStatus.Applied)
            {
                task.SetResult();
            }
            else
            {
                task.SetException(new UiWindowOperationException("native-open-failed"));
            }
        }
    }

    private sealed class Native(NativeBackend backend, WindowNode node, IUiWindowSource source) : IUiNativeWindow
    {
        public IUiWindowSource Source { get; } = source;

        public ValueTask PlaceAsync(Rect2D anchor, UiWindowPlacementSide side, float gap)
        {
            WindowPlacementSide direction = side switch
            {
                UiWindowPlacementSide.Below => WindowPlacementSide.Below,
                UiWindowPlacementSide.Above => WindowPlacementSide.Above,
                UiWindowPlacementSide.Right => WindowPlacementSide.Right,
                _ => WindowPlacementSide.Left,
            };
            return backend.Wait(node.Place(new(new(anchor.X, anchor.Y, anchor.Width, anchor.Height), direction, gap)));
        }

        public ValueTask ShowAsync() => backend.Wait(node.Show());

        public ValueTask CloseAsync() => backend.Wait(node.Close());
    }
}
