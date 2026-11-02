using System.Numerics;
using Lumyte.Input.Abstractions;

namespace Lumyte.UI.Windowing;

/// <summary>Manages borrowed roots and owned transient windows, driven sequentially by the UI host.</summary>
public interface IUiWindowManager : IDisposable
{
    /// <summary>Gets Id.</summary>
    Guid Id { get; }

    /// <summary>Gets a value indicating whether all registrations and owned windows have drained.</summary>
    bool IsStopped { get; }

    /// <summary>Gets SupportedPresentations.</summary>
    UiWindowPresentations SupportedPresentations { get; }

    /// <summary>既存提示rootを借用登録。同じSourceIdの重複を拒否する。 keyboardRoot省略は自身。指定時は同じmanager/scopeの生存rootを要求する。.</summary>
    /// <param name="source">The source value.</param>
    /// <param name="keyboardRoot">The keyboardRoot value.</param>
    /// <returns>The operation result.</returns>
    UiWindowRegistration Register(IUiWindowSource source, UiWindowId? keyboardRoot = null);

    /// <summary>同期検証後に論理windowを作り、実準備はAdvance/Bindで進める。.</summary>
    /// <param name="request">The request value.</param>
    /// <returns>The operation result.</returns>
    UiWindowId Create(UiWindowCreateRequest request);

    /// <summary>同じwindow/surfaceへの同時bindingを拒否。hidden Readyまでを待つ。 元surfaceを破棄せず、同じsurfaceをfallback先へ再bindingできる。.</summary>
    /// <param name="window">The window value.</param>
    /// <param name="surface">The surface value.</param>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The operation result.</returns>
    ValueTask<UiWindowBinding> BindPresentationAsync(UiWindowId window, UiSurface surface, CancellationToken cancellationToken = default);

    /// <summary>Executes IsBound.</summary>
    /// <param name="window">The window value.</param>
    /// <param name="surface">The surface value.</param>
    /// <returns>The operation result.</returns>
    bool IsBound(UiWindowId window, UiSurface surface);

    /// <summary>Ready/Openの実提示rootを借用。Native/外部rootの終了はleaseの返却を待つ。 Hostedから呼んだ場合も実際のPresentationRootのsourceを返す。.</summary>
    /// <param name="window">The window value.</param>
    /// <returns>The operation result.</returns>
    UiWindowSourceLease BorrowPresentationSource(UiWindowId window);

    /// <summary>Executes GetNativeSupport.</summary>
    /// <param name="owner">The owner value.</param>
    /// <returns>The operation result.</returns>
    UiNativeWindowSupport GetNativeSupport(UiWindowId owner);

    /// <summary>request IDは再利用しない。ShowはReadyのみ。Closeは冪等。.</summary>
    /// <param name="window">The window value.</param>
    /// <param name="placement">The placement value.</param>
    /// <returns>The operation result.</returns>
    UiWindowRequestId Place(UiWindowId window, UiWindowPlacement placement);

    /// <summary>Resizes a hosted normal window; native or registered roots return unsupported.</summary>
    /// <param name="window">The hosted target.</param>
    /// <param name="clientSize">A positive finite logical size.</param>
    /// <returns>A request completed after the geometry is presented.</returns>
    UiWindowRequestId Resize(UiWindowId window, Vector2 clientSize);

    /// <summary>Raises within the current role layer without changing keyboard ownership.</summary>
    /// <param name="window">The hosted normal target.</param>
    /// <returns>A request completed after the new ordering is presented.</returns>
    UiWindowRequestId BringToFront(UiWindowId window);

    /// <summary>Executes Show.</summary>
    /// <param name="window">The window value.</param>
    /// <returns>The operation result.</returns>
    UiWindowRequestId Show(UiWindowId window);

    /// <summary>Executes Close.</summary>
    /// <param name="window">The window value.</param>
    /// <returns>The operation result.</returns>
    UiWindowRequestId Close(UiWindowId window);

    /// <summary>Closed/Failedかつbindingなしのみ。終了snapshot/要求結果を退役させる。.</summary>
    /// <param name="window">The window value.</param>
    void Release(UiWindowId window);

    /// <summary>Executes TryGetRequest.</summary>
    /// <param name="request">The request value.</param>
    /// <param name="result">The result value.</param>
    /// <returns>The operation result.</returns>
    bool TryGetRequest(UiWindowRequestId request, out UiWindowRequestResult? result);

    /// <summary>Executes TryGetSnapshot.</summary>
    /// <param name="window">The window value.</param>
    /// <param name="snapshot">The snapshot value.</param>
    /// <returns>The operation result.</returns>
    bool TryGetSnapshot(UiWindowId window, out UiWindowSnapshot? snapshot);

    /// <summary>Executes CaptureWindows.</summary>
    /// <returns>The operation result.</returns>
    IReadOnlyList<UiWindowSnapshot> CaptureWindows();

    /// <summary>InputWindowIdの取得元から登録済み提示rootへ明示対応する。.</summary>
    /// <param name="origin">The origin value.</param>
    /// <returns>The operation result.</returns>
    UiWindowId? ResolveInputRoot(InputWindowId origin);

    /// <summary>通常画面の配送先。Popup/Menu/Candidateの表示では変更しない。.</summary>
    /// <param name="window">The window value.</param>
    /// <returns>The operation result.</returns>
    UiWindowId? GetKeyboardTarget(UiWindowId window);

    /// <summary>Selects a visible Normal/Tool UI destination without activating an operating-system window.</summary>
    /// <param name="window">A bound normal window in the physical keyboard scope.</param>
    void SetKeyboardTarget(UiWindowId window);

    /// <summary>提示済み幾何・z順・clipで最前面を返す。Passiveは除外、Candidateは含む。.</summary>
    /// <param name="presentationRoot">The presentationRoot value.</param>
    /// <param name="position">The position value.</param>
    /// <returns>The operation result.</returns>
    UiWindowHit? HitTest(UiWindowId presentationRoot, Vector2 position);

    /// <summary>Executes TryTransformPoint.</summary>
    /// <param name="from">The from value.</param>
    /// <param name="to">The to value.</param>
    /// <param name="position">The position value.</param>
    /// <param name="transformed">The transformed value.</param>
    /// <returns>The operation result.</returns>
    bool TryTransformPoint(UiWindowId from, UiWindowId to, Vector2 position, out Vector2 transformed);

    /// <summary>owner threadのIdleで一frame一回。非同期結果をcommitする。.</summary>
    void Advance();

    /// <summary>Executes BeginStop.</summary>
    void BeginStop();
}
