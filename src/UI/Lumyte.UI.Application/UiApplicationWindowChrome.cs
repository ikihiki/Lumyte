using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using Lumyte.TextEditing;
using Lumyte.UI.Automation;
using Lumyte.UI.TextEditing;
using Lumyte.UI.Windowing;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application;

/// <summary>Provides the title bar and resize grip of a hosted application window.</summary>
public sealed class UiApplicationWindowChrome
{
    private const float TitleHeight = 24;
    private const float GripSize = 12;
    private readonly UiApplication _application;
    private readonly UiApplicationWindowContext _context;
    private readonly Action _closeRequested;
    private readonly List<UiWindowRequestId> _requests = [];
    private UiWindowId _root;
    private UiWindowId _headerWindow;
    private UiWindowId _gripWindow;
    private UiSurface? _header;
    private UiSurface? _grip;
    private UiWindowBinding? _headerBinding;
    private UiWindowBinding? _gripBinding;
    private UiPopupOwner? _headerOwner;
    private UiPopupOwner? _gripOwner;
    private UiApplicationAutomationHost? _headerHost;
    private UiApplicationAutomationHost? _gripHost;
    private Vector2 _position;
    private Vector2 _size;
    private bool _stopping;
    private bool _geometryPending;
    private string _title;

    internal UiApplicationWindowChrome(UiApplication application, UiApplicationWindowContext context, string title, Action closeRequested)
    {
        _application = application;
        _context = context;
        _title = title;
        _closeRequested = closeRequested;
    }

    /// <summary>Gets routed automation for the title bar and close button.</summary>
    public IUiAutomationHost Header => _headerHost ?? throw new InvalidOperationException("The title bar is not ready.");

    /// <summary>Gets routed automation for the resize grip.</summary>
    public IUiAutomationHost ResizeGrip => _gripHost ?? throw new InvalidOperationException("The resize grip is not ready.");

    /// <summary>Gets the internal title-bar window identity.</summary>
    public UiWindowId HeaderWindow => _headerWindow;

    /// <summary>Gets the internal resize-grip window identity.</summary>
    public UiWindowId ResizeWindow => _gripWindow;

    internal UiSurface? HeaderSurface => _header;

    internal UiSurface? ResizeSurface => _grip;

    internal bool IsStopped => _stopping && (_headerHost?.IsStopped ?? true) && (_gripHost?.IsStopped ?? true) && (_headerOwner?.IsReleased ?? true) && (_gripOwner?.IsReleased ?? true);

    internal async ValueTask PrepareAsync(Vector2 requestedSize)
    {
        if (!_context.Windows.TryGetSnapshot(_context.Window, out UiWindowSnapshot? body) || body is null)
        {
            throw new UiApplicationException("invalid-window");
        }

        _root = body.PresentationRoot;
        _size = requestedSize;
        _position = new(16, 40);
        Clamp();
        Remember(_context.Windows.Resize(_context.Window, _size));
        Remember(_context.Windows.Place(_context.Window, Placement(_position)));
        _headerWindow = _context.Windows.Create(new(_context.Window, new(_size.X, TitleHeight), UiWindowRole.Tool, UiWindowInputMode.Interactive, Placement(_position - new Vector2(0, TitleHeight)), UiWindowPolicy.HostedOnly));
        _gripWindow = _context.Windows.Create(new(_context.Window, new(Math.Min(GripSize, _size.X), Math.Min(GripSize, _size.Y)), UiWindowRole.Tool, UiWindowInputMode.Interactive, Placement(_position + _size - Vector2.Min(new(GripSize), _size)), UiWindowPolicy.HostedOnly));
        _header = new(new HeaderRoot(this, _title, _context.TextLayoutProvider));
        _grip = new(new Motion(this, true));
        _headerBinding = await _context.Windows.BindPresentationAsync(_headerWindow, _header);
        _gripBinding = await _context.Windows.BindPresentationAsync(_gripWindow, _grip);
        _headerOwner = _application.Popups.RegisterOwner(_headerWindow, _header);
        _gripOwner = _application.Popups.RegisterOwner(_gripWindow, _grip);
        _headerHost = UiApplicationAutomationHost.Create(_application, () => _header);
        _gripHost = UiApplicationAutomationHost.Create(_application, () => _grip);
    }

    internal void Show()
    {
        Remember(_context.Windows.Show(_headerWindow));
        Remember(_context.Windows.Show(_gripWindow));
    }

    internal void UpdateTitle(string title)
    {
        if (_title != title)
        {
            _title = title;
            _header?.Rebuild(new HeaderRoot(this, title, _context.TextLayoutProvider));
        }
    }

    internal void BeginStop()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        _headerHost?.BeginStop();
        _gripHost?.BeginStop();
        _header?.Dispatch(new UiFocusLostInput());
        _grip?.Dispatch(new UiFocusLostInput());
        _headerOwner?.Dispose();
        _gripOwner?.Dispose();
    }

    internal async ValueTask DetachAsync()
    {
        _headerHost?.Dispose();
        _gripHost?.Dispose();
        if (_headerBinding is not null)
        {
            await _headerBinding.DisposeAsync();
            _headerBinding = null;
        }

        if (_gripBinding is not null)
        {
            await _gripBinding.DisposeAsync();
            _gripBinding = null;
        }

        _header?.Dispose();
        _header = null;
        _grip?.Dispose();
        _grip = null;
        foreach (UiWindowId window in new[] { _headerWindow, _gripWindow })
        {
            if (window == default || !_context.Windows.TryGetSnapshot(window, out UiWindowSnapshot? snapshot))
            {
                continue;
            }

            UiWindowRequestId close = _context.Windows.Close(window);
            while (!_context.Windows.TryGetRequest(close, out UiWindowRequestResult? result) || result?.Status == UiWindowRequestStatus.Pending)
            {
                await _application.NextTurnAsync();
            }

            _context.Windows.Release(window);
        }
    }

    internal void Advance()
    {
        if (_context.Windows is IUiWindowRequestLifetime lifetime)
        {
            for (int index = _requests.Count - 1; index >= 0; index--)
            {
                if (lifetime.ReleaseRequest(_requests[index]) || !_context.Windows.TryGetRequest(_requests[index], out _))
                {
                    _requests.RemoveAt(index);
                }
            }
        }

        if (!_geometryPending || _stopping)
        {
            return;
        }

        _geometryPending = false;
        Remember(_context.Windows.Resize(_context.Window, _size));
        Remember(_context.Windows.Place(_context.Window, Placement(_position)));
        Remember(_context.Windows.Resize(_headerWindow, new(_size.X, TitleHeight)));
        Remember(_context.Windows.Place(_headerWindow, Placement(_position - new Vector2(0, TitleHeight))));
        var grip = Vector2.Min(new(GripSize), _size);
        Remember(_context.Windows.Resize(_gripWindow, grip));
        Remember(_context.Windows.Place(_gripWindow, Placement(_position + _size - grip)));
    }

    private UiWindowPlacement Placement(Vector2 position) => new(_root, new Rect2D(position.X, position.Y, 0, 0), UiWindowPlacementSide.Below);

    private void Clamp()
    {
        _context.Windows.TryGetSnapshot(_root, out UiWindowSnapshot? root);
        Vector2 limit = root?.ClientSize ?? new(800, 600);
        _size = Vector2.Clamp(_size, new(Math.Min(1, limit.X), Math.Min(1, limit.Y)), new(Math.Max(1, limit.X), Math.Max(1, limit.Y - TitleHeight)));
        _position = Vector2.Clamp(_position, new(0, Math.Min(TitleHeight, Math.Max(0, limit.Y - _size.Y))), Vector2.Max(Vector2.Zero, limit - _size));
    }

    private void Change(Vector2 position, Vector2 size)
    {
        _position = position;
        _size = size;
        Clamp();
        _geometryPending = true;
    }

    private void Remember(UiWindowRequestId request)
    {
        if (_context.Windows is IUiWindowRequestLifetime)
        {
            _requests.Add(request);
        }
    }

    private void Activate(UiWindowId target)
    {
        Remember(_context.Windows.BringToFront(_context.Window));
        Remember(_context.Windows.BringToFront(_headerWindow));
        Remember(_context.Windows.BringToFront(_gripWindow));
        _context.Windows.SetKeyboardTarget(target);
    }

    private sealed class HeaderRoot : Lumyte.UI.Compose.Definitions.Panel
    {
        private readonly Widget _move;
        private readonly Widget _close;

        internal HeaderRoot(UiApplicationWindowChrome chrome, string title, IUiTextLayoutProvider? provider)
        {
            _move = new Motion(chrome, false, title, provider);
            _close = new CloseButton(chrome);
            Children.Initialize(_move, _close);
            ClipToBounds = true;
        }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            Vector2 size = constraints.Constrain(new(constraints.MaxWidth, TitleHeight));
            float close = Math.Min(TitleHeight, size.X);
            LayoutChild(_move, UiBoxConstraints.Tight(new(size.X - close, size.Y)));
            SetChildOffset(_move, Vector2.Zero);
            LayoutChild(_close, UiBoxConstraints.Tight(new(close, size.Y)));
            SetChildOffset(_close, new(size.X - close, 0));
            return size;
        }
    }

    private sealed class CloseButton : Lumyte.UI.Compose.Definitions.Button
    {
        internal CloseButton(UiApplicationWindowChrome chrome)
        {
            TestId = "window-close";
            AutomationName = "Close window";
            OnClick = () => chrome._closeRequested();
        }

        protected override void OnRecord(DrawingCanvas2D canvas)
        {
            canvas.FillRect(new(0, 0, Size.X, Size.Y), IsPressed ? new Color2D(0.65f, 0.15f, 0.15f, 1) : new Color2D(0.3f, 0.3f, 0.35f, 1));
            var stroke = new StrokeStyle2D { Width = 2 };
            canvas.DrawLine(new(Size.X * 0.3f, Size.Y * 0.3f), new(Size.X * 0.7f, Size.Y * 0.7f), new Color2D(1, 1, 1, 1), stroke);
            canvas.DrawLine(new(Size.X * 0.7f, Size.Y * 0.3f), new(Size.X * 0.3f, Size.Y * 0.7f), new Color2D(1, 1, 1, 1), stroke);
        }
    }

    private sealed class Motion : Widget
    {
        private readonly UiApplicationWindowChrome _chrome;
        private readonly bool _resize;
        private readonly string? _title;
        private readonly IUiTextLayoutProvider? _provider;
        private Vector2 _pointerStart;
        private Vector2 _windowStart;
        private Vector2 _sizeStart;
        private UiTextLayout? _caption;

        internal Motion(UiApplicationWindowChrome chrome, bool resize, string? title = null, IUiTextLayoutProvider? provider = null)
        {
            _chrome = chrome;
            _resize = resize;
            _title = title;
            _provider = provider;
            TestId = resize ? "window-resize" : "window-move";
            AutomationName = resize ? "Resize window" : title;
            Role = UiRole.Button;
            Focusable = true;
            ClipToBounds = true;
        }

        protected override bool RetainsInputOnRebuild => true;

        protected override UiAutomationActions AutomationActions => UiAutomationActions.Click | UiAutomationActions.Focus;

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            if (_caption is null && _provider is not null && _title is not null)
            {
                _caption = _provider.CreateLayout(new TextDocument(_title).CaptureSnapshot(), float.PositiveInfinity);
            }

            return constraints.Constrain(new(constraints.MaxWidth, constraints.MaxHeight));
        }

        protected override UiInputResult OnProbePointerDown(Vector2 localPosition) => UiInputResult.Handled;

        protected override UiInputResult OnInput(in UiInputContext context)
        {
            UiWindowId window = _resize ? _chrome._gripWindow : _chrome._headerWindow;
            if (context.Input is UiPointerInput pointer)
            {
                if (!_chrome._context.Windows.TryTransformPoint(window, _chrome._root, pointer.Position, out Vector2 screen))
                {
                    return UiInputResult.Handled;
                }

                if (pointer.Phase == UiPointerPhase.Down)
                {
                    _chrome.Activate(window);
                    context.RequestFocus();
                    context.CapturePointer();
                    _pointerStart = screen;
                    _windowStart = _chrome._position;
                    _sizeStart = _chrome._size;
                }
                else if (pointer.Phase == UiPointerPhase.Move && context.IsCaptured)
                {
                    Vector2 delta = screen - _pointerStart;
                    _chrome.Change(_resize ? _windowStart : _windowStart + delta, _resize ? _sizeStart + delta : _sizeStart);
                }

                return UiInputResult.Handled;
            }

            if (context.Input is UiKeyInput key && context.IsFocused)
            {
                Vector2 delta = key.Key switch
                {
                    Lumyte.Input.Abstractions.Key.ArrowLeft => new(-8, 0),
                    Lumyte.Input.Abstractions.Key.ArrowRight => new(8, 0),
                    Lumyte.Input.Abstractions.Key.ArrowUp => new(0, -8),
                    Lumyte.Input.Abstractions.Key.ArrowDown => new(0, 8),
                    _ => Vector2.Zero,
                };
                if (delta != Vector2.Zero)
                {
                    if (key.IsDown)
                    {
                        context.CaptureKey();
                        _chrome.Change(_resize ? _chrome._position : _chrome._position + delta, _resize ? _chrome._size + delta : _chrome._size);
                    }

                    return UiInputResult.Handled;
                }
            }

            return UiInputResult.Unhandled;
        }

        protected override void OnReconcile(Widget previous)
        {
            var motion = (Motion)previous;
            _pointerStart = motion._pointerStart;
            _windowStart = motion._windowStart;
            _sizeStart = motion._sizeStart;
        }

        protected override void OnRecord(DrawingCanvas2D canvas)
        {
            canvas.FillRect(new(0, 0, Size.X, Size.Y), new Color2D(0.2f, 0.25f, 0.35f, 1));
            if (_caption is not null)
            {
                canvas.Translate(new(4, 2));
                canvas.DrawText(_caption.Drawing);
            }
        }
    }
}
