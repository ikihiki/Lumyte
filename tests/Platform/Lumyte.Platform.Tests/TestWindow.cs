using System.Numerics;

namespace Lumyte.Platform.Tests;

internal sealed class TestWindow : IWindow
{
    private readonly TestPlatform _platform;
    private WindowState _state;
    private ulong _request;
    private int _retained;
    private WindowRequestId? _close;

    internal TestWindow(TestPlatform platform, WindowId id, WindowDescriptor descriptor)
    {
        _platform = platform;
        Id = id;
        _state = new WindowState
        {
            Id = id,
            Owner = descriptor.Owner?.Id,
            Kind = descriptor.Kind,
            Activation = descriptor.Activation,
            Title = descriptor.Title,
            ClientSize = descriptor.ClientSize,
            PixelSize = new WindowPixelSize((int)descriptor.ClientSize.X, (int)descriptor.ClientSize.Y),
            Scale = Vector2.One,
            IsVisible = false,
            IsActive = false,
            DisplayMode = WindowDisplayMode.Normal,
            Lifetime = WindowLifetime.Open,
            Features = WindowFeatures.OwnedWindow | WindowFeatures.NonActivating,
        };
    }

    public event Action<WindowState>? StateChanged;

    public event Action<WindowRequestResult>? RequestCompleted;

    public event EventHandler<WindowClosingEventArgs>? Closing;

    public WindowId Id { get; }

    public WindowState State => _state;

    internal bool Disposed { get; private set; }

    internal bool FailDispose { get; set; }

    public WindowRequestId Show() => Request(() => Change(_state with { IsVisible = true }));

    public WindowRequestId Hide() => Request(() => Change(_state with { IsVisible = false }));

    public WindowRequestId SetTitle(string title) => Request(() => Change(_state with { Title = title }));

    public WindowRequestId Resize(Vector2 clientSize) => Request(() => Change(_state with { ClientSize = clientSize, PixelSize = new WindowPixelSize((int)clientSize.X, (int)clientSize.Y) }));

    public WindowRequestId Place(WindowPlacement placement) => Request(() => { });

    public WindowRequestId Activate() => Request(() => Change(_state with { IsActive = true }));

    public WindowRequestId BringToFront() => Request(() => { });

    public WindowRequestId SetDisplayMode(WindowDisplayMode mode) => Request(() => Change(_state with { DisplayMode = mode }));

    public WindowRequestId Close()
    {
        var id = new WindowRequestId(++_request);
        _platform.Pending.Enqueue(() =>
        {
            if (_platform.Windows.Any(w => w.State.Owner == Id && w.State.Lifetime != WindowLifetime.Closed))
            {
                RequestCompleted?.Invoke(new WindowRequestResult(Id, id, WindowRequestStatus.Denied, "children"));
                return;
            }

            _close = id;
            Change(_state with { Lifetime = WindowLifetime.Closing });
            Advance();
        });
        return id;
    }

    public IDisposable RetainNativeTarget()
    {
        _retained++;
        return new Retention(this);
    }

    public void Dispose()
    {
        if (_retained > 0 || FailDispose)
        {
            throw new InvalidOperationException("retained or failed");
        }

        Disposed = true;
    }

    internal void UserClose()
    {
        var args = new WindowClosingEventArgs(Id);
        Closing?.Invoke(this, args);
        if (!args.Cancel)
        {
            Close();
        }
    }

    internal void Change(WindowState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }

    internal void Advance()
    {
        if (_close is { } request && _retained == 0)
        {
            _close = null;
            Change(_state with { Lifetime = WindowLifetime.Closed, IsVisible = false, IsActive = false });
            RequestCompleted?.Invoke(new WindowRequestResult(Id, request, WindowRequestStatus.Applied, null));
        }
    }

    private WindowRequestId Request(Action action)
    {
        var id = new WindowRequestId(++_request);
        _platform.Pending.Enqueue(() =>
        {
            action();
            RequestCompleted?.Invoke(new WindowRequestResult(Id, id, WindowRequestStatus.Applied, null));
        });
        return id;
    }

    private sealed class Retention(TestWindow window) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                window._retained--;
                _disposed = true;
            }
        }
    }
}
