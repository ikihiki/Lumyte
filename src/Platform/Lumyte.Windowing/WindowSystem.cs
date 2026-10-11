using System.Numerics;
using System.Runtime.CompilerServices;
using Lumyte.Platform.Abstractions;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Windowing;

/// <summary>Optionally manages native windows and hosted display regions on one thread.</summary>
public sealed class WindowSystem : IDisposable
{
    private static readonly ConditionalWeakTable<IWindow, WindowSystem> _registrations = new();
    private readonly IPlatform _platform;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Guid _id = Guid.NewGuid();
    private readonly Dictionary<WindowNodeId, Node> _nodes = [];
    private readonly Queue<Action> _pending = new();
    private readonly Dictionary<WindowNodeRequestId, WindowNodeId> _outstanding = [];
    private readonly Queue<Action> _nativeEvents = new();
    private readonly Dictionary<(WindowId, WindowRequestId), (WindowNodeId, WindowNodeRequestId)> _requests = [];
    private readonly List<Termination> _terminations = [];
    private readonly Dictionary<WindowNodeId, WindowNodeId> _lastTargets = [];
    private ulong _nextNode;
    private ulong _nextRequest;
    private ulong _nextOrder;
    private bool _updating;
    private bool _disposed;
    private WindowNodeId? _keyboardTarget;
    private List<Exception>? _errors;

    /// <summary>Initializes a new instance of the <see cref="WindowSystem"/> class.</summary>
    /// <param name="platform">The borrowed platform supplying native windows.</param>
    public WindowSystem(IPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        _platform = platform;
    }

    /// <summary>Occurs after a managed state changes.</summary>
    public event Action<WindowNodeState>? Changed;

    /// <summary>Occurs after a node is unregistered.</summary>
    public event Action<WindowNodeId>? Removed;

    /// <summary>Occurs once for each completed management request.</summary>
    public event Action<WindowNodeRequestResult>? RequestCompleted;

    /// <summary>Asks the owner of a borrowed native window to close or detach it.</summary>
    public event Action<WindowNodeId>? CloseRequested;

    /// <summary>Gets immutable snapshots of the managed windows.</summary>
    public IReadOnlyList<WindowNodeState> Windows
    {
        get
        {
            Check();
            return Array.AsReadOnly(_nodes.Values.Select(Snapshot).ToArray());
        }
    }

    /// <summary>Gets the logical keyboard target in the active native window.</summary>
    public WindowNodeId? KeyboardTarget
    {
        get
        {
            Check();
            return _keyboardTarget;
        }
    }

    /// <summary>Creates and owns a native window.</summary>
    /// <param name="descriptor">Native creation options.</param>
    /// <param name="role">The management role.</param>
    /// <param name="parent">The registered native owner, if present.</param>
    /// <returns>The new management identifier.</returns>
    public WindowNodeId CreateNativeWindow(WindowDescriptor descriptor, WindowRole role, WindowNodeId? parent = null)
    {
        CheckMutation();
        ArgumentNullException.ThrowIfNull(descriptor);
        ValidateRole(role);
        ValidateNativeRole(role, descriptor.Kind, descriptor.Activation);
        IWindow? owner = parent is { } id ? GetNativeWindow(id) : null;
        if (!ReferenceEquals(owner, descriptor.Owner))
        {
            throw new ArgumentException("Parent must match the descriptor's native owner.", nameof(parent));
        }

        IWindow window = _platform.CreateWindow(descriptor);
        try
        {
            return Attach(window, role, WindowOwnership.Owned);
        }
        catch
        {
            window.Dispose();
            throw;
        }
    }

    /// <summary>Registers a native window without wrapping its implementation.</summary>
    /// <param name="window">The open native window on this platform.</param>
    /// <param name="role">The management role.</param>
    /// <param name="ownership">Whether ownership transfers after successful registration.</param>
    /// <returns>The management identifier.</returns>
    public WindowNodeId Attach(IWindow window, WindowRole role, WindowOwnership ownership)
    {
        CheckMutation();
        ArgumentNullException.ThrowIfNull(window);
        ValidateRole(role);
        if (!Enum.IsDefined(ownership) || window.Id.PlatformId != _platform.Id || window.State.Lifetime != WindowLifetime.Open)
        {
            throw new ArgumentException("Window must be open and belong to this platform.", nameof(window));
        }

        ValidateNativeRole(role, window.State.Kind, window.State.Activation);

        WindowNodeId? parent = null;
        if (window.State.Owner is { } owner)
        {
            parent = _nodes.Values.SingleOrDefault(n => n.Native?.Id == owner)?.Id
                ?? throw new ArgumentException("Register the native owner first.", nameof(window));
            if (_terminations.Any(termination => termination.Nodes.Contains(parent.Value)))
            {
                throw new ArgumentException("The native owner is closing.", nameof(window));
            }
        }

        if (_registrations.TryGetValue(window, out _) || _nodes.Values.Any(n => n.Native?.Id == window.Id))
        {
            throw new ArgumentException("Window is already registered.", nameof(window));
        }

        var id = new WindowNodeId(_id, checked(++_nextNode));
        var node = new Node(id, parent, role, ownership, window.State.Activation, checked(++_nextOrder)) { Native = window };
        node.Observed = window.State;
        node.WasActive = window.State.IsActive;
        node.StateHandler = state => _nativeEvents.Enqueue(() => NativeChanged(node, state));
        node.ResultHandler = result => _nativeEvents.Enqueue(() => NativeCompleted(result));
        node.ClosingHandler = (_, args) =>
        {
            // Window changes are forbidden inside its callback; defer management work.
            args.Cancel = true;
            _nativeEvents.Enqueue(() => UserClose(node));
        };
        _registrations.Add(window, this);
        _nodes.Add(id, node);
        window.StateChanged += node.StateHandler;
        window.RequestCompleted += node.ResultHandler;
        window.Closing += node.ClosingHandler;
        _nativeEvents.Enqueue(() => Publish(node));
        return id;
    }

    /// <summary>Creates a hosted region on its nearest native ancestor's presentation target.</summary>
    /// <param name="descriptor">The parent, bounds and interaction policy.</param>
    /// <returns>The new management identifier.</returns>
    public WindowNodeId CreateHostedWindow(HostedWindowDescriptor descriptor)
    {
        CheckMutation();
        ArgumentNullException.ThrowIfNull(descriptor);
        Node parent = GetNode(descriptor.Parent);
        ValidateBounds(descriptor.Bounds);
        ValidateRole(descriptor.Role);
        if (!Enum.IsDefined(descriptor.Activation) || Layer(descriptor.Role) < Layer(parent.Role) || Snapshot(parent).Lifetime != WindowLifetime.Open || Root(parent).Native!.State.Lifetime != WindowLifetime.Open
            || _terminations.Any(t => t.Nodes.Contains(parent.Id)))
        {
            throw new ArgumentException("Invalid activation, layer or parent lifetime.", nameof(descriptor));
        }

        if (IsTemporary(descriptor.Role) && descriptor.Activation != WindowActivation.PreserveFocus)
        {
            throw new ArgumentException("Temporary regions must preserve focus.", nameof(descriptor));
        }

        var id = new WindowNodeId(_id, checked(++_nextNode));
        var node = new Node(id, parent.Id, descriptor.Role, WindowOwnership.Owned, descriptor.Activation, checked(++_nextOrder))
        {
            Bounds = descriptor.Bounds,
        };
        _nodes.Add(id, node);
        _nativeEvents.Enqueue(() => Publish(node));
        return id;
    }

    /// <summary>Returns an immutable observation of a node.</summary>
    /// <param name="node">The management identifier.</param>
    /// <returns>The current snapshot.</returns>
    public WindowNodeState GetState(WindowNodeId node) => Snapshot(GetNode(node));

    /// <summary>Borrows a native window for backend-specific operations.</summary>
    /// <param name="node">A native management identifier.</param>
    /// <returns>The original native object.</returns>
    public IWindow GetNativeWindow(WindowNodeId node) => GetNode(node).Native
        ?? throw new ArgumentException("Hosted regions do not implement IWindow.", nameof(node));

    /// <summary>Returns back-to-front hosted order for one presentation target.</summary>
    /// <param name="presentationRoot">A native node.</param>
    /// <returns>An immutable sequence of hosted identifiers.</returns>
    public IReadOnlyList<WindowNodeId> GetHostedOrder(WindowNodeId presentationRoot)
    {
        Node root = GetNode(presentationRoot);
        _ = GetNativeWindow(root.Id);
        return Array.AsReadOnly(_nodes.Values.Where(n => n.Native is null && Root(n).Id == root.Id)
            .OrderBy(n => Layer(n.Role)).ThenBy(n => n.Order).Select(n => n.Id).ToArray());
    }

    /// <summary>Finds the frontmost visible region using native client coordinates.</summary>
    /// <param name="presentationRoot">The native presentation target.</param>
    /// <param name="point">A point in logical client coordinates.</param>
    /// <returns>The target, or null outside the visible target.</returns>
    public WindowNodeId? HitTest(WindowNodeId presentationRoot, Vector2 point)
    {
        Node root = GetNode(presentationRoot);
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw new ArgumentException("Point must be finite.", nameof(point));
        }

        if (!Snapshot(root).IsEffectivelyVisible || !Contains(Snapshot(root).Bounds, point))
        {
            return null;
        }

        foreach (WindowNodeId id in GetHostedOrder(presentationRoot).Reverse())
        {
            Node node = GetNode(id);
            if (Snapshot(node).IsEffectivelyVisible && Contains(Clip(node), point))
            {
                return id;
            }
        }

        return root.Id;
    }

    /// <summary>Requests visibility.</summary>
    /// <param name="node">The target.</param>
    /// <returns>The management request identifier.</returns>
    public WindowNodeRequestId Show(WindowNodeId node) => Submit(node, n =>
    {
        n.Visible = true;
        return n.Native?.Show();
    });

    /// <summary>Hides a node and ends temporary child displays.</summary>
    /// <param name="node">The target.</param>
    /// <returns>The management request identifier.</returns>
    public WindowNodeRequestId Hide(WindowNodeId node) => Submit(node, n =>
    {
        n.Visible = false;
        EndTemporaryChildren(n);
        return n.Native?.Hide();
    });

    /// <summary>Activates an eligible native or hosted target.</summary>
    /// <param name="node">The target.</param>
    /// <returns>The management request identifier.</returns>
    public WindowNodeRequestId Activate(WindowNodeId node) => Submit(node, n =>
    {
        if (n.Activation == WindowActivation.PreserveFocus || !Snapshot(n).IsEffectivelyVisible)
        {
            throw new RequestDeniedException("The target cannot receive activation.");
        }

        if (n.Native is { } native)
        {
            return native.Activate();
        }

        if (!Root(n).Native!.State.IsActive)
        {
            throw new RequestDeniedException("Activate the native presentation root first.");
        }

        SetTarget(n);
        return null;
    });

    /// <summary>Raises a native window or a hosted subtree within its layer.</summary>
    /// <param name="node">The target.</param>
    /// <returns>The management request identifier.</returns>
    public WindowNodeRequestId BringToFront(WindowNodeId node) => Submit(node, n =>
    {
        foreach (Node item in Subtree(n).Reverse())
        {
            item.Order = checked(++_nextOrder);
        }

        return n.Native?.BringToFront();
    });

    /// <summary>Places a display beside an anchor, flipping and clamping hosted displays.</summary>
    /// <param name="node">The target.</param>
    /// <param name="placement">An anchor in parent coordinates.</param>
    /// <returns>The management request identifier.</returns>
    public WindowNodeRequestId Place(WindowNodeId node, WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ValidateBounds(placement.Anchor);
        if (!Enum.IsDefined(placement.PreferredSide) || !float.IsFinite(placement.Gap) || placement.Gap < 0)
        {
            throw new ArgumentException("Invalid placement.", nameof(placement));
        }

        return Submit(node, n =>
        {
            if (n.Native is { } native)
            {
                return native.Place(placement);
            }

            n.Placement = placement;
            ApplyPlacement(n);
            return null;
        });
    }

    /// <summary>Changes the logical bounds of a hosted region.</summary>
    /// <param name="node">The hosted target.</param>
    /// <param name="bounds">Positive finite bounds in parent coordinates.</param>
    /// <returns>The management request identifier.</returns>
    public WindowNodeRequestId SetHostedBounds(WindowNodeId node, WindowRect bounds)
    {
        ValidateBounds(bounds);
        if (GetNode(node).Native is not null)
        {
            throw new ArgumentException("Use IWindow.Resize for native windows.", nameof(node));
        }

        return Submit(node, n =>
        {
            n.Bounds = bounds;
            n.Placement = null;
            return null;
        });
    }

    /// <summary>Closes an owned subtree from children to parents.</summary>
    /// <param name="node">The subtree root.</param>
    /// <returns>A request completed after physical closure.</returns>
    public WindowNodeRequestId Close(WindowNodeId node) => Terminate(node, false);

    /// <summary>Detaches a borrowed native subtree, ending owned children first.</summary>
    /// <param name="node">A borrowed native root.</param>
    /// <returns>A request completed after unregistration.</returns>
    public WindowNodeRequestId Detach(WindowNodeId node) => Terminate(node, true);

    /// <summary>Processes observations and management requests without pumping OS events.</summary>
    public void Update()
    {
        CheckMutation();
        _updating = true;
        _errors = [];
        try
        {
            while (_nativeEvents.TryDequeue(out Action? notification))
            {
                Try(notification);
            }

            while (_pending.TryDequeue(out Action? operation))
            {
                Try(operation);
            }

            foreach (Termination termination in _terminations.ToArray())
            {
                Try(() => AdvanceTermination(termination));
            }

            foreach (Node node in _nodes.Values.ToArray())
            {
                if (node.Placement is not null)
                {
                    ApplyPlacement(node);
                }

                Publish(node);
            }

            RefreshTarget();
        }
        finally
        {
            _updating = false;
        }

        ThrowErrors();
    }

    /// <summary>Releases owned nodes and unregisters borrowed ones; failures can be retried.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CheckMutation();
        _updating = true;
        _errors = [];
        try
        {
            foreach (Node node in _nodes.Values.OrderByDescending(Depth).ToArray())
            {
                Try(() =>
                {
                    if (_nodes.Values.Any(n => n.Parent == node.Id))
                    {
                        throw new InvalidOperationException("A child has not been released.");
                    }

                    if (node.Ownership == WindowOwnership.Owned && node.Native is { } native)
                    {
                        native.Dispose();
                    }

                    Remove(node);
                });
            }

            _disposed = _nodes.Count == 0;
            if (_disposed)
            {
                foreach ((WindowNodeRequestId request, WindowNodeId node) in _outstanding.ToArray())
                {
                    Complete(node, request, WindowRequestStatus.Superseded);
                }

                _pending.Clear();
                _nativeEvents.Clear();
                _terminations.Clear();
            }
        }
        finally
        {
            _updating = false;
        }

        ThrowErrors();
    }

    private static int Layer(WindowRole role) => role switch
    {
        WindowRole.Popup or WindowRole.ContextMenu => 1,
        WindowRole.ImeCandidates => 2,
        _ => 0,
    };

    private static bool IsTemporary(WindowRole role) => Layer(role) > 0;

    private static void ValidateRole(WindowRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }
    }

    private static void ValidateNativeRole(WindowRole role, WindowKind kind, WindowActivation activation)
    {
        WindowKind expected = role switch
        {
            WindowRole.Normal => WindowKind.Normal,
            WindowRole.Tool => WindowKind.Tool,
            _ => WindowKind.Popup,
        };
        if (kind != expected || (IsTemporary(role) && activation != WindowActivation.PreserveFocus))
        {
            throw new ArgumentException("Native kind and activation must match the management role.", nameof(role));
        }
    }

    private static void ValidateBounds(WindowRect bounds)
    {
        if (!float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) || !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height)
            || bounds.Width <= 0 || bounds.Height <= 0 || !float.IsFinite(bounds.X + bounds.Width) || !float.IsFinite(bounds.Y + bounds.Height))
        {
            throw new ArgumentOutOfRangeException(nameof(bounds));
        }
    }

    private static bool Contains(WindowRect rect, Vector2 point) => rect.Width > 0 && rect.Height > 0
        && point.X >= rect.X && point.Y >= rect.Y && point.X < rect.X + rect.Width && point.Y < rect.Y + rect.Height;

    private static WindowRect Intersect(WindowRect a, WindowRect b)
    {
        float x = Math.Max(a.X, b.X);
        float y = Math.Max(a.Y, b.Y);
        return new WindowRect(x, y, Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - x), Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - y));
    }

    private Node GetNode(WindowNodeId id)
    {
        Check();
        if (id.SystemId != _id || id.Value == 0)
        {
            throw new ArgumentException("Identifier belongs to another system.", nameof(id));
        }

        return _nodes.TryGetValue(id, out Node? node) ? node : throw new KeyNotFoundException("Node is no longer registered.");
    }

    private Node Root(Node node) => node.Native is not null ? node : Root(_nodes[node.Parent!.Value]);

    private int Depth(Node node) => node.Parent is { } id ? 1 + Depth(_nodes[id]) : 0;

    private Node[] Subtree(Node node) => _nodes.Values.Where(n => IsDescendant(n, node)).OrderByDescending(Depth).ToArray();

    private bool IsDescendant(Node candidate, Node ancestor) => candidate.Id == ancestor.Id
        || (candidate.Parent is { } parent && IsDescendant(_nodes[parent], ancestor));

    private WindowRect AbsoluteBounds(Node node)
    {
        WindowRect bounds = Snapshot(node).Bounds;
        if (node.Native is not null)
        {
            return bounds;
        }

        WindowRect parent = AbsoluteBounds(_nodes[node.Parent!.Value]);
        return bounds with { X = bounds.X + parent.X, Y = bounds.Y + parent.Y };
    }

    private WindowRect Clip(Node node)
    {
        WindowRect bounds = AbsoluteBounds(node);
        return node.Native is not null ? bounds : Intersect(bounds, Clip(_nodes[node.Parent!.Value]));
    }

    private WindowNodeState Snapshot(Node node)
    {
        Node root = Root(node);
        WindowState native = root.Observed!;
        WindowRect bounds = node.Native is not null ? new WindowRect(0, 0, native.ClientSize.X, native.ClientSize.Y) : node.Bounds;
        bool visible = node.Observed?.IsVisible ?? node.Visible;
        bool effective = visible && native.Lifetime == WindowLifetime.Open && native.DisplayMode != WindowDisplayMode.Minimized;
        if (node.Native is null)
        {
            effective &= Snapshot(_nodes[node.Parent!.Value]).IsEffectivelyVisible;
        }

        return new WindowNodeState
        {
            Id = node.Id,
            Parent = node.Parent,
            PresentationRoot = root.Id,
            Kind = node.Native is null ? WindowNodeKind.Hosted : WindowNodeKind.Native,
            Role = node.Role,
            Ownership = node.Ownership,
            NativeWindow = node.Native?.Id,
            Bounds = bounds,
            ClientSize = new Vector2(bounds.Width, bounds.Height),
            PixelSize = node.Observed?.PixelSize ?? new WindowPixelSize((int)Math.Ceiling(bounds.Width * native.Scale.X), (int)Math.Ceiling(bounds.Height * native.Scale.Y)),
            Scale = native.Scale,
            IsVisible = visible,
            IsEffectivelyVisible = effective,
            Lifetime = node.Observed?.Lifetime ?? WindowLifetime.Open,
        };
    }

    private WindowNodeRequestId Submit(WindowNodeId id, Func<Node, WindowRequestId?> operation)
    {
        CheckMutation();
        Node node = GetNode(id);
        if (Snapshot(node).Lifetime != WindowLifetime.Open || _terminations.Any(t => t.Nodes.Contains(id)))
        {
            throw new InvalidOperationException("The target is closing.");
        }

        WindowNodeRequestId request = Issue(id);
        _pending.Enqueue(() =>
        {
            if (!_nodes.ContainsKey(id))
            {
                Complete(id, request, WindowRequestStatus.Superseded);
                return;
            }

            try
            {
                WindowRequestId? nativeRequest = operation(node);
                if (nativeRequest is { } nativeId)
                {
                    _requests.Add((node.Native!.Id, nativeId), (id, request));
                }
                else
                {
                    Complete(id, request, WindowRequestStatus.Applied);
                }

                Publish(node);
            }
            catch (RequestDeniedException ex)
            {
                Complete(id, request, WindowRequestStatus.Denied, ex.Message);
            }
            catch (Exception ex)
            {
                Complete(id, request, WindowRequestStatus.Failed, ex.Message);
            }
        });
        return request;
    }

    private WindowNodeRequestId Terminate(WindowNodeId id, bool detach)
    {
        CheckMutation();
        Node node = GetNode(id);
        Node[] subtree = Subtree(node);
        if (detach ? node.Native is null || node.Ownership != WindowOwnership.Borrowed : subtree.Any(n => n.Native is not null && n.Ownership == WindowOwnership.Borrowed))
        {
            throw new InvalidOperationException("Borrowed windows must be detached by their owner.");
        }

        if (_terminations.Any(t => t.Nodes.Any(x => subtree.Any(n => n.Id == x))))
        {
            throw new InvalidOperationException("A termination is already pending.");
        }

        WindowNodeRequestId request = Issue(id);
        var termination = new Termination(id, request, detach, subtree.Select(n => n.Id).ToArray());
        _terminations.Add(termination);
        return request;
    }

    private void AdvanceTermination(Termination termination)
    {
        foreach (WindowNodeId id in termination.Nodes)
        {
            if (!_nodes.TryGetValue(id, out Node? node))
            {
                continue;
            }

            if (_nodes.Values.Any(n => n.Parent == id))
            {
                return;
            }

            if (node.Native is { } native && !(termination.Detach && node.Ownership == WindowOwnership.Borrowed))
            {
                if (native.State.Lifetime == WindowLifetime.Open && node.CloseRequest is null)
                {
                    node.CloseRequest = native.Close();
                }

                if (native.State.Lifetime != WindowLifetime.Closed)
                {
                    return;
                }

                native.Dispose();
            }

            Remove(node);
        }

        _terminations.Remove(termination);
        Complete(termination.Root, termination.Request, WindowRequestStatus.Applied);
    }

    private void NativeCompleted(WindowRequestResult result)
    {
        if (_requests.Remove((result.Window, result.Request), out (WindowNodeId, WindowNodeRequestId) request))
        {
            Complete(request.Item1, request.Item2, result.Status, result.Error);
        }

        Node? node = _nodes.Values.FirstOrDefault(n => n.Native?.Id == result.Window);
        if (node is not null && node.CloseRequest == result.Request && result.Status != WindowRequestStatus.Applied)
        {
            node.CloseRequest = null;
            Termination? termination = _terminations.FirstOrDefault(t => t.Nodes.Contains(node.Id));
            if (termination is not null)
            {
                _terminations.Remove(termination);
                Complete(termination.Root, termination.Request, result.Status, result.Error);
            }
        }
    }

    private void NativeChanged(Node node, WindowState observed)
    {
        if (!_nodes.ContainsKey(node.Id))
        {
            return;
        }

        node.Observed = observed;
        WindowNodeState state = Snapshot(node);
        bool deactivated = node.WasActive && !observed.IsActive;
        node.WasActive = observed.IsActive;
        if ((node.Last?.IsEffectivelyVisible == true && !state.IsEffectivelyVisible) || deactivated)
        {
            EndTemporaryChildren(node);
        }

        if (state.Lifetime == WindowLifetime.Closed && !_terminations.Any(t => t.Nodes.Contains(node.Id)))
        {
            foreach (Node child in Subtree(node))
            {
                if (child.Native is null)
                {
                    Remove(child);
                }
            }

            if (!_nodes.Values.Any(n => n.Parent == node.Id))
            {
                if (node.Ownership == WindowOwnership.Owned)
                {
                    node.Native!.Dispose();
                }

                Remove(node);
                ReapClosedAncestors(node.Parent);
                return;
            }
        }

        Publish(node);
    }

    private void UserClose(Node node)
    {
        if (!_nodes.ContainsKey(node.Id))
        {
            return;
        }

        if (node.Ownership == WindowOwnership.Borrowed || Subtree(node).Any(n => n.Ownership == WindowOwnership.Borrowed))
        {
            Notify(CloseRequested, node.Id);
            return;
        }

        if (!_terminations.Any(t => t.Nodes.Contains(node.Id)))
        {
            WindowNodeRequestId request = Issue(node.Id);
            _terminations.Add(new Termination(node.Id, request, false, Subtree(node).Select(n => n.Id).ToArray()));
        }
    }

    private void EndTemporaryChildren(Node parent)
    {
        foreach (Node child in Subtree(parent).Where(n => n.Id != parent.Id && IsTemporary(n.Role)).ToArray())
        {
            if (_terminations.Any(t => t.Nodes.Contains(child.Id)))
            {
                continue;
            }

            if (child.Ownership == WindowOwnership.Borrowed)
            {
                child.Native!.Hide();
                Notify(CloseRequested, child.Id);
            }
            else if (Subtree(child).All(n => n.Ownership == WindowOwnership.Owned))
            {
                WindowNodeRequestId request = Issue(child.Id);
                _terminations.Add(new Termination(child.Id, request, false, Subtree(child).Select(n => n.Id).ToArray()));
            }
        }
    }

    private void ApplyPlacement(Node node)
    {
        WindowPlacement placement = node.Placement!;
        WindowRect parent = AbsoluteBounds(_nodes[node.Parent!.Value]);
        WindowRect clip = Clip(_nodes[node.Parent.Value]);
        WindowRect anchor = placement.Anchor with { X = placement.Anchor.X + parent.X, Y = placement.Anchor.Y + parent.Y };
        Vector2 Position(WindowPlacementSide side) => side switch
        {
            WindowPlacementSide.Below => new(anchor.X, anchor.Y + anchor.Height + placement.Gap),
            WindowPlacementSide.Above => new(anchor.X, anchor.Y - node.Bounds.Height - placement.Gap),
            WindowPlacementSide.Right => new(anchor.X + anchor.Width + placement.Gap, anchor.Y),
            _ => new(anchor.X - node.Bounds.Width - placement.Gap, anchor.Y),
        };
        bool Fits(Vector2 point) => point.X >= clip.X && point.Y >= clip.Y
            && point.X + node.Bounds.Width <= clip.X + clip.Width && point.Y + node.Bounds.Height <= clip.Y + clip.Height;
        Vector2 position = Position(placement.PreferredSide);
        if (!Fits(position))
        {
            WindowPlacementSide opposite = placement.PreferredSide switch
            {
                WindowPlacementSide.Below => WindowPlacementSide.Above,
                WindowPlacementSide.Above => WindowPlacementSide.Below,
                WindowPlacementSide.Left => WindowPlacementSide.Right,
                _ => WindowPlacementSide.Left,
            };
            position = Position(opposite);
        }

        position.X = Math.Clamp(position.X, clip.X, Math.Max(clip.X, clip.X + clip.Width - node.Bounds.Width));
        position.Y = Math.Clamp(position.Y, clip.Y, Math.Max(clip.Y, clip.Y + clip.Height - node.Bounds.Height));
        node.Bounds = node.Bounds with { X = position.X - parent.X, Y = position.Y - parent.Y };
    }

    private void SetTarget(Node node)
    {
        _keyboardTarget = node.Id;
        _lastTargets[Root(node).Id] = node.Id;
    }

    private void RefreshTarget()
    {
        Node? active = _nodes.Values.FirstOrDefault(n => n.Observed?.IsActive == true && Snapshot(n).IsEffectivelyVisible);
        if (active is null)
        {
            _keyboardTarget = null;
            return;
        }

        if (_keyboardTarget is { } current && _nodes.TryGetValue(current, out Node? target)
            && Root(target).Id == active.Id && Snapshot(target).IsEffectivelyVisible
            && target.Activation != WindowActivation.PreserveFocus)
        {
            return;
        }

        if (_lastTargets.TryGetValue(active.Id, out WindowNodeId last) && _nodes.TryGetValue(last, out Node? previous)
            && Snapshot(previous).IsEffectivelyVisible && previous.Activation != WindowActivation.PreserveFocus)
        {
            SetTarget(previous);
        }
        else
        {
            SetTarget(active);
        }
    }

    private void Remove(Node node)
    {
        if (node.Native is { } native)
        {
            native.StateChanged -= node.StateHandler;
            native.RequestCompleted -= node.ResultHandler;
            native.Closing -= node.ClosingHandler;
            _registrations.Remove(native);
        }

        if (_keyboardTarget == node.Id)
        {
            _keyboardTarget = ClosestFocusableAncestor(node.Parent);
        }

        foreach ((WindowId, WindowRequestId) key in _requests.Where(x => x.Value.Item1 == node.Id).Select(x => x.Key).ToArray())
        {
            (WindowNodeId, WindowNodeRequestId) request = _requests[key];
            _requests.Remove(key);
            Complete(node.Id, request.Item2, WindowRequestStatus.Superseded);
        }

        _nodes.Remove(node.Id);
        Notify(Removed, node.Id);
    }

    private WindowNodeId? ClosestFocusableAncestor(WindowNodeId? ancestor)
    {
        while (ancestor is { } id && _nodes.TryGetValue(id, out Node? node))
        {
            if (node.Activation != WindowActivation.PreserveFocus && Snapshot(node).IsEffectivelyVisible)
            {
                return id;
            }

            ancestor = node.Parent;
        }

        return null;
    }

    private void ReapClosedAncestors(WindowNodeId? ancestor)
    {
        while (ancestor is { } id && _nodes.TryGetValue(id, out Node? node)
            && node.Observed?.Lifetime == WindowLifetime.Closed
            && !_nodes.Values.Any(child => child.Parent == id)
            && !_terminations.Any(termination => termination.Nodes.Contains(id)))
        {
            ancestor = node.Parent;
            if (node.Ownership == WindowOwnership.Owned)
            {
                node.Native!.Dispose();
            }

            Remove(node);
        }
    }

    private void Publish(Node node)
    {
        if (!_nodes.ContainsKey(node.Id))
        {
            return;
        }

        WindowNodeState snapshot = Snapshot(node);
        if (snapshot != node.Last)
        {
            node.Last = snapshot;
            Notify(Changed, snapshot);
        }
    }

    private WindowNodeRequestId Issue(WindowNodeId node)
    {
        var request = new WindowNodeRequestId(checked(++_nextRequest));
        _outstanding.Add(request, node);
        return request;
    }

    private void Complete(WindowNodeId node, WindowNodeRequestId request, WindowRequestStatus status, string? error = null)
    {
        if (_outstanding.Remove(request))
        {
            Notify(RequestCompleted, new WindowNodeRequestResult(node, request, status, error));
        }
    }

    private void Notify<T>(Action<T>? handlers, T value)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Action<T> handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            Try(() => handler(value));
        }
    }

    private void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            (_errors ??= []).Add(ex);
        }
    }

    private void ThrowErrors()
    {
        List<Exception>? errors = _errors;
        _errors = null;
        if (errors?.Count > 0)
        {
            throw new AggregateException(errors);
        }
    }

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread)
        {
            throw new InvalidOperationException("Use the creating thread.");
        }
    }

    private void CheckMutation()
    {
        Check();
        if (_updating)
        {
            throw new InvalidOperationException("Reentrant modification is forbidden.");
        }
    }

    private sealed class Node(WindowNodeId id, WindowNodeId? parent, WindowRole role, WindowOwnership ownership, WindowActivation activation, ulong order)
    {
        public WindowNodeId Id { get; } = id;

        public WindowNodeId? Parent { get; } = parent;

        public WindowRole Role { get; } = role;

        public WindowOwnership Ownership { get; } = ownership;

        public WindowActivation Activation { get; } = activation;

        public ulong Order { get; set; } = order;

        public IWindow? Native { get; init; }

        public WindowState? Observed { get; set; }

        public bool WasActive { get; set; }

        public WindowRect Bounds { get; set; }

        public bool Visible { get; set; }

        public WindowPlacement? Placement { get; set; }

        public WindowRequestId? CloseRequest { get; set; }

        public WindowNodeState? Last { get; set; }

        public Action<WindowState>? StateHandler { get; set; }

        public Action<WindowRequestResult>? ResultHandler { get; set; }

        public EventHandler<WindowClosingEventArgs>? ClosingHandler { get; set; }
    }

    private sealed class RequestDeniedException(string message) : Exception(message);

    private sealed record Termination(WindowNodeId Root, WindowNodeRequestId Request, bool Detach, WindowNodeId[] Nodes);
}
