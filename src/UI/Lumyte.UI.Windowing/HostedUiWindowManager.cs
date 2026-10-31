using System.Numerics;
using Lumyte.Input.Abstractions;

namespace Lumyte.UI.Windowing;

/// <summary>An independent manager composing transient UI inside borrowed roots without native windows.</summary>
public sealed class HostedUiWindowManager : IUiWindowManager, IUiWindowRequestLifetime
{
    private readonly UiWindowManagerCore _inner;

    /// <summary>Initializes a new instance of the <see cref="HostedUiWindowManager"/> class.</summary>
    /// <param name="clock">The monotonic preparation clock.</param>
    /// <param name="nativeUnavailableCode">The host's stable native capability reason.</param>
    public HostedUiWindowManager(TimeProvider? clock = null, string nativeUnavailableCode = "native-unavailable")
    {
        if (nativeUnavailableCode is not ("native-unavailable" or "browser-hosted-only" or "origin-input-unavailable" or "native-features-unavailable" or "native-renderer-unavailable" or "native-observation-unavailable"))
        {
            throw new ArgumentException("Unknown native capability reason.", nameof(nativeUnavailableCode));
        }

        _inner = new(clock ?? TimeProvider.System, unavailableCode: nativeUnavailableCode);
    }

    /// <inheritdoc/>
    public Guid Id => _inner.Id;

    /// <inheritdoc/>
    public UiWindowPresentations SupportedPresentations => _inner.SupportedPresentations;

    /// <inheritdoc/>
    public bool IsStopped => _inner.IsStopped;

    /// <inheritdoc/>
    public UiWindowRegistration Register(IUiWindowSource source, UiWindowId? keyboardRoot = null) => _inner.Register(source, keyboardRoot);

    /// <inheritdoc/>
    public UiWindowId Create(UiWindowCreateRequest request) => _inner.Create(request);

    /// <inheritdoc/>
    public ValueTask<UiWindowBinding> BindPresentationAsync(UiWindowId window, UiSurface surface, CancellationToken cancellationToken = default) => _inner.BindPresentationAsync(window, surface, cancellationToken);

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

    /// <inheritdoc/>
    public void Dispose() => _inner.Dispose();
}
