using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;

namespace Lumyte.UI.Windowing;

internal sealed class UiWindowManagerCore(TimeProvider clock, IUiNativeWindowBackend? native = null, string unavailableCode = "native-unavailable") : IUiWindowManager
{
    private readonly Dictionary<UiWindowId, Node> _nodes = [];
    private readonly Dictionary<UiWindowId, UiWindowId> _keyboardTargets = [];

    private readonly Dictionary<UiWindowRequestId, UiWindowRequestResult> _requests = [];

    private ulong _next;

    private ulong _nextRequest;

    private bool _stopping;

    private bool _disposed;

    private bool _advancing;

    public Guid Id { get; } = Guid.NewGuid();

    public UiWindowPresentations SupportedPresentations => native is null ? UiWindowPresentations.Hosted : UiWindowPresentations.Native;

    public bool IsStopped => _stopping && _nodes.Values.All(n => n.Terminal && n.Surface is null && n.Loans == 0 && (n.Registration is null || n.Registration.IsReleased));

    public UiWindowRegistration Register(IUiWindowSource source, UiWindowId? keyboardRoot = null)
    {
        CheckAdmission();
        ArgumentNullException.ThrowIfNull(source);
        UiWindowSourceSnapshot state = source.CaptureState();
        ValidateState(state);
        if (source.SourceId == Guid.Empty || state.IsClosed || _nodes.Values.Any(n => n.Source?.SourceId == source.SourceId))
        {
            throw new ArgumentException("Invalid or duplicate presentation source.", nameof(source));
        }

        Node? keyboard = keyboardRoot is { } key ? Get(key) : null;
        if (keyboard is not null && (keyboard.Terminal || keyboard.State.KeyboardScopeId != state.KeyboardScopeId))
        {
            throw new ArgumentException("Keyboard root must share the live scope.", nameof(keyboardRoot));
        }

        UiWindowId id = Next();
        var registration = new UiWindowRegistration(id, () => Unregister(id));
        var node = new Node(id, null, UiWindowRole.Normal, UiWindowInputMode.Interactive)
        {
            Source = source,
            Registration = registration,
            Presentation = UiWindowPresentation.External,
            Root = id,
            Keyboard = keyboard?.Keyboard ?? id,
            State = state,
            Phase = UiWindowState.Ready,
        };
        _nodes.Add(id, node);
        Refresh(node);
        return registration;
    }

    public UiWindowId Create(UiWindowCreateRequest request)
    {
        CheckAdmission();
        ArgumentNullException.ThrowIfNull(request);
        Node owner = Get(request.Owner);
        ValidateRequest(request);
        if (owner.Terminal || owner.Phase == UiWindowState.Closing)
        {
            throw new ArgumentException("Owner is closing.", nameof(request));
        }

        UiWindowId id = Next();
        var node = new Node(id, request.Owner, request.Role, request.InputMode)
        {
            Create = request,
            Placement = request.Placement,
            Root = owner.Root,
            Keyboard = owner.Keyboard,
        };
        _nodes.Add(id, node);
        return id;
    }

    public ValueTask<UiWindowBinding> BindPresentationAsync(UiWindowId window, UiSurface surface, CancellationToken cancellationToken = default)
    {
        CheckAdmission();
        ArgumentNullException.ThrowIfNull(surface);
        Node node = Get(window);
        if (node.Surface is not null || node.Terminal || node.Phase == UiWindowState.Closing || _nodes.Values.Any(n => ReferenceEquals(n.Surface, surface)))
        {
            throw new InvalidOperationException("Window or surface is already bound or closing.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        node.Surface = surface;
        node.Bind = new();
        node.Retired = new();
        node.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        node.Phase = UiWindowState.Preparing;
        node.PreparationElapsed = TimeSpan.Zero;
        node.Started = clock.GetTimestamp();
        return new(node.Bind.Task);
    }

    public bool IsBound(UiWindowId window, UiSurface surface)
    {
        Check();
        return ReferenceEquals(Get(window).Surface, surface);
    }

    public UiWindowSourceLease BorrowPresentationSource(UiWindowId window)
    {
        Check();
        Node node = Get(window);
        Node root = Get(node.Root);
        if (node.Phase is not (UiWindowState.Ready or UiWindowState.Open) || root.Source is null || root.Terminal || root.Phase == UiWindowState.Closing)
        {
            throw new InvalidOperationException("Presentation source is unavailable.");
        }

        checked
        {
            root.Loans++;
        }

        return new(root.Source, root.Id, node.Keyboard, () =>
        {
            Check();
            root.Loans--;
        });
    }

    public UiNativeWindowSupport GetNativeSupport(UiWindowId owner)
    {
        Check();
        Node node = Get(owner);
        return native is not null && Get(node.Root).Source is { } source ? native.GetSupport(source) : new(false, unavailableCode);
    }

    public UiWindowRequestId Place(UiWindowId window, UiWindowPlacement placement)
    {
        Check();
        Node node = Owned(window);
        ValidatePlacement(placement);
        if (node.Terminal || node.Phase == UiWindowState.Closing)
        {
            throw new InvalidOperationException("Window is closing.");
        }

        if (node.PlaceRequest is { } old)
        {
            Complete(old, "superseded");
        }

        node.Placement = placement;
        node.PlaceRequest = Issue(window);
        node.NativePlace = null;
        return node.PlaceRequest.Value;
    }

    public UiWindowRequestId Resize(UiWindowId window, Vector2 clientSize)
    {
        Check();
        if (!float.IsFinite(clientSize.X) || !float.IsFinite(clientSize.Y) || clientSize.X <= 0 || clientSize.Y <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clientSize));
        }

        Node node = Get(window);
        UiWindowRequestId request = Issue(window);
        if (node.Presentation != UiWindowPresentation.Hosted || node.Role is not (UiWindowRole.Normal or UiWindowRole.Tool) || node.Terminal || node.Retiring)
        {
            Complete(request, "unsupported");
            return request;
        }

        if (node.ResizeRequest is { } previous)
        {
            Complete(previous, "superseded");
        }

        node.RequestedSize = clientSize;
        node.ResizeRequest = request;
        return request;
    }

    public UiWindowRequestId BringToFront(UiWindowId window)
    {
        Check();
        Node node = Get(window);
        UiWindowRequestId request = Issue(window);
        if (node.Presentation != UiWindowPresentation.Hosted || node.Role is not (UiWindowRole.Normal or UiWindowRole.Tool) || node.Terminal || node.Retiring)
        {
            Complete(request, "unsupported");
            return request;
        }

        if (node.StackingRequest is { } previous)
        {
            Complete(previous, "superseded");
        }

        node.StackingOrder = checked(_nodes.Values.Max(current => Math.Max(current.StackingOrder, current.Id.Value)) + 1);
        node.StackingRequest = request;
        return request;
    }

    public UiWindowRequestId Show(UiWindowId window)
    {
        Check();
        Node node = Owned(window);
        if (node.Phase != UiWindowState.Ready || node.Surface is null || node.Retiring || node.ShowIssued)
        {
            throw new InvalidOperationException("Show requires a bound hidden Ready window.");
        }

        node.ShowIssued = true;
        node.ShowRequest = Issue(window);
        node.Started = clock.GetTimestamp();
        return node.ShowRequest.Value;
    }

    public UiWindowRequestId Close(UiWindowId window)
    {
        Check();
        Node node = Owned(window);
        return RequestClose(node);
    }

    public void Release(UiWindowId window)
    {
        Check();
        Node node = Owned(window);
        if (!node.Terminal || node.Surface is not null || node.Loans != 0 || _nodes.Values.Any(n => n.Owner == window))
        {
            throw new InvalidOperationException("Release requires a drained terminal window with no children.");
        }

        Remove(node);
    }

    public bool TryGetRequest(UiWindowRequestId request, out UiWindowRequestResult? result)
    {
        Check();
        if (request.ManagerId != Id || request.Value == 0)
        {
            throw new ArgumentException("Foreign request.", nameof(request));
        }

        return _requests.TryGetValue(request, out result);
    }

    public bool ReleaseRequest(UiWindowRequestId request) => TryGetRequest(request, out UiWindowRequestResult? result) && result is { Status: not UiWindowRequestStatus.Pending } && _requests.Remove(request);

    public bool TryGetSnapshot(UiWindowId window, out UiWindowSnapshot? snapshot)
    {
        Check();
        ValidateId(window);
        snapshot = _nodes.TryGetValue(window, out Node? node) ? Snapshot(node) : null;
        return snapshot is not null;
    }

    public IReadOnlyList<UiWindowSnapshot> CaptureWindows()
    {
        Check();
        return Array.AsReadOnly(_nodes.Values.Select(Snapshot).ToArray());
    }

    public UiWindowId? ResolveInputRoot(InputWindowId origin)
    {
        Check();
        return _nodes.Values.FirstOrDefault(n => n.Root == n.Id && n.Phase == UiWindowState.Open && n.State.InputOrigin == origin)?.Id;
    }

    public UiWindowId? GetKeyboardTarget(UiWindowId window)
    {
        Check();
        Node node = Get(window);
        if (!_nodes.TryGetValue(node.Keyboard, out Node? root) || !root.State.IsActive || !root.State.IsVisible || root.State.IsClosed)
        {
            return null;
        }

        return _keyboardTargets.TryGetValue(node.Keyboard, out UiWindowId target) && _nodes.TryGetValue(target, out Node? selected) && selected.Phase == UiWindowState.Open && selected.State.IsVisible ? target : root.Id;
    }

    public void SetKeyboardTarget(UiWindowId window)
    {
        Check();
        Node node = Get(window);
        if (node.Phase != UiWindowState.Open || node.Role is not (UiWindowRole.Normal or UiWindowRole.Tool) || node.Mode != UiWindowInputMode.Interactive || !node.State.IsVisible || node.Surface is null)
        {
            throw new InvalidOperationException("Keyboard selection requires a visible bound normal window.");
        }

        _keyboardTargets[node.Keyboard] = window;
    }

    public UiWindowHit? HitTest(UiWindowId presentationRoot, Vector2 position)
    {
        Check();
        _ = Get(presentationRoot);
        foreach (Node node in _nodes.Values.Where(n => n.Root == presentationRoot).OrderByDescending(n => UiWindowStacking.Key(n.Role, n.Id, n.Committed?.StackingOrder ?? 0)))
        {
            if (node.Mode != UiWindowInputMode.Passive && node.Phase == UiWindowState.Open && node.State.IsVisible && node.State.ClientSize.X > 0 && node.State.ClientSize.Y > 0 && node.Committed is { } view && view.IsVisible && Contains(view.ClipInPresentation, position))
            {
                return new(node.Id, position - new Vector2(view.BoundsInPresentation.X, view.BoundsInPresentation.Y), view.GeometryRevision, node.Mode);
            }
        }

        return null;
    }

    public bool TryTransformPoint(UiWindowId from, UiWindowId to, Vector2 position, out Vector2 transformed)
    {
        Check();
        Node a = Get(from);
        Node b = Get(to);
        Vector2 offsetA = Origin(a);
        Vector2 offsetB = Origin(b);
        if (a.Root == b.Root)
        {
            transformed = position + offsetA - offsetB;
            return true;
        }

        if (Get(a.Root).Source is { } source && Get(b.Root).Source is { } destination && source.TryTransformPoint(destination, position + offsetA, out Vector2 point))
        {
            transformed = point - offsetB;
            return true;
        }

        transformed = default;
        return false;
    }

    public void Advance()
    {
        Check();
        if (_advancing)
        {
            throw new InvalidOperationException("Advance cannot be reentered.");
        }

        _advancing = true;
        var failures = new List<Exception>();
        try
        {
            foreach (Node node in _nodes.Values.OrderBy(n => n.Id.Value).ToArray())
            {
                try
                {
                    Step(node);
                }
                catch (Exception error)
                {
                    Fail(node, error is UiWindowOperationException operation ? operation.Code : "presentation-failed", error);
                }
            }

            foreach (Node node in _nodes.Values.OrderByDescending(n => n.Id.Value).ToArray())
            {
                try
                {
                    Drain(node);
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
            }
        }
        finally
        {
            _advancing = false;
        }

        if (failures.Count != 0)
        {
            throw new AggregateException(failures);
        }
    }

    public void BeginStop()
    {
        Check();
        _stopping = true;
        foreach (Node node in _nodes.Values.ToArray())
        {
            RequestClose(node);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        BeginStop();
        if (!IsStopped)
        {
            throw new InvalidOperationException("Continue Advance until IsStopped before disposal.");
        }

        _nodes.Clear();
        _requests.Clear();
        _disposed = true;
    }

    internal static Rect2D Intersect(Rect2D a, Rect2D b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X)), Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y)));

    internal static bool Contains(Rect2D r, Vector2 p) => r.Width > 0 && r.Height > 0 && p.X >= r.X && p.Y >= r.Y && p.X < (double)r.X + r.Width && p.Y < (double)r.Y + r.Height;

    private static void Commit(Node node)
    {
        if (node.Desired is { } next && node.Token!.PresentedGeometryRevision >= next.GeometryRevision)
        {
            node.Committed = next;
        }
    }

    private static Vector2 PlacePoint(Rect2D a, Vector2 size, Vector2 limit, UiWindowPlacementSide side, float gap)
    {
        Vector2 Point(UiWindowPlacementSide s) => s switch
        {
            UiWindowPlacementSide.Below => new(a.X, a.Y + a.Height + gap),
            UiWindowPlacementSide.Above => new(a.X, a.Y - size.Y - gap),
            UiWindowPlacementSide.Right => new(a.X + a.Width + gap, a.Y),
            _ => new(a.X - size.X - gap, a.Y),
        };
        Vector2 p = Point(side);
        if (p.X < 0 || p.Y < 0 || p.X + size.X > limit.X || p.Y + size.Y > limit.Y)
        {
            UiWindowPlacementSide opposite = side switch
            {
                UiWindowPlacementSide.Below => UiWindowPlacementSide.Above,
                UiWindowPlacementSide.Above => UiWindowPlacementSide.Below,
                UiWindowPlacementSide.Right => UiWindowPlacementSide.Left,
                _ => UiWindowPlacementSide.Right,
            };
            p = Point(opposite);
        }

        return Vector2.Clamp(p, Vector2.Zero, Vector2.Max(Vector2.Zero, limit - size));
    }

    private static Vector2 Origin(Node n) => n.Committed is { } v ? new(v.BoundsInPresentation.X, v.BoundsInPresentation.Y) : Vector2.Zero;

    private static void ValidateState(UiWindowSourceSnapshot s)
    {
        if (!Finite(s.ClientSize) || s.ClientSize.X < 0 || s.ClientSize.Y < 0 || !Finite(s.Scale) || s.Scale.X <= 0 || s.Scale.Y <= 0 || s.KeyboardScopeId == Guid.Empty || (s.InputOrigin is { Value: var id } && id == Guid.Empty))
        {
            throw new ArgumentException("Invalid source observation.");
        }
    }

    private static bool Finite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);

    private void ValidatePlacement(UiWindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        _ = Get(placement.AnchorWindow);
        Rect2D r = placement.Anchor;
        if (!float.IsFinite(r.X) || !float.IsFinite(r.Y) || !float.IsFinite(r.Width) || !float.IsFinite(r.Height) || r.Width < 0 || r.Height < 0 || !float.IsFinite(placement.Gap) || placement.Gap < 0 || placement.PreferredSide is < UiWindowPlacementSide.Below or > UiWindowPlacementSide.Left)
        {
            throw new ArgumentException("Invalid placement.", nameof(placement));
        }
    }

    private void ValidateRequest(UiWindowCreateRequest r)
    {
        if (!Finite(r.ClientSize) || r.ClientSize.X <= 0 || r.ClientSize.Y <= 0 || r.Role is not (UiWindowRole.Normal or UiWindowRole.Tool or UiWindowRole.Popup or UiWindowRole.ContextMenu or UiWindowRole.ImeCandidates) || r.InputMode is < UiWindowInputMode.Interactive or > UiWindowInputMode.Candidate || r.Policy is < UiWindowPolicy.HostedOnly or > UiWindowPolicy.RequireNative || (r.Role == UiWindowRole.ImeCandidates) != (r.InputMode == UiWindowInputMode.Candidate))
        {
            throw new ArgumentException("Invalid window request.", nameof(r));
        }

        ValidatePlacement(r.Placement);
        if (Get(r.Owner).Keyboard != Get(r.Placement.AnchorWindow).Keyboard)
        {
            throw new ArgumentException("Anchor must share the owner's keyboard root.", nameof(r));
        }
    }

    private UiWindowId Next() => new(Id, checked(++_next));

    private void ValidateId(UiWindowId id)
    {
        if (id.ManagerId != Id || id.Value == 0)
        {
            throw new ArgumentException("Foreign or default window identity.", nameof(id));
        }
    }

    private Node Get(UiWindowId id)
    {
        ValidateId(id);
        return _nodes.TryGetValue(id, out Node? node) ? node : throw new ArgumentException("Unknown window.", nameof(id));
    }

    private Node Owned(UiWindowId id)
    {
        Node node = Get(id);
        return node.Registration is null ? node : throw new InvalidOperationException("Borrowed roots cannot be placed, shown or closed.");
    }

    private void Check() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void CheckAdmission()
    {
        Check();
        if (_stopping)
        {
            throw new InvalidOperationException("The manager is stopping.");
        }
    }

    private UiWindowRequestId Issue(UiWindowId window)
    {
        var id = new UiWindowRequestId(Id, checked(++_nextRequest));
        _requests.Add(id, new(id, window, UiWindowRequestStatus.Pending, string.Empty));
        return id;
    }

    private void Complete(UiWindowRequestId id, string code = "")
    {
        if (_requests.TryGetValue(id, out UiWindowRequestResult? result))
        {
            _requests[id] = result with
            {
                Status = code.Length == 0 ? UiWindowRequestStatus.Succeeded : UiWindowRequestStatus.Failed,
                Code = code,
            };
        }
    }

    private UiWindowRequestId RequestClose(Node node)
    {
        if (node.CloseRequest is { } existing && _requests.ContainsKey(existing))
        {
            return existing;
        }

        node.CloseRequest = Issue(node.Id);
        if (node.Terminal)
        {
            Complete(node.CloseRequest.Value);
            return node.CloseRequest.Value;
        }

        node.Phase = UiWindowState.Closing;
        try
        {
            node.Cancellation?.Cancel();
        }
        catch (Exception error)
        {
            node.Error ??= error;
            node.Failure = "presentation-failed";
        }

        foreach (Node child in _nodes.Values.Where(n => n.Owner == node.Id).ToArray())
        {
            RequestClose(child);
        }

        return node.CloseRequest.Value;
    }

    private void Unregister(UiWindowId id)
    {
        Check();
        if (_nodes.TryGetValue(id, out Node? node))
        {
            RequestClose(node);
        }
    }

    private ValueTask Retire(Node node)
    {
        if (node.Retired is { Task.IsCompleted: true } completed)
        {
            return new(completed.Task);
        }

        Check();
        node.Retiring = true;
        return node.Retired is { } tcs ? new(tcs.Task) : ValueTask.CompletedTask;
    }

    private void Fail(Node node, string code, Exception? error = null)
    {
        if (node.Terminal)
        {
            return;
        }

        node.Failure = code;
        node.Error ??= error;
        RequestClose(node);
    }

    private void Refresh(Node node)
    {
        if (node.Source is not null)
        {
            UiWindowSourceSnapshot state = node.Source.CaptureState();
            ValidateState(state);
            node.State = state;
        }
    }

    private void Step(Node node)
    {
        if (node.Terminal || node.Phase == UiWindowState.Closing)
        {
            return;
        }

        if (node.Owner is { } parent && (Get(parent).Phase == UiWindowState.Closing || Get(parent).Terminal || !Get(parent).State.IsVisible || Get(parent).State.ClientSize.X <= 0 || Get(parent).State.ClientSize.Y <= 0))
        {
            Fail(node, "owner-unavailable");
            return;
        }

        Refresh(node);
        if (node.Source is not null && (node.State.IsClosed || (node.Registration is null && node.Phase == UiWindowState.Open && (!node.State.IsVisible || node.State.ClientSize.X == 0 || node.State.ClientSize.Y == 0))))
        {
            Fail(node, "owner-unavailable");
            return;
        }

        if (node.Surface is null)
        {
            return;
        }

        if (node.Cancellation!.IsCancellationRequested)
        {
            RequestClose(node);
            return;
        }

        if ((node.Phase == UiWindowState.Preparing || (node.ShowIssued && node.Phase != UiWindowState.Open)) && clock.GetElapsedTime(node.Started) + (node.Phase == UiWindowState.Preparing ? TimeSpan.Zero : node.PreparationElapsed) >= TimeSpan.FromSeconds(2))
        {
            Fail(node, "opening-timeout");
            return;
        }

        if (node.Preparing is null && node.Token is null && !node.Retiring)
        {
            Initialize(node);
            if (node.Phase == UiWindowState.Closing)
            {
                return;
            }

            UpdateGeometry(node);
            node.Surface.SetViewport(node.Size);
            node.Surface.UpdateLayout();
            node.Preparing = node.Source!.PreparePresentationAsync(node.Desired!, node.Surface, node.Cancellation.Token).AsTask();
        }

        if (node.Preparing is { IsCompleted: true } pending)
        {
            node.Preparing = null;
            node.Token = pending.GetAwaiter().GetResult();
            node.Token.Update(node.Desired!);
        }

        if (node.Token is null || node.Retiring)
        {
            return;
        }

        UpdateGeometry(node);
        if (node.Native is not null)
        {
            node.NativePlace ??= node.Native.PlaceAsync(Anchor(node), node.Placement!.PreferredSide, node.Placement.Gap).AsTask();
            if (!node.NativePlace.IsCompleted)
            {
                return;
            }

            node.NativePlace.GetAwaiter().GetResult();
        }

        Commit(node);
        if (node.Phase == UiWindowState.Preparing)
        {
            node.PreparationElapsed = clock.GetElapsedTime(node.Started);
            node.Phase = node.Registration is null ? UiWindowState.Ready : UiWindowState.Open;
            node.Bind!.TrySetResult(new(node.Id, () => Retire(node)));
        }

        if (node.Committed?.GeometryRevision == node.Desired!.GeometryRevision)
        {
            if (node.ResizeRequest is { } resize)
            {
                Complete(resize);
                node.ResizeRequest = null;
            }

            if (node.StackingRequest is { } stacking)
            {
                Complete(stacking);
                node.StackingRequest = null;
            }
        }

        if (node.PlaceRequest is { } placement && node.Committed?.GeometryRevision == node.Desired!.GeometryRevision)
        {
            Complete(placement);
            node.PlaceRequest = null;
        }

        if (node.ShowIssued && node.Phase == UiWindowState.Ready)
        {
            if (node.Native is not null)
            {
                node.NativeShow ??= node.Native.ShowAsync().AsTask();
                if (!node.NativeShow.IsCompleted)
                {
                    return;
                }

                node.NativeShow.GetAwaiter().GetResult();
                Refresh(node);
                if (!node.State.IsVisible)
                {
                    return;
                }
            }

            UpdateGeometry(node);
            Commit(node);
            if (node.Committed is { IsVisible: true })
            {
                node.Phase = UiWindowState.Open;
                Complete(node.ShowRequest!.Value);
            }
        }
    }

    private void Initialize(Node node)
    {
        if (node.Registration is not null)
        {
            node.Size = node.State.ClientSize;
            return;
        }

        Node owner = Get(node.Owner!.Value);
        UiWindowCreateRequest request = node.Create!;
        if (native is null)
        {
            if (request.Policy == UiWindowPolicy.RequireNative)
            {
                Fail(node, "native-unavailable");
                return;
            }

            node.Fallback = request.Policy == UiWindowPolicy.PreferNative ? unavailableCode : string.Empty;
            node.Root = owner.Root;
            node.Source = Get(owner.Root).Source;
            node.Presentation = UiWindowPresentation.Hosted;
            Vector2 limit = Get(owner.Root).State.ClientSize;
            node.Size = Vector2.Min(request.ClientSize, limit);
            Refresh(node);
        }
        else
        {
            IUiWindowSource source = Get(owner.Root).Source!;
            UiNativeWindowSupport support = native.GetSupport(source);
            if (request.Policy == UiWindowPolicy.HostedOnly || request.Role == UiWindowRole.Normal || !support.Available)
            {
                Fail(node, "native-unavailable");
                return;
            }

            Vector2 limit = native.GetLimit(source, Anchor(node));
            if (!Finite(limit) || limit.X <= 0 || limit.Y <= 0)
            {
                Fail(node, "placement-failed");
                return;
            }

            node.Native = native.Create(source, request with { ClientSize = Vector2.Min(request.ClientSize, limit), });
            node.Source = node.Native.Source;
            node.Root = node.Id;
            node.Presentation = UiWindowPresentation.Native;
            Refresh(node);
            node.Size = node.State.ClientSize;
        }
    }

    private Rect2D Anchor(Node node)
    {
        UiWindowPlacement p = node.Placement!;
        Node anchor = Get(p.AnchorWindow);
        if (!TryTransformPoint(anchor.Id, Get(node.Owner!.Value).Root, new(p.Anchor.X, p.Anchor.Y), out Vector2 at))
        {
            throw new UiWindowOperationException("placement-failed");
        }

        return new(at.X, at.Y, p.Anchor.Width, p.Anchor.Height);
    }

    private void UpdateGeometry(Node node)
    {
        Rect2D bounds;
        if (node.Presentation == UiWindowPresentation.External || node.Presentation == UiWindowPresentation.Native)
        {
            node.Size = node.State.ClientSize;
            bounds = new(0, 0, node.Size.X, node.Size.Y);
        }
        else
        {
            Node root = Get(node.Root);
            Refresh(root);
            Rect2D anchor = Anchor(node);
            Vector2 limit = root.State.ClientSize;
            node.Size = Vector2.Min(node.RequestedSize ?? node.Create!.ClientSize, limit);
            Vector2 at = PlacePoint(anchor, node.Size, limit, node.Placement!.PreferredSide, node.Placement.Gap);
            bounds = new(at.X, at.Y, node.Size.X, node.Size.Y);
        }

        Rect2D clip = Intersect(bounds, new(0, 0, Get(node.Root).State.ClientSize.X, Get(node.Root).State.ClientSize.Y));
        if (node.Role == UiWindowRole.Normal && node.Owner is { } owner && Get(owner).Committed is { } parent)
        {
            clip = Intersect(clip, parent.ClipInPresentation);
        }

        bool visible = node.Registration is not null ? node.State.IsVisible : node.ShowIssued;
        UiWindowPresentationView next = new(node.Id, node.Source!.SourceId, node.Role, bounds, clip, node.State.Scale, 0, visible) { StackingOrder = node.StackingOrder };
        if (node.Desired is { } previous && previous with
        {
            GeometryRevision = 0,
        }

        == next)
        {
            return;
        }

        node.Desired = next with
        {
            GeometryRevision = checked(++node.Revision),
        };
        node.Surface!.SetViewport(node.Size);
        node.Surface.UpdateLayout();
        node.Token?.Update(node.Desired);
    }

    private void Drain(Node node)
    {
        if (node.Terminal && node.Registration is { } finishedRegistration && !_nodes.Values.Any(n => n.Owner == node.Id))
        {
            finishedRegistration.IsReleased = true;
            Remove(node);
            return;
        }

        if (node.Phase != UiWindowState.Closing && !node.Retiring)
        {
            return;
        }

        if (_nodes.Values.Any(n => n.Owner == node.Id && !n.Terminal))
        {
            return;
        }

        if (node.Preparing is { } preparing)
        {
            if (!preparing.IsCompleted)
            {
                return;
            }

            node.Preparing = null;
            try
            {
                node.Token = preparing.GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                node.Error ??= error;
            }
        }

        if (node.Token is not null && node.Retirement is null)
        {
            node.Retirement = node.Token.DisposeAsync().AsTask();
        }

        if (node.Retirement is { IsCompleted: false })
        {
            return;
        }

        Exception? cleanup = null;
        if (node.Retirement is not null)
        {
            try
            {
                node.Retirement.GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                cleanup = error;
            }

            node.Retirement = null;
        }

        node.Token = null;
        node.Surface = null;
        node.Committed = null;
        node.Desired = null;
        node.Cancellation?.Dispose();
        node.Cancellation = null;
        if (cleanup is not null)
        {
            node.Retired?.TrySetException(cleanup);
            node.Error ??= cleanup;
            node.Failure = "presentation-failed";
        }
        else
        {
            node.Retired?.TrySetResult();
        }

        node.Retiring = false;
        if (node.Phase != UiWindowState.Closing)
        {
            node.Phase = UiWindowState.Ready;
            return;
        }

        if (node.Loans != 0)
        {
            return;
        }

        if (node.Native is not null)
        {
            node.NativeClose ??= node.Native.CloseAsync().AsTask();
            if (!node.NativeClose.IsCompleted)
            {
                return;
            }

            try
            {
                node.NativeClose.GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                node.Error ??= error;
                node.Failure = "window-close-failed";
                if (!node.Native.Source.CaptureState().IsClosed)
                {
                    return;
                }
            }

            node.Native = null;
        }

        node.Phase = node.Failure.Length == 0 ? UiWindowState.Closed : UiWindowState.Failed;
        if (node.Bind is { Task.IsCompleted: false })
        {
            if (node.Failure.Length != 0)
            {
                node.Bind.TrySetException(new UiWindowOperationException(node.Failure, node.Error));
            }
            else
            {
                node.Bind.TrySetCanceled();
            }
        }

        foreach (UiWindowRequestId request in _requests.Values.Where(r => r.Window == node.Id && r.Status == UiWindowRequestStatus.Pending).Select(r => r.Id).ToArray())
        {
            Complete(request, request == node.CloseRequest ? string.Empty : node.Failure.Length == 0 ? "canceled" : node.Failure);
        }

        if (node.Registration is { } registration && !_nodes.Values.Any(n => n.Owner == node.Id))
        {
            registration.IsReleased = true;
            Remove(node);
        }
    }

    private UiWindowSnapshot Snapshot(Node n)
    {
        UiWindowPresentationView? view = n.Committed;
        return new(n.Id, n.Revision, n.Phase, n.Owner, n.Root, n.Keyboard, n.Source?.SourceId ?? Guid.Empty, n.State.InputOrigin, n.State.KeyboardScopeId, n.Role, n.Mode, n.Presentation, n.Size, n.State.Scale, view?.BoundsInPresentation ?? default, view?.ClipInPresentation ?? default, n.Phase == UiWindowState.Open && n.State.IsVisible && n.State.ClientSize.X > 0 && n.State.ClientSize.Y > 0 && view is { IsVisible: true }, n.State.IsActive, n.ShowIssued, n.Fallback, n.Failure);
    }

    private void Remove(Node node)
    {
        _nodes.Remove(node.Id);
        foreach (UiWindowId root in _keyboardTargets.Where(pair => pair.Key == node.Id || pair.Value == node.Id).Select(pair => pair.Key).ToArray())
        {
            _keyboardTargets.Remove(root);
        }

        node.Source = null;
        node.Error = null;
        foreach (UiWindowRequestId id in _requests.Values.Where(r => r.Window == node.Id).Select(r => r.Id).ToArray())
        {
            _requests.Remove(id);
        }
    }

    private sealed class Node(UiWindowId id, UiWindowId? owner, UiWindowRole role, UiWindowInputMode mode)
    {
        internal UiWindowId Id { get; } = id;

        internal UiWindowId? Owner { get; } = owner;

        internal UiWindowRole Role { get; } = role;

        internal UiWindowInputMode Mode { get; } = mode;

        internal UiWindowId Root { get; set; }

        internal UiWindowId Keyboard { get; set; }

        internal UiWindowCreateRequest? Create { get; set; }

        internal UiWindowPlacement? Placement { get; set; }

        internal UiWindowState Phase { get; set; } = UiWindowState.Pending;

        internal UiWindowPresentation? Presentation { get; set; }

        internal IUiWindowSource? Source { get; set; }

        internal UiWindowSourceSnapshot State { get; set; } = new(0, null, Vector2.Zero, Vector2.One, false, false, false, Guid.Empty);

        internal UiWindowRegistration? Registration { get; set; }

        internal IUiNativeWindow? Native { get; set; }

        internal UiSurface? Surface { get; set; }

        internal IUiWindowPresentation? Token { get; set; }

        internal Task<IUiWindowPresentation>? Preparing { get; set; }

        internal Task? Retirement { get; set; }

        internal Task? NativePlace { get; set; }

        internal Task? NativeShow { get; set; }

        internal Task? NativeClose { get; set; }

        internal TaskCompletionSource<UiWindowBinding>? Bind { get; set; }

        internal TaskCompletionSource? Retired { get; set; }

        internal CancellationTokenSource? Cancellation { get; set; }

        internal UiWindowPresentationView? Desired { get; set; }

        internal UiWindowPresentationView? Committed { get; set; }

        internal UiWindowRequestId? ShowRequest { get; set; }

        internal UiWindowRequestId? PlaceRequest { get; set; }

        internal UiWindowRequestId? CloseRequest { get; set; }

        internal Vector2 Size { get; set; }

        internal Vector2? RequestedSize { get; set; }

        internal ulong StackingOrder { get; set; }

        internal UiWindowRequestId? ResizeRequest { get; set; }

        internal UiWindowRequestId? StackingRequest { get; set; }

        internal ulong Revision { get; set; }

        internal long Started { get; set; }

        internal TimeSpan PreparationElapsed { get; set; }

        internal int Loans { get; set; }

        internal bool ShowIssued { get; set; }

        internal bool Retiring { get; set; }

        internal string Fallback { get; set; } = string.Empty;

        internal string Failure { get; set; } = string.Empty;

        internal Exception? Error { get; set; }

        internal bool Terminal => Phase is UiWindowState.Closed or UiWindowState.Failed;
    }
}
