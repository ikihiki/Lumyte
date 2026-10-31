using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Hosting;
using Lumyte.Input;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Abstractions;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.UI.Windowing;
using Lumyte.UI.Windowing.WindowSystem;
using Lumyte.Windowing;
using Lumyte.Windowing.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.UI.Application;

internal abstract class UiPlatformApplicationDriver : UiApplicationDriver, IUiNativePresentationFactory
{
    private readonly List<Window> _windows = [];
    private readonly Queue<Window> _closing = new();
    private readonly Queue<InputRecord> _records = new();
    private readonly Queue<TextInputKey> _textKeys = new();
    private readonly Dictionary<(InputDeviceId Device, int Kind, ulong Identity), InputWindowId?> _held = [];
    private readonly Dictionary<Guid, IWindow> _sources = [];
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly List<IUiWindowManager> _defaultManagers = [];
    private UiApplication? _application;
    private UiFrameSubmissionQueue _framesInFlight = null!;
    private IPlatform? _platform;
    private IInputSource? _rawInput;
    private InputSystem? _input;
    private ITextInputService? _text;
    private IClipboard? _clipboard;
    private IWindowInputSource? _map;
    private WindowSystem? _system;
    private IGraphicsDeviceService? _graphics;
    private GraphicsDeviceLease? _lease;
    private IGraphicDevice? _device;
    private UiHostPresenter? _presenter;
    private bool _faulted;

    internal override bool SupportsScreenshots => true;

    internal override IWindowInputSource? InputSource => _map;

    internal override ITextInputService? TextInput => _text;

    internal override IClipboard? Clipboard => _clipboard;

    internal override bool IsPhysicalInputIdle => _held.Count == 0;

    protected IPlatform? Platform => _platform;

    protected UiApplication Application => _application!;

    public Vector2 GetClientLimit(IWindow owner, Rect2D anchor) => owner.State.ClientSize;

    public async ValueTask<IUiWindowPresentation> PrepareAsync(IWindow native, UiWindowPresentationView view, UiSurface surface, CancellationToken cancellationToken)
    {
        Window? window = _windows.FirstOrDefault(current => ReferenceEquals(current.NativeWindow, native));
        bool auxiliary = window is null;
        if (window is null)
        {
            window = new(this, "popup:" + view.Window.Value, native, false, null);
            _windows.Add(window);
        }

        IUiWindowPresentation? token = null;
        try
        {
            if (auxiliary)
            {
                await PrepareGraphicsAsync(window, cancellationToken);
            }

            window.Source.ValidateRoot = Application.ValidateEditors;
            _sources[view.SourceId] = native;
            token = await window.Source.PreparePresentationAsync(view, surface, cancellationToken);
            if (auxiliary)
            {
                Application.AttachAuxInput(window, surface);
            }

            return auxiliary ? new AuxiliaryPresentation(this, window, token) : token;
        }
        catch
        {
            if (auxiliary)
            {
                Application.DetachAuxInput(window);
                if (token is not null)
                {
                    await token.DisposeAsync();
                }

                await CloseWindowAsync(window);
            }

            throw;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await RetireResourcesAsync();

        _text?.Dispose();
        _text = null;
        _clipboard?.Dispose();
        _clipboard = null;
        _input?.Dispose();
        _input = null;
        _rawInput?.Dispose();
        _rawInput = null;
        _system?.Dispose();
        _system = null;
        _platform?.Dispose();
        _platform = null;
    }

    internal override async ValueTask RetireResourcesAsync()
    {
        foreach (Window window in _windows.ToArray())
        {
            await CloseWindowAsync(window);
        }

        foreach (IUiWindowManager manager in _defaultManagers.ToArray())
        {
            manager.BeginStop();
            while (!manager.IsStopped)
            {
                manager.Advance();
                await Application.NextTurnAsync();
            }

            manager.Dispose();
            _defaultManagers.Remove(manager);
        }

        _presenter?.Dispose();
        _presenter = null;
        _lease?.Dispose();
        _lease = null;
        _device = null;
        if (_graphics is not null)
        {
            await _graphics.DisposeAsync();
            _graphics = null;
        }
    }

    internal override async ValueTask InitializeAsync(UiApplication application, IServiceProvider services, bool textInput, CancellationToken cancellationToken)
    {
        _application = application;
        _framesInFlight = new(_renderGate, application.Options.MaximumFramesInFlight);
        (_platform, _text) = await CreatePlatformAsync(textInput, cancellationToken);
        _rawInput = _platform.CreateInputSource();
        _map = _rawInput as IWindowInputSource ?? throw new UiApplicationException("input-source-unavailable");
        _input = new([_rawInput], application.TimeProvider);
        _input.Recorded += Record;
        _text = textInput ? await CreateTextServiceAsync(_platform, cancellationToken) : null;
        _clipboard = _text is not null ? _platform.CreateClipboard() : null;
        _system = new(_platform);
        _graphics = services.GetRequiredService<IGraphicsDeviceService>();
        ConfigureTextBridge();
    }

    internal override async ValueTask<UiDriverWindow> CreateWindowAsync(string key, string title, Vector2 size, UiDriverWindow? owner, bool primary, CancellationToken cancellationToken)
    {
        IWindow native = _platform!.CreateWindow(new() { Title = title, ClientSize = size, Owner = owner?.NativeWindow });
        WindowNode node = _system!.Attach(native, WindowRole.Normal, WindowOwnership.Borrowed);
        var window = new Window(this, key, native, true, node);
        _windows.Add(window);
        native.Closing += window.OnClosing;
        try
        {
            await PrepareGraphicsAsync(window, cancellationToken);
            return window;
        }
        catch (Exception failure)
        {
            try
            {
                await CloseWindowAsync(window);
            }
            catch (Exception cleanup)
            {
                throw new UiApplicationException("window-cleanup-failed", new AggregateException(failure, cleanup));
            }

            throw;
        }
    }

    internal override IUiWindowManager CreateWindowManager(TimeProvider clock)
    {
        if (!NativeWindows)
        {
            return new HostedUiWindowManager(clock);
        }

        var native = new WindowSystemUiWindowManager(_system!, _map!, this, new(true, true), clock);
        var hosted = new HostedUiWindowManager(clock);
        _defaultManagers.Add(native);
        _defaultManagers.Add(hosted);
        return new FallbackUiWindowManager(native, hosted, clock);
    }

    internal override void Pump()
    {
        _platform?.PumpEvents();
        _system?.Update();
        foreach (Window window in _windows.Where(window => window.Owned && !window.Closing).ToArray())
        {
            if (!IsTargetConnected(window.NativeWindow) || window.NativeWindow.State.Lifetime == WindowLifetime.Closed)
            {
                Application.NotifyTargetLost(window);
            }
        }

        if (IsQuitRequested)
        {
            Application.RequestStop();
        }

        PumpGraphicsEvents(_device);
        _text?.Advance();
        while (_closing.TryDequeue(out Window? window))
        {
            CloseRequested?.Invoke(window);
        }
    }

    internal override void DispatchPendingInput() => PumpInput();

    internal override async Task<bool> DrawAsync(UiDriverWindow target, DrawList2D commands)
    {
        var window = (Window)target;
        ulong epoch = _lease?.State.Epoch ?? 0;
        Capture[] captures = window.Captures.Where(capture => !capture.Token.IsCancellationRequested).ToArray();
        using UiFrameSubmissionQueue.Flight flight = await _framesInFlight.EnterAsync();
        try
        {
            if (epoch != (_lease?.State.Epoch ?? 0))
            {
                return false;
            }

            if (_faulted)
            {
                throw new UiApplicationException("presentation-failed");
            }

            if (_lease?.IsUsable == false)
            {
                await RecoverAsync();
                return false;
            }

            if (!Drawable(window))
            {
                return false;
            }

            if (window.Graphics is null)
            {
                window.Graphics = CreateGraphicsSurface(_device!, window.NativeWindow!);
            }

            WindowState state = window.NativeWindow!.State;
            SwapchainDesc configuration = UiHostPresenter.Configuration(window.Graphics, state.PixelSize.Width, state.PixelSize.Height);
            if (window.Swapchain is null)
            {
                window.Swapchain = window.Graphics.CreateSwapchain(configuration);
            }
            else if (window.Reconfigure || window.Swapchain.Configuration != configuration)
            {
                await DrainWindowAsync(window);
                window.Swapchain.Reconfigure(configuration);
                window.Reconfigure = false;
            }

            var crops = new Dictionary<Capture, Rect2D>();
            foreach (Capture capture in captures)
            {
                if (window.Source.Recorded(capture.Surface) is { IsVisible: true } snapshot)
                {
                    Rect2D bounds = snapshot.BoundsInPresentation;
                    Rect2D clip = snapshot.ClipInPresentation;
                    float left = Math.Max(bounds.X, clip.X);
                    float top = Math.Max(bounds.Y, clip.Y);
                    float right = Math.Min(bounds.X + bounds.Width, clip.X + clip.Width);
                    float bottom = Math.Min(bounds.Y + bounds.Height, clip.Y + clip.Height);
                    crops.Add(capture, new(left * snapshot.Scale.X, top * snapshot.Scale.Y, Math.Max(0, right - left) * snapshot.Scale.X, Math.Max(0, bottom - top) * snapshot.Scale.Y));
                }
                else
                {
                    capture.Completion.TrySetException(new InvalidOperationException("The target is no longer visible."));
                }
            }

            void CompleteCapture(UiFramePixels pixels)
            {
                foreach ((Capture capture, Rect2D crop) in crops)
                {
                    try
                    {
                        capture.Token.ThrowIfCancellationRequested();
                        capture.Completion.TrySetResult(pixels.Crop(crop));
                    }
                    catch (Exception error)
                    {
                        capture.Completion.TrySetException(error);
                    }
                }
            }

            SurfaceStatus status;
            try
            {
                status = await _presenter!.DrawAsync(window.Swapchain, commands, window.Source.SourceId, crops.Count == 0 ? null : CompleteCapture, submitted: flight.Submitted);
            }
            catch (NotSupportedException error) when (crops.Count != 0)
            {
                foreach (Capture capture in crops.Keys)
                {
                    capture.Completion.TrySetException(error);
                }

                status = await _presenter!.DrawAsync(window.Swapchain, commands, window.Source.SourceId, submitted: flight.Submitted);
            }

            await flight.ResumeAsync();
            if (epoch != (_lease?.State.Epoch ?? 0))
            {
                return false;
            }

            switch (status)
            {
                case SurfaceStatus.Success:
                    return true;
                case SurfaceStatus.Suboptimal:
                    window.Reconfigure = true;
                    return true;
                case SurfaceStatus.Timeout:
                    return false;
                case SurfaceStatus.Outdated:
                    window.Reconfigure = true;
                    return false;
                case SurfaceStatus.Lost:
                    window.DisposeGraphics();
                    return false;
                case SurfaceStatus.DeviceLost:
                    await RecoverAsync();
                    return false;
                default:
                    throw new UiApplicationException("presentation-failed");
            }
        }
        catch (Exception) when (_lease?.IsUsable == false || epoch != (_lease?.State.Epoch ?? 0))
        {
            await flight.ResumeAsync();
            if (epoch == (_lease?.State.Epoch ?? 0))
            {
                await RecoverAsync();
            }

            return false;
        }
        catch
        {
            _faulted = true;
            throw;
        }
    }

    internal override async ValueTask CloseWindowAsync(UiDriverWindow target)
    {
        var window = (Window)target;
        window.Closing = true;
        foreach (Capture capture in window.Captures)
        {
            capture.Completion.TrySetException(new ObjectDisposedException(nameof(UiDriverWindow)));
        }

        window.Captures.Clear();
        await DrainWindowAsync(window);
        window.DisposeGraphics();
        window.ReturnRetention();
        if (window.Owned)
        {
            window.NativeWindow!.Closing -= window.OnClosing;
            if (window.NativeWindow.State.Lifetime == WindowLifetime.Open)
            {
                window.NativeWindow.Close();
            }

            while (window.NativeWindow.State.Lifetime != WindowLifetime.Closed)
            {
                await Application.NextTurnAsync();
            }

            window.NativeWindow.Dispose();
        }

        _windows.Remove(window);
        foreach (Guid source in _sources.Where(pair => ReferenceEquals(pair.Value, window.NativeWindow)).Select(pair => pair.Key).ToArray())
        {
            _sources.Remove(source);
        }
    }

    internal override async ValueTask DrainWindowAsync(UiDriverWindow window)
    {
        if (_lease is { IsUsable: false } lease)
        {
            await _graphics!.WaitForRetirementAsync(lease.State.Epoch);
            if (_presenter is { } retired)
            {
                await retired.ReleaseAfterDeviceRetirementAsync();
            }
        }
        else if (_presenter is { } presenter)
        {
            await presenter.DrainAsync(window.Source.SourceId);
        }
    }

    internal override IWindow? ResolveNative(Guid source) => _sources.GetValueOrDefault(source);

    internal override async ValueTask<byte[]> CaptureAsync(UiSurface surface, CancellationToken token)
    {
        if (Application.Popups.FindWindow(surface) is not { } id || !Application.Windows.TryGetSnapshot(id, out UiWindowSnapshot? snapshot) || snapshot is null)
        {
            throw new InvalidOperationException("The target is no longer bound.");
        }

        IWindow? native = ResolveNative(snapshot.SourceId);
        Window window = _windows.FirstOrDefault(window => ReferenceEquals(window.NativeWindow, native)) ?? throw new NotSupportedException("The target has no native presentation.");
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new Capture(surface, token, completion);
        window.Captures.Add(request);
        try
        {
            return await completion.Task.WaitAsync(token);
        }
        finally
        {
            window.Captures.Remove(request);
        }
    }

    internal override void CompleteFrames()
    {
        foreach (Window window in _windows.Where(window => !window.Owned).ToArray())
        {
            if (window.Drawing is { IsCompleted: true } drawing)
            {
                window.Source.CompleteFrame(drawing.IsCompletedSuccessfully && drawing.Result);
                window.Drawing = null;
                if (drawing.Exception is { } error)
                {
                    throw new UiApplicationException("presentation-failed", error.InnerException ?? error);
                }
            }
        }
    }

    internal override void AdvanceFrames()
    {
        foreach (Window window in _windows.Where(window => !window.Owned).ToArray())
        {
            if (window.Drawing is null && !window.Closing)
            {
                var canvas = new Canvas2D();
                canvas.Begin();
                window.Source.Record(canvas);
                window.Drawing = Application.DrawFrameAsync(window, canvas.End());
            }
        }
    }

    protected abstract ValueTask<(IPlatform Platform, ITextInputService? Text)> CreatePlatformAsync(bool textInput, CancellationToken cancellationToken);

    protected virtual ValueTask<ITextInputService?> CreateTextServiceAsync(IPlatform platform, CancellationToken cancellationToken) => ValueTask.FromResult<ITextInputService?>(null);

    protected virtual bool IsTargetConnected(IWindow window) => true;

    protected abstract IGraphicsSurface CreateGraphicsSurface(IGraphicDevice device, IWindow window);

    protected virtual void PumpGraphicsEvents(IGraphicDevice? device)
    {
    }

    protected virtual void ConfigureTextBridge()
    {
    }

    protected virtual bool IsTargetVisible(IWindow window) => true;

    protected void PumpInput()
    {
        _input?.Update();
        if (_text is not null)
        {
            foreach (TextInputKey key in _text.DrainKeys())
            {
                _textKeys.Enqueue(key);
            }
        }

        if (Application.State == UiApplicationState.Recovering)
        {
            _records.Clear();
            _textKeys.Clear();
            return;
        }

        if (Application.HasInputLease)
        {
            return;
        }

        while (_records.Count != 0 || _textKeys.Count != 0)
        {
            if (_records.TryPeek(out InputRecord record) && (!_textKeys.TryPeek(out TextInputKey? key) || (record.Capture?.Sequence ?? 0) <= key.Capture.Sequence))
            {
                _records.Dequeue();
                DispatchInput?.Invoke(record);
            }
            else if (_textKeys.TryDequeue(out TextInputKey? textKey))
            {
                Application.DispatchTextKey(textKey);
            }

            SynchronizeFocus?.Invoke();
        }
    }

    private bool Drawable(Window window)
    {
        WindowState state = window.NativeWindow!.State;
        return IsTargetVisible(window.NativeWindow) && state.IsVisible && state.Lifetime == WindowLifetime.Open && state.PixelSize.Width > 0 && state.PixelSize.Height > 0 && float.IsFinite(state.Scale.X) && float.IsFinite(state.Scale.Y) && state.Scale.X > 0 && state.Scale.Y > 0;
    }

    private void Record(InputRecord record)
    {
        _records.Enqueue(record);
        (InputDeviceId Device, int Kind, ulong Identity)? identity = record.Data switch
        {
            KeyData key => (record.DeviceId, 0, (ulong)key.Key),
            MouseButtonData mouse => (record.DeviceId, 1, (ulong)mouse.Button),
            TouchData touch => (record.DeviceId, 2, touch.ContactId.Value),
            PenData pen => (record.DeviceId, 3, pen.PointerId.Value),
            KeyCanceledData key => (record.DeviceId, 0, (ulong)key.Key),
            MouseButtonCanceledData mouse => (record.DeviceId, 1, (ulong)mouse.Button),
            _ => null,
        };
        if (identity is { } id)
        {
            bool down = record.Data is KeyData { IsDown: true } or MouseButtonData { IsDown: true } or TouchData { Phase: TouchPhase.Began or TouchPhase.Moved } or PenData { IsInContact: true };
            if (down)
            {
                _held[id] = record.WindowId;
            }
            else
            {
                _held.Remove(id);
            }
        }

        if (record.Data is DeviceDisconnectedData or FocusData { IsFocused: false } or WindowInputClosedData)
        {
            foreach ((InputDeviceId Device, int Kind, ulong Identity) heldIdentity in _held.Where(pair => pair.Key.Device == record.DeviceId && (record.StateWindowId is null || pair.Value == record.StateWindowId)).Select(pair => pair.Key).ToArray())
            {
                _held.Remove(heldIdentity);
            }
        }
    }

    private async Task PrepareGraphicsAsync(Window window, CancellationToken cancellationToken)
    {
        if (_lease is null)
        {
            try
            {
                await _graphics!.InitializeAsync(cancellationToken);
                _lease = await _graphics.AcquireAsync(cancellationToken);
                _device = _lease.Device;
                _presenter = new(_device, Application.Options.Background);
            }
            catch (Exception error)
            {
                throw new UiApplicationException("graphics-initialize-failed", error);
            }
        }

        window.Graphics = CreateGraphicsSurface(_device!, window.NativeWindow!);
        _sources[window.PresentationSource.SourceId] = window.NativeWindow!;
    }

    private async Task RecoverAsync()
    {
        try
        {
            await Application.BeginGraphicsRecoveryAsync();
            if (_lease is not null)
            {
                await _graphics!.WaitForRetirementAsync(_lease.State.Epoch);
                if (_presenter is { } retired)
                {
                    await retired.ReleaseAfterDeviceRetirementAsync();
                }
            }

            foreach (Window window in _windows)
            {
                window.DisposeGraphics();
            }

            _presenter?.Dispose();
            _presenter = null;
            _lease?.Dispose();
            _lease = null;
            _device = null;
            await _graphics!.RetireAsync();
            await _graphics.RecoverAsync();
            _lease = await _graphics.AcquireAsync();
            _device = _lease.Device;
            _presenter = new(_device, Application.Options.Background);
            await Application.EndGraphicsRecoveryAsync();
        }
        catch (Exception error)
        {
            _faulted = true;
            throw new UiApplicationException("graphics-recovery-failed", error);
        }
    }

    private sealed record Capture(UiSurface Surface, CancellationToken Token, TaskCompletionSource<byte[]> Completion);

    private sealed class Window : UiDriverWindow
    {
        private readonly UiPlatformApplicationDriver _driver;
        private readonly IWindow _native;
        private readonly IUiWindowSource? _presentation;
        private readonly Guid _scope = Guid.NewGuid();
        private IDisposable? _retention;
        private UiWindowSourceSnapshot? _previous;
        private ulong _revision;

        internal Window(UiPlatformApplicationDriver driver, string key, IWindow native, bool owned, WindowNode? node)
            : base(key)
        {
            _driver = driver;
            _native = native;
            Owned = owned;
            _retention = native.RetainNativeTarget();
            if (node is not null && driver.NativeWindows)
            {
                _presentation = new WindowSystemUiWindowSource(node, driver._map!, driver);
            }
        }

        internal override IWindow NativeWindow => _native;

        internal override IUiWindowSource PresentationSource => _presentation ?? Source;

        internal bool Owned { get; }

        internal List<Capture> Captures { get; } = [];

        internal bool Closing { get; set; }

        internal IGraphicsSurface? Graphics { get; set; }

        internal IGraphicsSwapchain? Swapchain { get; set; }

        internal bool Reconfigure { get; set; }

        internal Task<bool>? Drawing { get; set; }

        public override ValueTask DisposeAsync() => _driver.CloseWindowAsync(this);

        internal override UiWindowSourceSnapshot CaptureState()
        {
            WindowState state = _native.State;
            InputWindowId? origin = _driver._map!.TryGetInputWindowId(_native.Id, out InputWindowId id) ? id : null;
            var next = new UiWindowSourceSnapshot(0, origin, state.ClientSize, state.Scale, state.IsVisible && _driver.IsTargetVisible(_native), state.IsActive, state.Lifetime == WindowLifetime.Closed, _scope);
            if (_previous is not null && _previous with { Revision = 0 } == next)
            {
                return _previous;
            }

            _previous = next with { Revision = checked(++_revision) };
            return _previous;
        }

        internal override void Show() => _native.Show();

        internal override void SetTitle(string title) => _native.SetTitle(title);

        internal void OnClosing(object? sender, WindowClosingEventArgs args)
        {
            args.Cancel = true;
            _driver._closing.Enqueue(this);
        }

        internal void ReturnRetention()
        {
            _retention?.Dispose();
            _retention = null;
        }

        internal void DisposeGraphics()
        {
            Swapchain?.Dispose();
            Swapchain = null;
            Graphics?.Dispose();
            Graphics = null;
        }
    }

    private sealed class AuxiliaryPresentation(UiPlatformApplicationDriver driver, Window window, IUiWindowPresentation inner) : IUiWindowPresentation
    {
        public ulong PresentedGeometryRevision => inner.PresentedGeometryRevision;

        public void Update(UiWindowPresentationView view) => inner.Update(view);

        public async ValueTask DisposeAsync()
        {
            window.Closing = true;
            driver.Application.DetachAuxInput(window);
            await inner.DisposeAsync();
            if (window.Drawing is { } drawing)
            {
                await drawing;
                window.Source.CompleteFrame(drawing.Result);
                window.Drawing = null;
            }

            await driver.CloseWindowAsync(window);
        }
    }
}
