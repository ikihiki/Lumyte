using System.Numerics;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Abstractions;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.UI.Windowing.WindowSystem;

/// <summary>An optional native adapter; the host pumps Platform and WindowSystem before Advance.</summary>
public sealed class WindowSystemUiWindowManager : IUiWindowManager, IUiWindowPresentationReplacementManager, IUiWindowRequestLifetime
{
    private readonly UiWindowManagerCore _inner;

    private readonly NativeBackend _backend;

    /// <summary>Initializes a new instance of the <see cref="WindowSystemUiWindowManager"/> class.</summary>
    /// <param name="windows">The borrowed native window system.</param>
    /// <param name="input">The matching ordered input acquisition source.</param>
    /// <param name="presentations">The borrowed renderer factory.</param>
    /// <param name="capabilities">Host-verified native input capabilities.</param>
    /// <param name="clock">The monotonic preparation clock.</param>
    public WindowSystemUiWindowManager(Lumyte.Windowing.WindowSystem windows, IWindowInputSource input, IUiNativePresentationFactory presentations, UiNativeWindowHostCapabilities capabilities, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(presentations);
        ArgumentNullException.ThrowIfNull(capabilities);
        _backend = new(windows, input, presentations, capabilities);
        _inner = new(clock ?? TimeProvider.System, _backend);
    }

    /// <inheritdoc/>
    public Guid Id => _inner.Id;

    /// <inheritdoc/>
    public UiWindowPresentations SupportedPresentations => _inner.SupportedPresentations;

    /// <inheritdoc/>
    public bool IsStopped => _inner.IsStopped;

    /// <summary>Resolves only a live, explicitly registered native source identity.</summary>
    /// <param name="sourceId">The actual source identity from a snapshot.</param>
    /// <param name="window">The borrowed native window.</param>
    /// <returns>Whether this adapter knows the live source.</returns>
    public bool TryResolveNativeWindow(Guid sourceId, out IWindow? window)
    {
        window = null;
        UiWindowSnapshot? snapshot = _inner.CaptureWindows().FirstOrDefault(s => s.SourceId == sourceId && s.State is not (UiWindowState.Closed or UiWindowState.Failed));
        if (snapshot is null)
        {
            return false;
        }

        UiWindowSourceLease lease = _inner.BorrowPresentationSource(snapshot.Id);
        try
        {
            window = (UiWindowSourceProjection.Unwrap(lease.Source) as WindowSystemUiWindowSource)?.Window;
            return window is not null;
        }
        finally
        {
            _ = lease.DisposeAsync();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _inner.Dispose();
        _backend.Dispose();
    }

    /// <inheritdoc/>
    public UiWindowRegistration Register(IUiWindowSource source, UiWindowId? keyboardRoot = null) => _inner.Register(source, keyboardRoot);

    /// <inheritdoc/>
    public UiWindowId Create(UiWindowCreateRequest request) => _inner.Create(request);

    /// <inheritdoc/>
    public ValueTask<UiWindowBinding> BindPresentationAsync(UiWindowId window, UiSurface surface, CancellationToken cancellationToken = default) => _inner.BindPresentationAsync(window, surface, cancellationToken);

    /// <inheritdoc/>
    public IUiWindowPresentationReplacement BeginReplacement(UiWindowId window, UiWindowBinding? current, UiSurface candidate) => _inner.BeginReplacement(window, current, candidate);

    /// <inheritdoc/>
    public bool IsBound(UiWindowId window, UiSurface surface) => _inner.IsBound(window, surface);

    /// <inheritdoc/>
    public UiWindowSourceLease BorrowPresentationSource(UiWindowId window) => _inner.BorrowPresentationSource(window);

    /// <inheritdoc/>
    public UiNativeWindowSupport GetNativeSupport(UiWindowId owner) => _inner.GetNativeSupport(owner);

    /// <inheritdoc/>
    public UiWindowRequestId Place(UiWindowId window, UiWindowPlacement placement) => _inner.Place(window, placement);

    /// <inheritdoc/>
    public UiWindowRequestId Resize(UiWindowId window, Vector2 clientSize) => _inner.Resize(window, clientSize);

    /// <inheritdoc/>
    public UiWindowRequestId BringToFront(UiWindowId window) => _inner.BringToFront(window);

    /// <inheritdoc/>
    public UiWindowRequestId Show(UiWindowId window) => _inner.Show(window);

    /// <inheritdoc/>
    public UiWindowRequestId Close(UiWindowId window) => _inner.Close(window);

    /// <inheritdoc/>
    public void Release(UiWindowId window) => _inner.Release(window);

    /// <inheritdoc/>
    public bool TryGetRequest(UiWindowRequestId request, out UiWindowRequestResult? result) => _inner.TryGetRequest(request, out result);

    /// <inheritdoc/>
    public bool ReleaseRequest(UiWindowRequestId request) => _inner.ReleaseRequest(request);

    /// <inheritdoc/>
    public bool TryGetSnapshot(UiWindowId window, out UiWindowSnapshot? snapshot) => _inner.TryGetSnapshot(window, out snapshot);

    /// <inheritdoc/>
    public IReadOnlyList<UiWindowSnapshot> CaptureWindows() => _inner.CaptureWindows();

    /// <inheritdoc/>
    public UiWindowId? ResolveInputRoot(InputWindowId origin) => _inner.ResolveInputRoot(origin);

    /// <inheritdoc/>
    public UiWindowId? GetKeyboardTarget(UiWindowId window) => _inner.GetKeyboardTarget(window);

    /// <inheritdoc/>
    public void SetKeyboardTarget(UiWindowId window) => _inner.SetKeyboardTarget(window);

    /// <inheritdoc />
    public UiWindowHit? HitTest(UiWindowId presentationRoot, Vector2 position) => _inner.HitTest(presentationRoot, position);

    /// <inheritdoc/>
    public bool TryTransformPoint(UiWindowId from, UiWindowId to, Vector2 position, out Vector2 transformed) => _inner.TryTransformPoint(from, to, position, out transformed);

    /// <inheritdoc/>
    public void Advance() => _inner.Advance();

    /// <inheritdoc/>
    public void BeginStop() => _inner.BeginStop();
}
