using System.Numerics;
using Lumyte.Input.Abstractions;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Windowing.Benchmark;

/// <summary>Public fixture with actual manager and surface lifetimes.</summary>
public sealed class HostedWindowSession : IDisposable
{
    private readonly HostSource _source = new();
    private readonly Lumyte.UI.Windowing.HostedUiWindowManager _windows = new();
    private readonly UiPopupManager _popups;
    private readonly UiSurface _surface;
    private readonly UiWindowRegistration _registration;
    private readonly UiWindowBinding _binding;
    private readonly UiPopupOwner _owner;
    private readonly UiElement _anchor;
    private UiPopupHandle? _inputMenu;
    private int _opened;
    private int _closed;
    private int _clicks;

    /// <summary>Initializes a new instance of the <see cref="HostedWindowSession"/> class.</summary>
    public HostedWindowSession()
    {
        _popups = new(_windows);
        _surface = new(Panel()[Button(width: 20, height: 20, testId: "anchor")]);
        _anchor = _surface.RootElement.Children[0];
        _registration = _windows.Register(_source);
        Task<UiWindowBinding> binding = _windows.BindPresentationAsync(_registration.Window, _surface).AsTask();
        Pump();
        _binding = binding.GetAwaiter().GetResult();
        _owner = _popups.RegisterOwner(_registration.Window, _surface);
        _surface.MoveFocus(UiFocusNavigation.First);
    }

    /// <summary>Gets observed opening callbacks.</summary>
    public int Opened => _opened;

    /// <summary>Gets observed closure callbacks.</summary>
    public int Closed => _closed;

    /// <summary>Checks root input isolation and acknowledged geometry.</summary>
    public void VerifyRoot()
    {
        Require(_windows.ResolveInputRoot(_source.Origin) == _registration.Window);
        Require(_windows.ResolveInputRoot(new(Guid.NewGuid())) is null);
        Require(_source.Prepared == 1 && _source.Acknowledged > 0);
    }

    /// <summary>Runs a finite wave including retirement.</summary>
    /// <param name="submenu">Whether to open a child.</param>
    /// <returns>Cumulative closure callbacks.</returns>
    public int PopupCycle(bool submenu)
    {
        CloseInput();
        int opened = _opened;
        int closed = _closed;
        UiPopupHandle root = Open();
        Require(_surface.FocusedElement is null);
        Require(_popups.TryGetSurface(root.Id, out UiSurface? menu));
        Require(!ReferenceEquals(menu, _surface) && menu!.FocusedElement is not null);
        UiPopupHandle? child = null;
        if (submenu)
        {
            child = _popups.RequestOpen(root, Request(menu!.FocusedElement!, UiWindowPlacementSide.Right));
            Pump();
            Require(child.State == UiPopupState.Open);
        }

        root.Close();
        Pump();
        Require(root.State == UiPopupState.Closed && (child is null || child.State == UiPopupState.Closed));
        Require(_opened - opened == (submenu ? 2 : 1) && _closed - closed == (submenu ? 2 : 1));
        Require(ReferenceEquals(_surface.FocusedElement, _anchor));
        Require(_popups.CapturePopups().Count == 0 && _windows.CaptureWindows().Count == 1);
        Require(_source.Prepared - _source.Disposed == 1);
        return _closed;
    }

    /// <summary>Routes down/up and Enter without menu recreation after the first call.</summary>
    /// <returns>Actual button callback count.</returns>
    public int InputCycle()
    {
        _inputMenu ??= Open();
        int clicks = _clicks;
        Send(Key.ArrowDown, true);
        Send(Key.ArrowDown, false);
        Send(Key.ArrowUp, true);
        Send(Key.ArrowUp, false);
        Send(Key.Enter, true);
        Send(Key.Enter, false);
        Require(_clicks == clicks + 1);
        return _clicks;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        CloseInput();
        _owner.Dispose();
        _popups.BeginStop();
        Pump();
        Require(_popups.IsStopped);
        _popups.Dispose();
        Task retired = _binding.DisposeAsync().AsTask();
        for (int i = 0; i < 12; i++)
        {
            _windows.Advance();
            _source.Present();
        }

        retired.GetAwaiter().GetResult();
        _registration.Dispose();
        _windows.BeginStop();
        _windows.Advance();
        Require(_registration.IsReleased && _windows.IsStopped && _source.Prepared == _source.Disposed);
        _windows.Dispose();
        _surface.Dispose();
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Hosted lifecycle oracle failed.");
        }
    }

    private UiPopupRequest Request(UiElement anchor, UiWindowPlacementSide side = UiWindowPlacementSide.Below) => new(anchor, new(80, 60), () => Stack(orientation: UiOrientation.Vertical)[Button(width: 60, height: 20, onClick: () => _clicks++), Button(width: 60, height: 20)], side: side, onOpened: _ => _opened++, onClosed: _ => _closed++);

    private UiPopupHandle Open()
    {
        UiPopupHandle popup = _popups.RequestOpen(_owner, Request(_anchor));
        Pump();
        Require(popup.State == UiPopupState.Open);
        return popup;
    }

    private void CloseInput()
    {
        if (_inputMenu is { } menu)
        {
            menu.Close();
            Pump();
            _inputMenu = null;
        }
    }

    private void Send(Key key, bool down) => Require(_popups.Dispatch(new(_source.Origin, new UiKeyInput(key, down))));

    private void Pump()
    {
        for (int i = 0; i < 12; i++)
        {
            _windows.Advance();
            _popups.Advance();
            _source.Present();
        }
    }

    private sealed class HostSource : IUiWindowSource
    {
        private readonly List<Presentation> _presentations = [];

        public Guid SourceId { get; } = Guid.NewGuid();

        internal InputWindowId Origin { get; } = new(Guid.NewGuid());

        internal int Prepared { get; private set; }

        internal int Disposed { get; private set; }

        internal int Acknowledged { get; private set; }

        public UiWindowSourceSnapshot CaptureState() => new(1, Origin, new(400, 300), Vector2.One, true, true, false, SourceId);

        public ValueTask<IUiWindowPresentation> PreparePresentationAsync(UiWindowPresentationView view, UiSurface surface, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Prepared++;
            var presentation = new Presentation(this, view);
            _presentations.Add(presentation);
            return ValueTask.FromResult<IUiWindowPresentation>(presentation);
        }

        public bool TryTransformPoint(IUiWindowSource destination, Vector2 position, out Vector2 transformed)
        {
            transformed = position;
            return ReferenceEquals(this, destination);
        }

        internal void Present()
        {
            foreach (Presentation presentation in _presentations)
            {
                if (presentation.PresentedGeometryRevision != presentation.View.GeometryRevision)
                {
                    presentation.PresentedGeometryRevision = presentation.View.GeometryRevision;
                    Acknowledged++;
                }
            }
        }

        private sealed class Presentation(HostSource source, UiWindowPresentationView view) : IUiWindowPresentation
        {
            public ulong PresentedGeometryRevision { get; set; }

            internal UiWindowPresentationView View { get; private set; } = view;

            public void Update(UiWindowPresentationView next) => View = next;

            public ValueTask DisposeAsync()
            {
                source.Disposed++;
                source._presentations.Remove(this);
                return ValueTask.CompletedTask;
            }
        }
    }
}
