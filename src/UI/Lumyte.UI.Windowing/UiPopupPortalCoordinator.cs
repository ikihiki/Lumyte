using System.Numerics;
using Popup = Lumyte.UI.Windowing.Compose.Definitions.Popup;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Windowing;

/// <summary>Reconciles controlled popup declarations for one borrowed owner surface.</summary>
/// <remarks>Advance after owner layout on the window-manager owner. Dispose before releasing its popup owner.</remarks>
public sealed class UiPopupPortalCoordinator : IDisposable
{
    private readonly UiPopupManager _manager;
    private readonly UiPopupOwner _owner;
    private readonly UiSurface _surface;
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private bool _disposed;
    private bool _enabled = true;

    /// <summary>Initializes a new instance of the <see cref="UiPopupPortalCoordinator"/> class.</summary>
    /// <param name="manager">The borrowed popup manager.</param>
    /// <param name="owner">Its registered borrowed owner.</param>
    /// <param name="surface">The same owner's surface.</param>
    public UiPopupPortalCoordinator(UiPopupManager manager, UiPopupOwner owner, UiSurface surface)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(surface);
        _manager = manager;
        _owner = owner;
        _surface = surface;
    }

    /// <summary>Reconciles fresh declarations while preserving a continuing popup session.</summary>
    public void Advance()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_enabled)
        {
            return;
        }

        Widget[] nodes = _surface.Root.CollectSubtree().ToArray();
        Popup[] declarations = nodes.OfType<Popup>().ToArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Popup declaration in declarations)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(declaration.Key);
            ArgumentException.ThrowIfNullOrWhiteSpace(declaration.Anchor);
            if (!keys.Add(declaration.Key))
            {
                throw new InvalidOperationException("Popup keys must be unique on their owner surface.");
            }

            if (nodes.Count(widget => widget.Key == declaration.Anchor) > 1)
            {
                throw new InvalidOperationException("A popup anchor key must identify one widget on the owner surface.");
            }
        }

        foreach (string removed in _sessions.Keys.Where(key => !keys.Contains(key)).ToArray())
        {
            _sessions[removed].Handle?.Close();
            _sessions.Remove(removed);
        }

        foreach (Popup declaration in declarations)
        {
            if (!_sessions.TryGetValue(declaration.Key!, out Session? session))
            {
                session = new();
                _sessions.Add(declaration.Key!, session);
            }

            Popup? previous = session.Declaration;
            session.Declaration = declaration;
            if (!declaration.IsOpen)
            {
                session.Handle?.Close();
                session.Requested = false;
                continue;
            }

            bool replacing = previous is not null && previous.Presentation != declaration.Presentation;
            if (replacing)
            {
                session.Handle?.Close();
                session.Requested = false;
            }

            if (session.Handle is { State: not (UiPopupState.Closed or UiPopupState.Failed) } active)
            {
                if (!ReferenceEquals(previous, declaration) && !active.IsCloseRequested)
                {
                    _manager.UpdateDeclaration(active, BuildContent(declaration), Side(declaration.Placement), End(declaration.Placement));
                }

                continue;
            }

            if (session.Requested || ResolveAnchor(session) is not { IsVisible: true, IsEnabled: true } anchor)
            {
                continue;
            }

            Widget root = BuildContent(declaration);
            Vector2 size;
            using (var measure = new UiSurface(root))
            {
                size = Vector2.Max(Vector2.One, measure.MeasureDesiredSize());
            }

            session.Requested = true;
            bool initialContent = true;
            var request = new UiPopupRequest(
                anchor,
                size,
                () =>
                {
                    if (initialContent)
                    {
                        initialContent = false;
                        return root;
                    }

                    return BuildContent(session.Declaration!);
                },
                Policy(declaration.Presentation),
                side: Side(declaration.Placement),
                onOpened: result => session.Declaration?.OnOpened?.Invoke(result),
                onClosed: result => session.Declaration?.OnClosed?.Invoke(result),
                onDismissRequested: result => session.Declaration?.OnDismiss?.Invoke(result))
            {
                ResolveAnchor = () => ResolveAnchor(session),
                AlignEnd = End(declaration.Placement),
            };
            session.Handle = _manager.RequestOpen(_owner, request);
        }
    }

    /// <summary>Stops new sessions and queues existing sessions for normal ordered cleanup.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Session session in _sessions.Values)
        {
            session.Handle?.Close();
        }

        _sessions.Clear();
    }

    internal void SetUpdatesEnabled(bool enabled) => _enabled = enabled;

    private static Widget BuildContent(Popup declaration)
    {
        UiDeclarationRecipe[] recipes = declaration.Content.Select(child => UiDeclarationRecipe.Capture(child)
            ?? throw new InvalidOperationException("Declarative popup content requires generated composition factories.")).ToArray();
        return Lumyte.UI.Compose.Panel()[recipes.Select(recipe => recipe.Build()).ToArray()];
    }

    private static UiWindowPolicy Policy(PopupPresentation presentation) => presentation switch
    {
        PopupPresentation.Hosted => UiWindowPolicy.HostedOnly,
        PopupPresentation.NativeRequired => UiWindowPolicy.RequireNative,
        _ => UiWindowPolicy.PreferNative,
    };

    private static UiWindowPlacementSide Side(PopupPlacement placement) => placement switch
    {
        PopupPlacement.TopStart or PopupPlacement.TopEnd => UiWindowPlacementSide.Above,
        PopupPlacement.LeftStart => UiWindowPlacementSide.Left,
        PopupPlacement.RightStart => UiWindowPlacementSide.Right,
        _ => UiWindowPlacementSide.Below,
    };

    private static bool End(PopupPlacement placement) => placement is PopupPlacement.BottomEnd or PopupPlacement.TopEnd;

    private Widget? ResolveAnchor(Session session)
    {
        if (_disposed || session.Declaration is not { } declaration)
        {
            return null;
        }

        Widget[] anchors = _surface.Root.CollectSubtree().Where(widget => widget.Key == declaration.Anchor).ToArray();
        return anchors.Length == 1 ? anchors[0] : null;
    }

    private sealed class Session
    {
        internal Popup? Declaration { get; set; }

        internal UiPopupHandle? Handle { get; set; }

        internal bool Requested { get; set; }
    }
}
