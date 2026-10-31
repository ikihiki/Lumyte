# ADR-UI-0014: 注入可能なUI Window managerとHostedフォールバック

- 状態: 採用
- 日付: 2026-10-28
- 対応: 実装PR 10、UI-0011と同じPR

## 背景

UIの表示先を既存WindowSystemへ直接結び付けると、独自ホストやBrowserでのHosted表示にも具体的なWindowNodeが必要になる。[UI-0011](UI-0011-popup-windowing-and-fallback.md)のPopupだけでなく、通常画面の登録、入力先の解決、配置、描画登録、終了も同じ境界で扱いたい。Native生成に失敗した場合は、注入したHosted実装へ切り替えられる必要がある。

## 決定

### 責務と依存

`Lumyte.UI.Windowing`にIUiWindowManagerと独立したHostedUiWindowManager、複数実装を組み合わせるFallbackUiWindowManagerを置く。UI、Input.Abstractions、BCLへ依存し、公開契約にWindowSystem、WindowNode、IWindow、WindowRole、Platformのhandleを含めない。UI本体からこのパッケージへの依存も追加しない。

既存WindowSystemを使う任意の`Lumyte.UI.Windowing.WindowSystem`を別assembly/packageに置く。WindowSystemUiWindowManagerとWindowSystemUiWindowSourceだけがWindowing/Windowing.Abstractions/Platform.Abstractionsを参照する。GPU準備・終了待ちは注入したpresentation factoryへ委譲する。

IUiWindowManagerは通常画面の借用登録、生成した表示先の状態・配置・描画binding・入力幾何を管理する。UiPopupManagerはmanagerをコンストラクターで受け取り、Composition内容、focus bookmark、Menu入力scope、アプリcallback、Automation/Diagnosticsの終了参加者を管理する。managerはWidget factoryやOnClickを実行しない。Hosted/Nativeの選択とfallbackはmanager境界に置く。

```text
UiPopupManager / 通常画面 / IME候補presenter
                    ↓ IUiWindowManagerを注入
      FallbackUiWindowManager（任意）
         ├─ WindowSystemUiWindowManager → 既存WindowSystem / Native
         └─ HostedUiWindowManager → 登録した提示root内の合成
                    ↓ IUiWindowSourceの描画binding
                 既存Drawing2D / GPU host
```

### ID・不変snapshot・借用root

UiWindowIdはmanagerの識別子と単調な連番を持ち、終了後も再利用しない。defaultと他managerのIDは拒否する。SourceIdは実際の提示rootを表すホスト発行の非空Guidで、OS handleから作らず、接続し直す場合は新規発行する。InputWindowIdは[INPUT-0007](../input/INPUT-0007-window-associated-input.md)の取得元Window接続IDであり、UiWindowId/SourceIdとは別物である。

IUiWindowSourceは既存Native Window、Browser canvas、独自描画先を借用登録する入口である。CaptureStateはコピーされた値だけを返し、Widget、WindowNode、可変配列を含めない。取得元IWindowInputSourceから得たInputWindowIdをホストが明示して渡す。managerがactive WindowやDeviceIdから補完しない。入力を持たない描画先ではnullを許すが、実入力配送の対象にはしない。

snapshotのBoundsInPresentation/ClipInPresentationは提示rootの論理座標、ClientSizeは対象windowの論理寸法、Scaleは提示rootの実際のDPIである。借用した既存rootのPresentationはExternal、生成した表示先はNative/Hostedとする。Native popupのPresentationRootは自身、Hostedは登録rootを指す。KeyboardRootはOS focusを保持する登録rootで、PreserveFocusのNative popupでもowner側を指す。KeyboardScopeIdは同じfocusを共有する範囲の非空Guidであり、DiagnosticsのControlGroupIdとは独立する。

### 公開契約

比較元はmain `20f666ff8f64a63e65407936d32fb98cfa19c9d8`。UI-0002〜0010とINPUT-0007の提案APIを前提とする。Rect2DはGraphics.Drawing2D、Vector2はSystem.Numericsを使う。

```diff
+namespace Lumyte.UI.Windowing
+{
+    public readonly record struct UiWindowId(Guid ManagerId, ulong Value);
+    public readonly record struct UiWindowRequestId(Guid ManagerId, ulong Value);
+    public enum UiWindowPolicy { HostedOnly, PreferNative, RequireNative }
+    public enum UiWindowPresentation { External, Hosted, Native }
+    [Flags]
+    public enum UiWindowPresentations { None = 0, Hosted = 1, Native = 2 }
+    public enum UiWindowRole { Normal, Tool, Popup, ContextMenu, ImeCandidates }
+    public enum UiWindowInputMode { Interactive, Passive, Candidate }
+    public enum UiWindowPlacementSide { Below, Above, Right, Left }
+    public enum UiWindowState { Pending, Preparing, Ready, Open, Closing, Closed, Failed }
+    public enum UiWindowRequestStatus { Pending, Succeeded, Failed }
+    // AnchorはAnchorWindowのclient論理座標。Gapはfiniteかつ非負。
+    public sealed record UiWindowPlacement(UiWindowId AnchorWindow, Rect2D Anchor,
+        UiWindowPlacementSide PreferredSide, float Gap = 0);
+    // Ownerは生存する同managerの登録/生成window。ClientSizeはfiniteかつ各軸正。
+    // 生成は常に非表示・PreserveFocus。Role=Toolの新規生成は初期範囲外。
+    public sealed record UiWindowCreateRequest(UiWindowId Owner, Vector2 ClientSize,
+        UiWindowRole Role, UiWindowInputMode InputMode, UiWindowPlacement Placement,
+        UiWindowPolicy Policy = UiWindowPolicy.HostedOnly);
+    public sealed record UiWindowSourceSnapshot(ulong Revision, InputWindowId? InputOrigin,
+        Vector2 ClientSize, Vector2 Scale, bool IsVisible, bool IsActive, bool IsClosed,
+        Guid KeyboardScopeId);
+    public sealed record UiWindowSnapshot(UiWindowId Id, ulong Revision, UiWindowState State,
+        UiWindowId? Owner, UiWindowId PresentationRoot, UiWindowId KeyboardRoot,
+        Guid SourceId, InputWindowId? InputOrigin, Guid KeyboardScopeId,
+        UiWindowRole Role, UiWindowInputMode InputMode, UiWindowPresentation? Presentation,
+        Vector2 ClientSize, Vector2 Scale, Rect2D BoundsInPresentation,
+        Rect2D ClipInPresentation, bool IsVisible, bool IsActive, bool ShowIssued,
+        string FallbackCode, string FailureCode);
+    public sealed record UiWindowRequestResult(UiWindowRequestId Id, UiWindowId Window,
+        UiWindowRequestStatus Status, string Code);
+    public sealed record UiNativeWindowSupport(bool Available, string Code);
+    public sealed class UiWindowOperationException : Exception
+    {
+        // 環境上の非同期失敗の固定code。元例外はInnerExceptionへ保持する。
+        public string Code { get; }
+    }
+    public readonly record struct UiWindowHit(UiWindowId Window, Vector2 LocalPosition,
+        ulong GeometryRevision, UiWindowInputMode InputMode);
+    // 描画先への登録と以後の配置更新に渡す不変値。Clipも提示root座標。
+    public sealed record UiWindowPresentationView(UiWindowId Window, Guid SourceId,
+        UiWindowRole Role, Rect2D BoundsInPresentation, Rect2D ClipInPresentation,
+        Vector2 Scale, ulong GeometryRevision, bool IsVisible = false);
+    public interface IUiWindowPresentation : IAsyncDisposable
+    {
+        // owner threadのみ。同じtokenへ最新の確定幾何を適用する。
+        void Update(UiWindowPresentationView view);
+        // 提示完了したrevision。初期0。managerの入力幾何公開に使う。
+        ulong PresentedGeometryRevision { get; }
+    }
+    public interface IUiWindowSource
+    {
+        Guid SourceId { get; }
+        UiWindowSourceSnapshot CaptureState();
+        // surfaceとsourceは借用。失敗/取消時も部分生成資源を回収する。
+        // DisposeAsyncは新規描画停止→GPU退役→登録解除の完了を意味する。
+        ValueTask<IUiWindowPresentation> PreparePresentationAsync(
+            UiWindowPresentationView view, UiSurface surface, CancellationToken cancellationToken);
+        // source同士のclient論理座標変換。推定できなければfalse。
+        bool TryTransformPoint(IUiWindowSource destination, Vector2 position, out Vector2 transformed);
+    }
+    public interface IUiWindowSourceAdapter : IUiWindowSource
+    {
+        // backend固有能力を調べる場合はこのchainを辿る。
+        IUiWindowSource InnerSource { get; }
+        // Nativeで新しいsourceを生成したときも公開ID/幾何revision変換を維持する。
+        IUiWindowSource AdaptSource(IUiWindowSource source);
+    }
+    public interface IUiWindowManager : IDisposable
+    {
+        Guid Id { get; }
+        UiWindowPresentations SupportedPresentations { get; }
+        // 既存提示rootを借用登録。同じSourceIdの重複を拒否する。
+        // keyboardRoot省略は自身。指定時は同じmanager/scopeの生存rootを要求する。
+        UiWindowRegistration Register(IUiWindowSource source, UiWindowId? keyboardRoot = null);
+        // 同期検証後に論理windowを作り、実準備はAdvance/Bindで進める。
+        UiWindowId Create(UiWindowCreateRequest request);
+        // 同じwindow/surfaceへの同時bindingを拒否。hidden Readyまでを待つ。
+        // 元surfaceを破棄せず、同じsurfaceをfallback先へ再bindingできる。
+        ValueTask<UiWindowBinding> BindPresentationAsync(UiWindowId window,
+            UiSurface surface, CancellationToken cancellationToken = default);
+        bool IsBound(UiWindowId window, UiSurface surface);
+        // Ready/Openの実提示rootを借用。Native/外部rootの終了はleaseの返却を待つ。
+        // Hostedから呼んだ場合も実際のPresentationRootのsourceを返す。
+        UiWindowSourceLease BorrowPresentationSource(UiWindowId window);
+        UiNativeWindowSupport GetNativeSupport(UiWindowId owner);
+        // request IDは再利用しない。ShowはReadyのみ。Closeは冪等。
+        UiWindowRequestId Place(UiWindowId window, UiWindowPlacement placement);
+        UiWindowRequestId Show(UiWindowId window);
+        UiWindowRequestId Close(UiWindowId window);
+        // Closed/Failedかつbindingなしのみ。終了snapshot/要求結果を退役させる。
+        void Release(UiWindowId window);
+        bool TryGetRequest(UiWindowRequestId request, out UiWindowRequestResult? result);
+        bool TryGetSnapshot(UiWindowId window, out UiWindowSnapshot? snapshot);
+        IReadOnlyList<UiWindowSnapshot> CaptureWindows();
+        // InputWindowIdの取得元から登録済み提示rootへ明示対応する。
+        UiWindowId? ResolveInputRoot(InputWindowId origin);
+        // 通常画面の配送先。Popup/Menu/Candidateの表示では変更しない。
+        UiWindowId? GetKeyboardTarget(UiWindowId window);
+        // 可視・binding済みNormal/Toolだけ。OS Activateを行わない。
+        void SetKeyboardTarget(UiWindowId window);
+        // 提示済み幾何・z順・clipで最前面を返す。Passiveは除外、Candidateは含む。
+        UiWindowHit? HitTest(UiWindowId presentationRoot, Vector2 position);
+        bool TryTransformPoint(UiWindowId from, UiWindowId to,
+            Vector2 position, out Vector2 transformed);
+        // owner threadのIdleで一frame一回。非同期結果をcommitする。
+        void Advance();
+        void BeginStop();
+        bool IsStopped { get; }
+    }
+    public sealed class UiWindowRegistration : IDisposable
+    {
+        public UiWindowId Window { get; }
+        // 子とbindingのdrain完了後true。source/既存Windowを破棄しない。
+        public bool IsReleased { get; }
+        public void Dispose();
+    }
+    public sealed class UiWindowBinding : IAsyncDisposable
+    {
+        public UiWindowId Window { get; }
+        // 冪等。描画の停止/GPU退役/参照解放を待ち、surfaceは破棄しない。
+        public ValueTask DisposeAsync();
+    }
+    public sealed class UiWindowSourceLease : IAsyncDisposable
+    {
+        public IUiWindowSource Source { get; }
+        // 発行元managerのID。wrapperは公開IDへ変換して扱う。
+        public UiWindowId PresentationRoot { get; }
+        public UiWindowId KeyboardRoot { get; }
+        // 冪等。保持だけを返し、source/Windowを直接破棄しない。
+        public ValueTask DisposeAsync();
+    }
+    public sealed class HostedUiWindowManager : IUiWindowManager
+    {
+        public HostedUiWindowManager(TimeProvider? clock = null,
+            string nativeUnavailableCode = "native-unavailable");
+    }
+    public sealed class FallbackUiWindowManager : IUiWindowManager
+    {
+        // 両managerを借用。稼働中はこのwrapperから専用利用し、同一owner thread。
+        public FallbackUiWindowManager(IUiWindowManager native,
+            IUiWindowManager hosted, TimeProvider? clock = null);
+    }
+}
+namespace Lumyte.UI.Windowing.WindowSystem
+{
+    public sealed record UiNativeWindowHostCapabilities(
+        bool CanObserveOutsidePress, bool CanCancelCrossWindowPointers);
+    public interface IUiNativePresentationFactory
+    {
+        // anchorのmonitor/work areaに入る有限かつ正のclient最大寸法。
+        Vector2 GetClientLimit(IWindow owner, Rect2D anchor);
+        // IWindowは借用。専用target作成・GPU退役はfactory実装が担当する。
+        ValueTask<IUiWindowPresentation> PrepareAsync(IWindow window,
+            UiWindowPresentationView view, UiSurface surface, CancellationToken cancellationToken);
+    }
+    public sealed class WindowSystemUiWindowSource : IUiWindowSource
+    {
+        // nodeは既存のNative提示root。inputはそのWindowを取得するsource。
+        public WindowSystemUiWindowSource(WindowNode node, IWindowInputSource input,
+            IUiNativePresentationFactory presentations);
+    }
+    public sealed class WindowSystemUiWindowManager : IUiWindowManager
+    {
+        // 全て借用。既存WindowSystemへNative生成・要求ID・完了照会を委譲する。
+        public WindowSystemUiWindowManager(Lumyte.Windowing.WindowSystem windows, IWindowInputSource input,
+            IUiNativePresentationFactory presentations, UiNativeWindowHostCapabilities capabilities,
+            TimeProvider? clock = null);
+        // optional Platform bridgeだけで利用。snapshotの実際のSourceIdを照合する。
+        public bool TryResolveNativeWindow(Guid sourceId, out IWindow? window);
+    }
+}
```

これらの実装はIUiWindowManagerのメンバーを実装する。省略した宣言は新しいメソッドの予約を意味しない。全操作はUI hostが一つの実行系列で直列化する。Advance再入はInvalidOperationException、破棄後はObjectDisposedException、不正なID・enum・寸法・role/mode組合せは状態変更前にArgumentException系で拒否する。連番/revisionのoverflowも変更前に失敗する。各snapshot/listは不変コピーで、後のAdvanceで変化しない。未確定のPresentation/SourceId/InputOriginはそれぞれnull/Guid.Empty/nullで、入力・描画の対象にしない。

登録rootは通常画面を含めBindPresentationAsyncでsurfaceを登録する。borrowed rootのPlace/Show/CloseはInvalidOperationExceptionとし、既存Windowの操作は元の所有者が行う。登録解除はUiWindowRegistration.Disposeから子の終了とbindingのdrainを要求する。IsReleased以前に元のsource/Windowを破棄しない。Createしたwindowだけをmanagerが所有し、CloseはbindingのGPU退役後に実Windowを閉じる。surfaceのDisposeは利用側の責務である。

CreateしたwindowのClosed/Failed snapshotと要求結果はReleaseまで保持する。利用側は終端を確認してReleaseし、終了IDを新しいwindowへ対応させない。登録rootは登録tokenのIsReleasedで同じ退役を行う。環境上の準備失敗ではsnapshotのFailureCodeを確定してBindPresentationAsyncをUiWindowOperationExceptionで失敗させる。主なcodeはowner-unavailable、source-unavailable、native-unavailable、hosted-unavailable、window-create-failed、placement-failed、presentation-failed、show-failed、opening-timeoutとする。caller取消はOperationCanceledExceptionとし、部分資源の回収は継続する。

### HostedとNativeの実装

HostedUiWindowManagerはWindowSystemを生成・参照しない。登録rootのsnapshotから論理的な子、描画順、祖先clip、hit testを管理し、source.PreparePresentationAsyncへ合成登録する。Popup/ContextMenu/submenuは提示root直下の兄弟とし、小さいownerやMenuの矩形でclipしない。NormalなHosted内容では親のclipを継承する。描画はNormal/Tool→Popup/ContextMenu→ImeCandidatesの順、同じ層では確定した生成順で前面へ置く。Passiveは入力透過、Candidateはpointer対象だがkeyboard targetを変えない。

Hostedの寸法は提示rootの可視client寸法へclampし、配置は希望方向→反対側→root内へclampする。配置はサイズを変更しない。sourceのScaleを使い、Window、swapchain、canvas、tabを新規生成しない。Browser/独自描画先でも同じ実装を注入できる。

WindowSystemUiWindowManagerはWindowSystemのNative生成・Place/Show/CloseとRequestCompletedを変換する。role・配置方向はadapter内で既存enumへ明示変換する。Native ownerはanchorの実際の提示rootで、WindowKind.Popup・PreserveFocus・非表示で作成する。InputOriginは注入したIWindowInputSource.TryGetInputWindowIdから取得し、別sourceや現在のactive Windowを使わない。配置上限はanchorのmonitor/work areaから取得し、screen変換をbackendへ委譲する。OSが採用した実寸法とpopup自身のScaleをsnapshotへ公開する。

Native能力はOwnedWindow/NonActivating/AnchorPlacement、origin付き入力、跨Window系列取消、描画準備、outside press/非活性化観測の全てを満たすときだけtrueとする。Codeは成功時空、失敗時browser-hosted-only、origin-input-unavailable、native-features-unavailable、native-renderer-unavailable、native-observation-unavailableに限定する。PlatformFeatures.NativeWindows単独や例外文字列から判定しない。各既存WindowSystem要求の完了と実状態を確認し、機能bitだけで成功扱いにしない。

IUiWindowSourceの描画実装は、root内容とHostedの複数bindingを同じ提示先へ順序通り合成する。presentation.Updateは最新の確定位置/clipを反映するが、入力のHitTestへ公開するGeometryRevisionはPresentedGeometryRevisionの完了まで旧値を維持する。未提示の初期windowはhitしない。UI-0004のClear/Load、UI-0007のDPI、UI-0008のGPU寿命を守る。Native source同士の座標変換が提供できなければTryTransformPointはfalseとし、routerは系列をCancelする。

### 準備・fallback・終了

Createは論理IDを発行し、BindPresentationAsyncが非表示の生成、寸法制限、surface.SetViewport/UpdateLayout、描画準備、初期Place完了までを進める。成功したbindingを返す時点がReadyで、まだShowを要求していない。managerは同じsurfaceを借用し、内容factory・callbackは実行しない。状態のcommitとlayoutはowner threadのAdvanceで行い、待機中もOS pumpとAdvanceを続ける。

FallbackUiWindowManagerは登録rootを両実装へ借用登録し、公開UiWindowIdを自身のnamespaceで一意に対応付ける。rootのsurfaceは一度だけbindingし、二つのroot描画を作らない。InputOrigin/SourceId、snapshot、要求ID、hit test、keyboard target、座標変換も同じ公開ID空間へ変換する。内側managerのIDを公開せず、Hosted overlayを含めて実際の提示順でhit testする。生成方式の分岐をUiPopupManagerへ戻さない。

Nativeで開いたPopupのsubmenuをHostedへfallbackする場合、wrapperはNative側のBorrowPresentationSourceで実際のNative popupのsourceを借りる。このsourceをHosted側へ内部rootとしてRegisterし、keyboardRootには元editor/ownerのKeyboardRootをHosted側IDへ写した値を明示する。新しい提示rootをOS focus rootと取り違えない。この内部rootは公開windowを増やさず、Native側の公開UiWindowIdへ対応付ける。Native側のsurfaceを再bindingせず、Hosted submenuのbindingだけを同じ提示sourceへ追加する。

Hosted内のanchorからNativeを作る場合は、wrapperが公開Owner/AnchorWindowから実際のPresentationRootを求め、anchor矩形をそのclient論理座標へ一度変換してNative側IDへ渡す。OS ownerにはそのNative提示rootを使い、論理OwnerとKeyboardRootは元の公開IDで保持する。Hostedの内側IDをNative managerへ渡さない。source共有・座標変換ができない組合せはnative-unavailable/hosted-unavailableで失敗し、IDや座標を推測しない。

内部rootへの全子・bindingを先にdrainし、内部登録のDispose→IsReleased→source leaseのDisposeAsyncの順で返す。元のNative Closeはsource lease返却まで実破棄を待つため、子のHosted描画より先に親の提示先が消えない。共有する内部root/leaseは参照数で管理し、最後の利用が終わった時点で返す。wrapper自身の停止も同じ子→親の順で進め、source lease待ちの循環を作らない。

| Policy | 選択と失敗 |
| --- | --- |
| HostedOnly | 注入したHosted managerだけを使う |
| PreferNative | Native能力があればNativeで準備。未対応またはShow発行前の準備失敗なら完全清掃後にHostedへ再準備 |
| RequireNative | Nativeだけを使う。未対応・失敗はFailedで返す |

HostedUiWindowManager単独でもHostedOnly/PreferNativeを受け、後者は未対応理由をFallbackCodeへ残す。RequireNativeはnative-unavailableとなる。Native単独実装はHosted生成を行わず、HostedOnlyはhosted-unavailableを返す。FallbackUiWindowManagerはfallback実装がHostedを提供できることを構築時に検証し、別のWindowSystemや隠れた既定managerを生成しない。

Nativeの準備失敗時は、取消→遅延完了token回収→GPU退役と描画登録解除→surface/Native保持参照解放→Native Close→Closed確認の順に清掃する。その後だけ同じsurfaceと論理UiWindowIdでHostedを試す。試行の世代とrevisionを更新し、旧試行の遅延結果を新試行へ適用しない。Contentは一回だけ構築され、同時に二つの表示先へbindingされない。Failed状態にはbindingを返さず、取消済みTaskの部分資源も回収する。

各方式の準備とShow処理には既定2秒の単調時計予算を共有する。Readyで利用側がShowを待つ時間は算入せず、候補presenter等は非表示Readyを保持できる。Show要求時に残りの予算を再開する。UiPopupManagerはReady確認後直ちにShowを要求する。Hostedへ切り替える場合は清掃完了後に新しい予算を開始する。清掃が遅れても新しい方式を先行開始せず、Preparingの清掃状態で待つ。非同期token回収はホストが有限時間で完了できることを契約とし、Task.Wait/Resultで停止しない。

Showの発行を不可逆な境界とし、ShowIssuedを先に固定する。Show発行後の失敗/timeoutとOpen後のdevice/presenter喪失では清掃してFailed/Closedへ進み、Hostedへ自動移送しない。Showはrequest IDの成功と実際の可視状態の両方を確認してOpenになる。Placeも要求IDと実寸法を追跡し、OSの修正寸法を毎frame書き戻すループを作らない。

Closeは子→親の順に新規描画を止め、bindingのGPU退役を待ってから生成Windowを閉じる。先にAutomation/Diagnostics/IME等をdrainする責任は利用側にあり、UiPopupManagerはその終了参加者を待ってからbindingを返す。BeginStop後は新規登録/生成を拒否し、Advanceを続けIsStoppedになってからDisposeする。wrapperは自分の登録/生成だけをdrainし、借用した内側managerやsourceをDisposeしない。

### 入力・候補表示・操作権

入力はInputWindowId→ResolveInputRoot→提示済みHitTest→対象surfaceの順で配送する。内部のHosted候補へOSのoriginを割り当てず、実取得元のInputWindowIdを保持する。公開UiWindowIdを維持して内側managerのID/SourceIdがfallbackで切り替わっても、既に開始したpointer/key系列の取消先は元の所有表へ固定する。Show前の切替には新規押下を受け付けず、表示後には切り替えない。

CandidateはIMEの独自候補presenter用であり、Menu/Passiveとは別の入力modeとする。最前面のCandidateに対するpointer系列は候補controllerの専用routeへ先に渡し、消費後にMenuのoutside press判定や背面Buttonへ再配送しない。keyboardは編集元KeyboardRootのTSF/DOM処理へ残し、Activate/MoveFocusしない。OS/TIP自身の候補Windowはmanagerへ登録しない。候補Widgetとcontrollerの詳細は同じ後続編集PRで扱う。

Automationの入力leaseはUI-0009のhostが保持する。独立したNative root同士でも同じmouse/keyboard deviceを共有する場合は、hostが同じ物理入力domainで排他を取る。Diagnosticsの登録rootごとのControlGroupIdだけではこの排他を代用できない。domainの割当は実取得source/deviceの共有関係からホストが明示し、同時送信・外部入力割込を防ぐ。終了時はWindowing/Hostedの境界を跨いでも、取得したleaseの元router/系列へCancelを送り終えてから排他を返す。

### 注入例

```csharp
var hosted = new HostedUiWindowManager();
var native = new WindowSystemUiWindowManager(windowSystem, inputSource, presentations, capabilities);
var windows = new FallbackUiWindowManager(native, hosted);
var rootSource = new WindowSystemUiWindowSource(nativeRoot, inputSource, presentations);
using var root = windows.Register(rootSource);
await using var rootDrawing = await windows.BindPresentationAsync(root.Window, rootSurface);
var popups = new UiPopupManager(windows);
using var owner = popups.RegisterOwner(root.Window, rootSurface);
```

例の非同期処理はOS pump/Advanceが動くUI host上で進める。Browserや独自canvasではIUiWindowSourceの実装とHostedUiWindowManagerを直接渡せばよい。終了はPopup停止・owner解放→rootDrawingのGPU drain→root登録解除→windows停止/Dispose→native/hosted停止/Dispose→元Window/Platformの順とし、using宣言だけで非同期drainを省略しない。

## 検討した代替案

- IUiPopupHostへWindowSystemを包むだけでは、通常画面の登録、state、hit test、keyboard targetが具体型に残る。これらをIUiWindowManagerへまとめ、描画だけをIUiWindowSourceへ分ける。
- UiPopupManagerでNative/Hostedを分岐すると、独自候補presenter等で同じfallbackと清掃を再実装することになる。managerの合成で選択する。
- Native失敗時に既存WindowSystemを暗黙Hosted化する案は採らない。fallbackは明示的に注入されたmanagerとPolicyに限定する。

## 結果と影響

アプリは同じUIへNative adapter、独立Hosted実装、独自managerを注入できる。通常画面からPopup/候補まで同じID・snapshot・描画bindingを使える。一方、wrapperはID変換と複数実装の終了待ちを管理し、ホストは実入力出所と提示sourceを正確に対応させる必要がある。

初期の新規生成はHosted内容と一時Windowに限定し、Tool/文書Windowの新規生成、一般modal、任意のOS z-order、Window間ドラッグは追加しない。既存の通常Windowは同じRegister/binding/snapshot契約で利用する。

## 検証方針

- WindowSystemを参照しない独自IUiWindowManagerとHosted実装をUiPopupManagerへ注入し、通常root登録・内容表示・配置・入力・終了が同じ契約で動作することを確認する。
- IDの異manager/default/再利用拒否、不変snapshot、root借用、登録重複、InputWindowIdの明示対応、未対応sourceの推測禁止を確認する。
- Native adapterとHostedで同じ位置・clip・z順・DPI・keyboard targetを観測し、Candidate/Passive/Interactiveの入力を区別する。
- Create/Bind/Place/Showの失敗、取消、遅延Task、GPU実行中Closeについて、fallback前の完全清掃、同じcontent一回、同時binding禁止、Show後の移送禁止を確認する。
- Native Popup内のHosted submenu、Hosted anchorからのNative生成、内部rootのsource lease保持と返却、元editorのKeyboardRoot維持を確認する。親NativeのCloseが子のHosted GPU退役を追い越さないことを検証する。
- root/Popup/submenu/候補の終了、borrowed source保持、複数Nativeで共有する入力domainの排他、旧系列へのCancel、IsStopped/IsReleased前の破棄拒否を確認する。
- BrowserでHosted managerだけを使い、追加Window/canvasを作らず全操作が成立することを確認する。Native実機未確認の能力は利用可能と報告しない。

## 実装時の具体化

描画viewへIsVisibleを追加し、非表示準備とShowの合成参加を区別する。sourceは非表示でも準備済みgeometryを確認でき、可視性を含む更新を実際のframe完了後にackする。Hostedの理由は既定native-unavailableとし、Browser構成ではbrowser-hosted-onlyを明示する。

managerの公開境界にはthread ID/UiSurface phase検査を追加しない。利用側が直列化し、Native adapterでは既存WindowSystem/Platformのスレッド条件を守る。source/presentationの非同期完了はAdvanceがcommitし、元の取得originを推測しない。

Native fixtureは実WindowSystemの要求ID・PreserveFocus・取得origin・表示前の清掃・Hosted切替・Native親内のHosted submenuを検証する。Windows/Linuxの実OS操作は未検証として残す。

fallbackはsource/presentationを透過adapterで包み、描画viewにもwrapperの公開WindowIdと単調なGeometryRevisionを渡す。提示完了ackを内側revisionへ戻し、HitTestには同じ公開revisionを返す。Native実装はIUiWindowSourceAdapterを辿って実sourceを解決し、生成sourceへAdaptSourceを適用する。これによりNative親内のHosted表示や逆方向の切替でも内側IDを描画ホストへ漏らさない。

通常Hosted画面のpointer DownではSetKeyboardTargetで論理的な配送先を選ぶ。Popup/Menu/Candidateでは呼ばず、一時scopeを閉じれば元の通常画面へ戻る。OSのKeyboardRootとUI内の通常画面の選択を混同しない。

## 全体レビューに基づく更新境界

Fallbackの要求対応表は進行中の要求だけを保持し、終端結果を毎turn再生成しない。公開結果はWindow.Releaseまで安定して取得できる。任意のIUiWindowRequestLifetime.ReleaseRequestは終端結果を早期解放し、pendingにはfalseを返す。解放後の同handleの照会はfalseとなり、処理の取消やWindow状態の変更は行わない。標準Hosted/WindowSystem/Fallbackはこの能力を提供し、Fallbackは自身で発行した内側要求の終端結果も解放する。HitTestは内側managerが決めた提示済みの順序を公開IDへ投影する。
