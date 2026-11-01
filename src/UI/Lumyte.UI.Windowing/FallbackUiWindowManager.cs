using System.Numerics;
using Lumyte.Input.Abstractions;

namespace Lumyte.UI.Windowing;

/// <summary>Composes borrowed native and hosted managers, with cleanup before a pre-show fallback.</summary>
public sealed class FallbackUiWindowManager : IUiWindowManager, IUiWindowRequestLifetime
{
    private readonly IUiWindowManager _native;

    private readonly IUiWindowManager _hosted;

    private readonly TimeProvider _clock;

    private readonly Dictionary<UiWindowId, Entry> _entries = [];
    private readonly Dictionary<UiWindowId, UiWindowId> _keyboardTargets = [];

    private readonly Dictionary<UiWindowRequestId, UiWindowRequestResult> _requests = [];

    private ulong _next;

    private ulong _nextRequest;

    private bool _stopping;

    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="FallbackUiWindowManager"/> class.</summary>
    /// <param name="native">The exclusively borrowed native implementation.</param>
    /// <param name="hosted">The exclusively borrowed hosted implementation.</param>
    /// <param name="clock">The monotonic clock.</param>
    public FallbackUiWindowManager(IUiWindowManager native, IUiWindowManager hosted, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(hosted);
        if (ReferenceEquals(native, hosted) || native.Id == hosted.Id)
        {
            throw new ArgumentException("Distinct managers are required.");
        }

        _native = native;
        _hosted = hosted;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public Guid Id { get; } = Guid.NewGuid();

    /// <inheritdoc/>
    public UiWindowPresentations SupportedPresentations => _native.SupportedPresentations | _hosted.SupportedPresentations;

    /// <inheritdoc/>
    public bool IsStopped => _stopping && _entries.Values.All(e => e.Terminal && e.Surface is null && e.Loans == 0 && (e.Registration is null || e.Registration.IsReleased));

    /// <inheritdoc/>
    public UiWindowRegistration Register(IUiWindowSource source, UiWindowId? keyboardRoot = null)
    {
        Admit();
        ArgumentNullException.ThrowIfNull(source);
        Entry? keyboard = keyboardRoot is { } key ? Get(key) : null;
        UiWindowRegistration a = _native.Register(ProjectSource(source, _native), keyboard?.NativeRoot?.Window);
        UiWindowRegistration b;
        try
        {
            b = _hosted.Register(ProjectSource(source, _hosted), keyboard?.HostedRoot?.Window);
        }
        catch
        {
            a.Dispose();
            throw;
        }

        UiWindowId id = Next();
        var registration = new UiWindowRegistration(id, () => CloseEntry(Get(id)));
        var entry = new Entry(id)
        {
            NativeRoot = a,
            HostedRoot = b,
            Manager = _hosted,
            Inner = b.Window,
            Registration = registration,
            Keyboard = keyboard?.Keyboard ?? id,
        };
        _entries.Add(id, entry);
        return registration;
    }

    /// <inheritdoc/>
    public UiWindowId Create(UiWindowCreateRequest request)
    {
        Admit();
        ArgumentNullException.ThrowIfNull(request);
        Entry owner = Get(request.Owner);
        if (owner.Closing || owner.Terminal)
        {
            throw new ArgumentException("Owner is closing.", nameof(request));
        }

        _ = Get(request.Placement.AnchorWindow);

        // The selected implementation performs the full synchronous request validation before publishing an identity.
        IUiWindowManager manager = request.Policy == UiWindowPolicy.HostedOnly ? _hosted : _native;
        UiNativeWindowSupport support = GetNativeSupport(request.Owner);
        string fallback = string.Empty;
        if (request.Policy == UiWindowPolicy.PreferNative && !support.Available)
        {
            manager = _hosted;
            fallback = support.Code;
        }

        (UiWindowId innerOwner, UiWindowRegistration? bridge, UiWindowSourceLease? loan) = MapOwner(owner, manager);
        UiWindowId inner;
        try
        {
            inner = manager.Create(MapRequest(request, manager, innerOwner));
        }
        catch
        {
            bridge?.Dispose();
            if (loan is not null)
            {
                _ = loan.DisposeAsync();
            }

            throw;
        }

        UiWindowId id = Next();
        _entries.Add(id, new(id) { Request = request, Owner = owner.Id, Keyboard = owner.Keyboard, Manager = manager, Inner = inner, Bridge = bridge, Loan = loan, Fallback = fallback, });
        return id;
    }

    /// <inheritdoc/>
    public ValueTask<UiWindowBinding> BindPresentationAsync(UiWindowId window, UiSurface surface, CancellationToken cancellationToken = default)
    {
        Admit();
        Entry entry = Get(window);
        ArgumentNullException.ThrowIfNull(surface);
        if (entry.Surface is not null || entry.Terminal || entry.Closing || _entries.Values.Any(e => ReferenceEquals(e.Surface, surface)))
        {
            throw new InvalidOperationException("Duplicate or closing binding.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        entry.Surface = surface;
        entry.CallerCancellation = cancellationToken;
        entry.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        entry.Accepted = _clock.GetTimestamp();
        entry.Bind = new();
        entry.Retired = new();
        entry.Pending = entry.Manager!.BindPresentationAsync(entry.Inner, surface, entry.Cancellation.Token).AsTask();
        return new(entry.Bind.Task);
    }

    /// <inheritdoc/>
    public bool IsBound(UiWindowId window, UiSurface surface)
    {
        Check();
        return ReferenceEquals(Get(window).Surface, surface);
    }

    /// <inheritdoc/>
    public UiWindowSourceLease BorrowPresentationSource(UiWindowId window)
    {
        Check();
        Entry e = Get(window);
        if (e.Closing || e.Terminal || e.FallingBack)
        {
            throw new InvalidOperationException("The presentation source is closing.");
        }

        UiWindowSourceLease inner = e.Manager!.BorrowPresentationSource(e.Inner);
        checked
        {
            e.Loans++;
        }

        UiWindowSnapshot snapshot = Snapshot(e);
        return new(inner.Source, snapshot.PresentationRoot, e.Keyboard, () =>
        {
            Check();
            _ = inner.DisposeAsync();
            e.Loans--;
        });
    }

    /// <inheritdoc/>
    public UiNativeWindowSupport GetNativeSupport(UiWindowId owner)
    {
        Check();
        Entry entry = Get(owner);
        if (entry.NativeRoot is { } root)
        {
            return _native.GetNativeSupport(root.Window);
        }

        if (entry.Manager == _native)
        {
            return _native.GetNativeSupport(entry.Inner);
        }

        UiWindowSnapshot snapshot = Snapshot(entry);
        Entry presentation = Get(snapshot.PresentationRoot);
        return presentation.NativeRoot is { } registration ? _native.GetNativeSupport(registration.Window) : presentation.Manager == _native ? _native.GetNativeSupport(presentation.Inner) : new(false, "native-unavailable");
    }

    /// <inheritdoc/>
    public UiWindowRequestId Place(UiWindowId window, UiWindowPlacement placement)
    {
        Check();
        Entry e = Owned(window);
        if (e.Closing || e.Terminal)
        {
            throw new InvalidOperationException("Window is closing.");
        }

        UiWindowCreateRequest request = e.Request! with
        {
            Placement = placement,
        };
        UiWindowPlacement mapped = MapRequest(request, e.Manager!, e.Manager!.TryGetSnapshot(e.Inner, out UiWindowSnapshot? s) ? s!.Owner!.Value : default).Placement;
        e.Request = request;
        return Track(e, e.Manager.Place(e.Inner, mapped));
    }

    /// <inheritdoc/>
    public UiWindowRequestId Show(UiWindowId window)
    {
        Check();
        Entry e = Owned(window);
        if (e.Closing || e.Terminal || e.ShowIssued)
        {
            throw new InvalidOperationException("Window cannot be shown.");
        }

        UiWindowRequestId inner = e.Manager!.Show(e.Inner);
        e.ShowIssued = true;
        return Track(e, inner);
    }

    /// <inheritdoc/>
    public UiWindowRequestId Close(UiWindowId window)
    {
        Check();
        Entry e = Owned(window);
        return CloseEntry(e);
    }

    /// <inheritdoc/>
    public void Release(UiWindowId window)
    {
        Check();
        Entry e = Owned(window);
        if (!e.Terminal || e.Surface is not null || e.Loans != 0 || _entries.Values.Any(child => child.Owner == window))
        {
            throw new InvalidOperationException("The window has not drained.");
        }

        if (!e.InnerReleased)
        {
            e.Manager!.Release(e.Inner);
        }

        Remove(e);
    }

    /// <inheritdoc/>
    public bool TryGetRequest(UiWindowRequestId request, out UiWindowRequestResult? result)
    {
        Check();
        if (request.ManagerId != Id || request.Value == 0)
        {
            throw new ArgumentException("Foreign request.", nameof(request));
        }

        return _requests.TryGetValue(request, out result);
    }

    /// <inheritdoc/>
    public bool ReleaseRequest(UiWindowRequestId request) => TryGetRequest(request, out UiWindowRequestResult? result) && result is { Status: not UiWindowRequestStatus.Pending } && _requests.Remove(request);

    /// <inheritdoc/>
    public bool TryGetSnapshot(UiWindowId window, out UiWindowSnapshot? snapshot)
    {
        Check();
        ValidateId(window);
        snapshot = _entries.TryGetValue(window, out Entry? e) ? Snapshot(e) : null;
        return snapshot is not null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<UiWindowSnapshot> CaptureWindows()
    {
        Check();
        return Array.AsReadOnly(_entries.Values.Select(Snapshot).ToArray());
    }

    /// <inheritdoc/>
    public UiWindowId? ResolveInputRoot(InputWindowId origin)
    {
        Check();
        return _entries.Values.Select(Snapshot).FirstOrDefault(s => s.Id == s.PresentationRoot && s.InputOrigin == origin && s.State == UiWindowState.Open)?.Id;
    }

    /// <inheritdoc/>
    public UiWindowId? GetKeyboardTarget(UiWindowId window)
    {
        Check();
        Entry e = Get(Get(window).Keyboard);
        if (e.Manager!.GetKeyboardTarget(e.Inner) is null)
        {
            return null;
        }

        return _keyboardTargets.TryGetValue(e.Id, out UiWindowId target) && _entries.TryGetValue(target, out Entry? selected) && !selected.Closing && !selected.Terminal && Snapshot(selected).IsVisible ? target : e.Id;
    }

    /// <inheritdoc/>
    public void SetKeyboardTarget(UiWindowId window)
    {
        Check();
        Entry entry = Get(window);
        UiWindowSnapshot snapshot = Snapshot(entry);
        if (!snapshot.IsVisible || entry.Closing || entry.Terminal || snapshot.Role is not (UiWindowRole.Normal or UiWindowRole.Tool) || snapshot.InputMode != UiWindowInputMode.Interactive || entry.Surface is null)
        {
            throw new InvalidOperationException("Keyboard selection requires a visible bound normal window.");
        }

        _keyboardTargets[entry.Keyboard] = window;
    }

    /// <inheritdoc />
    public UiWindowHit? HitTest(UiWindowId presentationRoot, Vector2 position)
    {
        Check();
        _ = Get(presentationRoot);
        var visited = new HashSet<(IUiWindowManager Manager, UiWindowId Root)>();
        UiWindowHit? best = null;
        int bestLayer = -1;
        foreach (Entry entry in _entries.Values)
        {
            if (entry.Manager is not { } manager || Snapshot(entry).PresentationRoot != presentationRoot
                || !manager.TryGetSnapshot(entry.Inner, out UiWindowSnapshot? inner) || inner is null
                || !visited.Add((manager, inner.PresentationRoot)))
            {
                continue;
            }

            if (manager.HitTest(inner.PresentationRoot, position) is not { } hit)
            {
                continue;
            }

            Entry? target = _entries.Values.FirstOrDefault(candidate => ReferenceEquals(candidate.Manager, manager) && candidate.Inner == hit.Window);
            if (target is null || target.Closing || target.Terminal)
            {
                continue;
            }

            UiWindowSnapshot snapshot = Snapshot(target);
            int layer = UiWindowStacking.Layer(snapshot.Role);
            if (layer > bestLayer || (layer == bestLayer && target.Id.Value > (best?.Window.Value ?? 0)))
            {
                best = hit with { Window = target.Id, GeometryRevision = target.PresentedGeometry };
                bestLayer = layer;
            }
        }

        return best;
    }

    /// <inheritdoc/>
    public bool TryTransformPoint(UiWindowId from, UiWindowId to, Vector2 position, out Vector2 transformed)
    {
        Check();
        Entry a = Get(from);
        Entry b = Get(to);
        UiWindowSnapshot sa = Snapshot(a);
        UiWindowSnapshot sb = Snapshot(b);
        Vector2 pa = position + new Vector2(sa.BoundsInPresentation.X, sa.BoundsInPresentation.Y);
        Vector2 pb = new(sb.BoundsInPresentation.X, sb.BoundsInPresentation.Y);
        if (sa.PresentationRoot == sb.PresentationRoot)
        {
            transformed = pa - pb;
            return true;
        }

        UiWindowSourceLease la = a.Manager!.BorrowPresentationSource(a.Inner);
        UiWindowSourceLease lb = b.Manager!.BorrowPresentationSource(b.Inner);
        try
        {
            if (la.Source.TryTransformPoint(lb.Source, pa, out Vector2 mapped))
            {
                transformed = mapped - pb;
                return true;
            }

            transformed = default;
            return false;
        }
        finally
        {
            _ = la.DisposeAsync();
            _ = lb.DisposeAsync();
        }
    }

    /// <inheritdoc/>
    public void Advance()
    {
        Check();
        _native.Advance();
        _hosted.Advance();
        foreach (Entry e in _entries.Values.OrderByDescending(e => e.Id.Value).ToArray())
        {
            Step(e);
        }
    }

    /// <inheritdoc/>
    public void BeginStop()
    {
        Check();
        _stopping = true;
        foreach (Entry e in _entries.Values.ToArray())
        {
            CloseEntry(e);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        BeginStop();
        if (!IsStopped)
        {
            throw new InvalidOperationException("Continue Advance until stopped.");
        }

        _entries.Clear();
        _requests.Clear();
        _disposed = true;
    }

    private static UiWindowSnapshot Publish(Entry entry, UiWindowSnapshot snapshot)
    {
        snapshot = snapshot with { Revision = 0 };
        if (entry.LastObservation is { } previous && previous with { Revision = 0 } == snapshot)
        {
            return previous;
        }

        entry.LastObservation = snapshot with { Revision = checked(++entry.Revision) };
        return entry.LastObservation;
    }

    private IUiWindowSource ProjectSource(IUiWindowSource source, IUiWindowManager manager)
    {
        while (source is UiWindowSourceProjection own && own.Owner == Id)
        {
            source = own.InnerSource;
        }

        return new UiWindowSourceProjection(
            Id,
            source,
            view =>
        {
            UiWindowId id = MapId(manager, view.Window);
            Entry entry = Get(id);
            return view with { Window = id, GeometryRevision = checked(++entry.Geometry) };
        },
            (window, revision) => Get(window).PresentedGeometry = revision);
    }

    private UiWindowId Next() => new(Id, checked(++_next));

    private void ValidateId(UiWindowId id)
    {
        if (id.ManagerId != Id || id.Value == 0)
        {
            throw new ArgumentException("Foreign window.", nameof(id));
        }
    }

    private Entry Get(UiWindowId id)
    {
        ValidateId(id);
        return _entries.TryGetValue(id, out Entry? e) ? e : throw new ArgumentException("Unknown window.", nameof(id));
    }

    private Entry Owned(UiWindowId id)
    {
        Entry e = Get(id);
        return e.Registration is null ? e : throw new InvalidOperationException("Borrowed root operation.");
    }

    private void Check() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void Admit()
    {
        Check();
        if (_stopping)
        {
            throw new InvalidOperationException("The manager is stopping.");
        }
    }

    private UiWindowId MapId(IUiWindowManager manager, UiWindowId id) => _entries.Values.FirstOrDefault(e => (e.Manager == manager && e.Inner == id) || e.NativeRoot?.Window == id || e.HostedRoot?.Window == id)?.Id ?? throw new InvalidOperationException("Unmapped inner window.");

    private (UiWindowId Window, UiWindowRegistration? Registration, UiWindowSourceLease? Lease) MapOwner(Entry owner, IUiWindowManager manager)
    {
        if (owner.Registration is not null)
        {
            return (manager == _native ? owner.NativeRoot!.Window : owner.HostedRoot!.Window, null, null);
        }

        if (owner.Manager == manager)
        {
            return (owner.Inner, null, null);
        }

        UiWindowSourceLease lease = owner.Manager!.BorrowPresentationSource(owner.Inner);
        Entry? existingRoot = _entries.Values.FirstOrDefault(e => e.Manager == manager && manager.TryGetSnapshot(e.Inner, out UiWindowSnapshot? snapshot) && snapshot!.Id == snapshot.PresentationRoot && snapshot.SourceId == lease.Source.SourceId);
        if (existingRoot is not null)
        {
            return (existingRoot.Inner, null, lease);
        }

        Entry keyboard = Get(owner.Keyboard);
        UiWindowId keyboardId = manager == _native ? keyboard.NativeRoot!.Window : keyboard.HostedRoot!.Window;
        try
        {
            UiWindowRegistration? shared = _entries.Values.FirstOrDefault(e => e.Manager == manager && e.Bridge is not null && e.Loan?.Source.SourceId == lease.Source.SourceId)?.Bridge;
            UiWindowRegistration bridge = shared ?? manager.Register(ProjectSource(lease.Source, manager), keyboardId);
            return (bridge.Window, bridge, lease);
        }
        catch
        {
            _ = lease.DisposeAsync();
            throw;
        }
    }

    private UiWindowCreateRequest MapRequest(UiWindowCreateRequest request, IUiWindowManager manager, UiWindowId innerOwner)
    {
        Entry anchor = Get(request.Placement.AnchorWindow);
        if (!manager.TryGetSnapshot(innerOwner, out UiWindowSnapshot? owner))
        {
            throw new ArgumentException("Owner is unavailable.");
        }

        UiWindowPlacement p = request.Placement;
        UiWindowSnapshot a = Snapshot(anchor);
        Vector2 point = new(p.Anchor.X, p.Anchor.Y);

        // A cross-implementation owner is a loan of the anchor's actual presentation root.
        if (anchor.Manager == manager && anchor.Registration is null)
        {
            return request with
            {
                Owner = innerOwner,
                Placement = p with
                {
                    AnchorWindow = anchor.Inner,
                },
                Policy = manager == _hosted ? UiWindowPolicy.HostedOnly : UiWindowPolicy.RequireNative,
            };
        }

        point += new Vector2(a.BoundsInPresentation.X, a.BoundsInPresentation.Y);
        return request with
        {
            Owner = innerOwner,
            Placement = p with
            {
                AnchorWindow = owner!.PresentationRoot,
                Anchor = p.Anchor with
                {
                    X = point.X,
                    Y = point.Y,
                },
            },
            Policy = manager == _hosted ? UiWindowPolicy.HostedOnly : UiWindowPolicy.RequireNative,
        };
    }

    private UiWindowSnapshot Snapshot(Entry e)
    {
        if (e.FinalSnapshot is { } final)
        {
            return Publish(e, final with
            {
                State = e.Terminal ? final.State : e.FallingBack ? UiWindowState.Preparing : UiWindowState.Closing,
            });
        }

        if (!e.Manager!.TryGetSnapshot(e.Inner, out UiWindowSnapshot? s))
        {
            throw new InvalidOperationException("Inner window disappeared before release.");
        }

        UiWindowId root = e.Bridge is not null && s!.PresentationRoot == e.Bridge.Window && e.Owner is { } owner ? Snapshot(Get(owner)).PresentationRoot : MapId(e.Manager, s!.PresentationRoot);
        UiWindowState phase = e.FallingBack ? UiWindowState.Preparing : e.Closing && !e.Terminal ? UiWindowState.Closing : s.State;
        return Publish(e, s with
        {
            Id = e.Id,
            Owner = e.Owner,
            PresentationRoot = root,
            KeyboardRoot = e.Keyboard,
            State = phase,
            ShowIssued = e.ShowIssued,
            FallbackCode = e.Fallback,
            Revision = 0,
        });
    }

    private UiWindowRequestId Track(Entry e, UiWindowRequestId inner)
    {
        var id = new UiWindowRequestId(Id, checked(++_nextRequest));
        _requests.Add(id, new(id, e.Id, UiWindowRequestStatus.Pending, string.Empty));
        e.Requests.Add(id, inner);
        return id;
    }

    private UiWindowRequestId CloseEntry(Entry e)
    {
        if (e.CloseRequest is { } id && _requests.ContainsKey(id))
        {
            return id;
        }

        id = new(Id, checked(++_nextRequest));
        e.CloseRequest = id;
        _requests.Add(id, new(id, e.Id, e.Terminal ? UiWindowRequestStatus.Succeeded : UiWindowRequestStatus.Pending, string.Empty));
        if (e.Terminal)
        {
            return id;
        }

        e.Closing = true;
        e.Cancellation?.Cancel();
        foreach (Entry child in _entries.Values.Where(child => child.Owner == e.Id).ToArray())
        {
            CloseEntry(child);
        }

        return id;
    }

    private ValueTask Retire(Entry e)
    {
        e.Retiring = true;
        return new(e.Retired!.Task);
    }

    private void Step(Entry e)
    {
        foreach ((UiWindowRequestId outer, UiWindowRequestId inner) in e.Requests.ToArray())
        {
            if (e.Manager!.TryGetRequest(inner, out UiWindowRequestResult? r) && r is { Status: not UiWindowRequestStatus.Pending })
            {
                _requests[outer] = r! with
                {
                    Id = outer,
                    Window = e.Id,
                };
                e.Requests.Remove(outer);
                (e.Manager as IUiWindowRequestLifetime)?.ReleaseRequest(inner);
            }
        }

        if (e.Pending is { IsCompleted: false } && !e.Closing && !e.FallingBack && _clock.GetElapsedTime(e.Accepted) >= TimeSpan.FromSeconds(2))
        {
            e.TimedOut = true;
            e.Error = new UiWindowOperationException("opening-timeout");
            e.FallingBack = e.Request?.Policy == UiWindowPolicy.PreferNative && e.Manager == _native && !e.CallerCancellation.IsCancellationRequested;
            e.Closing = !e.FallingBack;
            e.Fallback = e.FallingBack ? "opening-timeout" : e.Fallback;
            e.Cancellation?.Cancel();
        }

        if (e.Pending is { IsCompleted: true } pending)
        {
            e.Pending = null;
            try
            {
                e.Binding = pending.GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                e.Error = error;
                if (!e.Closing && !e.ShowIssued && !e.CallerCancellation.IsCancellationRequested && e.Request?.Policy == UiWindowPolicy.PreferNative && e.Manager == _native)
                {
                    e.FallingBack = true;
                    e.Fallback = e.TimedOut ? "opening-timeout" : error is UiWindowOperationException operation ? operation.Code : "native-open-failed";
                }
                else
                {
                    e.Closing = true;
                }
            }

            if (!e.Closing && !e.FallingBack)
            {
                e.Bind!.TrySetResult(new(e.Id, () => Retire(e)));
            }
        }

        if (!e.Closing && !e.FallingBack && !e.Retiring)
        {
            return;
        }

        if (_entries.Values.Any(child => child.Owner == e.Id && !child.Terminal))
        {
            return;
        }

        if (e.Pending is not null || e.Loans != 0)
        {
            return;
        }

        if (e.Binding is not null && e.Retirement is null)
        {
            e.Retirement = e.Binding.DisposeAsync().AsTask();
        }

        if (e.Retirement is { IsCompleted: false })
        {
            return;
        }

        if (e.Retirement is not null)
        {
            try
            {
                e.Retirement.GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                e.Error ??= error;
                e.Retired?.TrySetException(error);
            }

            e.Retirement = null;
            e.Binding = null;
        }

        e.Retired?.TrySetResult();
        if (e.Retiring && !e.Closing && !e.FallingBack)
        {
            e.Surface = null;
            e.Retiring = false;
            return;
        }

        if (e.Registration is not null)
        {
            if (_entries.Values.Any(child => child.Owner == e.Id))
            {
                return;
            }

            e.NativeRoot!.Dispose();
            e.HostedRoot!.Dispose();
            if (!e.NativeRoot.IsReleased || !e.HostedRoot.IsReleased)
            {
                return;
            }

            e.Surface = null;
            e.Terminal = true;
            e.Registration.IsReleased = true;
            CompleteClose(e);
            Remove(e);
            return;
        }

        if (!e.InnerReleased)
        {
            e.InnerClose ??= e.Manager!.Close(e.Inner);
            if (!e.Manager!.TryGetSnapshot(e.Inner, out UiWindowSnapshot? s) || s!.State is not (UiWindowState.Closed or UiWindowState.Failed))
            {
                return;
            }

            if (e.Bridge is not null || e.FallingBack)
            {
                e.FinalSnapshot = Snapshot(e) with
                {
                    State = s!.State,
                    IsVisible = false,
                };
                e.Manager!.Release(e.Inner);
                e.InnerReleased = true;
            }
        }

        if (e.Bridge is not null)
        {
            bool shared = _entries.Values.Any(other => other != e && ReferenceEquals(other.Bridge, e.Bridge));
            if (!shared)
            {
                e.Bridge.Dispose();
                if (!e.Bridge.IsReleased)
                {
                    return;
                }
            }

            e.Bridge = null;
        }

        if (e.Loan is not null)
        {
            _ = e.Loan.DisposeAsync();
            e.Loan = null;
        }

        if (e.FallingBack && e.CallerCancellation.IsCancellationRequested)
        {
            e.Closing = true;
        }

        if (e.FallingBack && !e.Closing)
        {
            if (!e.InnerReleased)
            {
                e.Manager!.Release(e.Inner);
            }

            foreach (UiWindowRequestId request in e.Requests.Keys)
            {
                if (_requests[request].Status == UiWindowRequestStatus.Pending)
                {
                    _requests[request] = _requests[request] with { Status = UiWindowRequestStatus.Failed, Code = "presentation-replaced" };
                }
            }

            e.Requests.Clear();
            e.FinalSnapshot = null;
            e.InnerReleased = false;
            e.Manager = _hosted;
            (UiWindowId owner, UiWindowRegistration? bridge, UiWindowSourceLease? loan) = MapOwner(Get(e.Owner!.Value), _hosted);
            e.Bridge = bridge;
            e.Loan = loan;
            e.Inner = _hosted.Create(MapRequest(e.Request!, _hosted, owner));

            e.InnerClose = null;
            e.Error = null;
            e.FallingBack = false;
            e.Cancellation!.Dispose();
            e.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(e.CallerCancellation);
            e.Accepted = _clock.GetTimestamp();
            e.TimedOut = false;
            e.Pending = _hosted.BindPresentationAsync(e.Inner, e.Surface!, e.Cancellation.Token).AsTask();
            return;
        }

        e.Surface = null;
        e.Terminal = true;
        e.Cancellation?.Dispose();
        e.Cancellation = null;
        CompleteClose(e);
        if (e.Bind is { Task.IsCompleted: false })
        {
            e.Bind.TrySetException(e.Error ?? new OperationCanceledException());
        }
    }

    private void CompleteClose(Entry e)
    {
        if (e.CloseRequest is { } id && _requests.TryGetValue(id, out UiWindowRequestResult? result) && result.Status == UiWindowRequestStatus.Pending)
        {
            _requests[id] = result with
            {
                Status = UiWindowRequestStatus.Succeeded,
            };
        }
    }

    private void Remove(Entry e)
    {
        _entries.Remove(e.Id);
        foreach (UiWindowId root in _keyboardTargets.Where(pair => pair.Key == e.Id || pair.Value == e.Id).Select(pair => pair.Key).ToArray())
        {
            _keyboardTargets.Remove(root);
        }

        foreach (UiWindowRequestId id in _requests.Values.Where(r => r.Window == e.Id).Select(r => r.Id).ToArray())
        {
            _requests.Remove(id);
        }
    }

    private sealed class Entry(UiWindowId id)
    {
        internal UiWindowId Id { get; } = id;

        internal UiWindowId? Owner { get; set; }

        internal UiWindowId Keyboard { get; set; }

        internal UiWindowId Inner { get; set; }

        internal IUiWindowManager? Manager { get; set; }

        internal UiWindowRegistration? Registration { get; set; }

        internal UiWindowRegistration? NativeRoot { get; set; }

        internal UiWindowRegistration? HostedRoot { get; set; }

        internal UiWindowRegistration? Bridge { get; set; }

        internal UiWindowSourceLease? Loan { get; set; }

        internal UiWindowCreateRequest? Request { get; set; }

        internal UiSurface? Surface { get; set; }

        internal UiWindowBinding? Binding { get; set; }

        internal Task<UiWindowBinding>? Pending { get; set; }

        internal Task? Retirement { get; set; }

        internal TaskCompletionSource<UiWindowBinding>? Bind { get; set; }

        internal TaskCompletionSource? Retired { get; set; }

        internal CancellationTokenSource? Cancellation { get; set; }

        internal Dictionary<UiWindowRequestId, UiWindowRequestId> Requests { get; } = [];

        internal UiWindowRequestId? CloseRequest { get; set; }

        internal UiWindowRequestId? InnerClose { get; set; }

        internal bool InnerReleased { get; set; }

        internal UiWindowSnapshot? FinalSnapshot { get; set; }

        internal bool TimedOut { get; set; }

        internal CancellationToken CallerCancellation { get; set; }

        internal bool Closing { get; set; }

        internal bool FallingBack { get; set; }

        internal bool Retiring { get; set; }

        internal bool Terminal { get; set; }

        internal bool ShowIssued { get; set; }

        internal string Fallback { get; set; } = string.Empty;

        internal Exception? Error { get; set; }

        internal ulong Revision { get; set; }

        internal ulong Geometry { get; set; }

        internal ulong PresentedGeometry { get; set; }

        internal UiWindowSnapshot? LastObservation { get; set; }

        internal int Loans { get; set; }

        internal long Accepted { get; set; }
    }
}
