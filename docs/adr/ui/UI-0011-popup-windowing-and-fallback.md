# ADR-UI-0011: ポップアップの別Window連携とBrowserのHostedフォールバック

- 状態: 採用
- 日付: 2026-10-28
- 対応: 実装PR 10、前提: PR 6〜9

## 背景

メニュー、サブメニュー、ドロップダウン等は、アンカーとなるWidgetのclip外へ表示し、元画面のOSフォーカスを保ちながら入力を受ける必要がある。Nativeの別Windowなら元のクライアント領域外へ表示できるが、独立した入力出所、描画先、DPI、非同期終了の管理が必要になる。Browserは既存canvas一つをIWindowとして扱い、Nativeの子Windowを生成できない。

[WINDOW-0001](../window/WINDOW-0001-window-system.md)はNativeとHostedを区別し、Windowing自身の暗黙フォールバックを禁止している。またPreserveFocusの一時Windowを表示してもWindowSystem.KeyboardTargetは変わらない。[UI-0007](UI-0007-window-input-and-dpi-integration.md)の配送だけでは、メニューへキーを渡すことも、別surfaceの祖先メニューと協調することもできない。

## 決定

### 配置と対象

任意の`src/UI/Lumyte.UI.Windowing`へUiPopupManagerと通常画面を含む入力routerを置く。[UI-0014](UI-0014-injectable-window-manager.md)のIUiWindowManagerをコンストラクターで注入し、WindowSystem/WindowNodeを公開境界へ含めない。Native adapterは別package、Hosted managerは独立実装とする。UI本体にはfocus保存・復帰に必要な小さい契約だけを追加する。PR 6のrouter規則を引き継ぎ、通常画面も同じmanagerへ借用登録して配送する。

対象は`UiWindowRole.Popup/ContextMenu`の一時表示と、その受動表示（tooltip等）である。内容は通常のCompositionで構築する。PopupをWidgetの第二の子スロットにせず、独立したUiSurfaceへ一度だけ接続する。独立したTool/文書Window、OSのメニューバー、一般modal dialog、IME候補、ドラッグによるWindow間移送、Text/scrollコントロールはこのPRに含めない。

```text
Composition → Popup内容Widget → Popup専用UiSurface
アプリ → UiPopupManager → 注入したIUiWindowManager
                          ├ Native adapter
                          └ Hosted manager（任意fallback）
                          ↓ 描画binding
                       既存Drawing2D・RenderGraph・GPU
UI.Windowing.Automation → UI.Windowing + UI.Automation
Diagnostics.UI.Automation → Popupごとの対象登録（通信は既存Diagnostics）
```

### 表示方針と環境の対応

要求policyはUiWindowPolicyを使い、既定はHostedOnlyとする。Native/Hostedの能力判定、表示前の清掃とfallback、表示後の移送禁止はUI-0014のmanagerが実装する。UiPopupManagerは方式を分岐せず、確定したPresentation/FallbackCodeをhandleとOnOpenedへ反映する。

| 環境・能力 | HostedOnly | PreferNative | RequireNative |
| --- | --- | --- | --- |
| Windows/Linux、所有付き非活性Window・配置・出所付き入力・rendererが揃う | 注入したHosted | Native。表示前の失敗は清掃後Hosted | Native。作成不能なら失敗 |
| Native backendだが必要条件の一部がない | Hosted | Hostedと理由を返す | native-unavailable |
| Windowの出所・跨Window系列取消を提供しない入力source | Hosted | Hosted（origin-input-unavailable等） | native-unavailable |
| BrowserにHosted managerを注入 | 同じcanvasのHosted | Hosted（browser-hosted-only） | native-unavailable |
| rootが非表示・0寸法・終了中 | owner-unavailable | owner-unavailable | owner-unavailable |

NativeではINPUT-0007の取得時InputWindowIdと実際のpresentation sourceを対応させる。既存InputRecordに後からWindowを推定して追加せず、必要な取得・取消・outside press観測を実環境で確認したadapterだけが利用可能とする。WindowSystem自身の暗黙フォールバックは追加しない。

Browserではwindow.open、新しいtab、別canvasの暗黙作成を使わない。Hostedはuser activationやpopup blockerの成功を前提とせず、canvas内だけで描画・操作する。ブラウザーのviewport、canvas、fullscreen要素の外へ出られるとは保証しない。DOMのcontextmenuを抑止する場合はアプリが対象canvas内で同期的にpreventDefaultし、開く要求をqueueする。右ボタン認識はホスト側で行い、既存UIの左ボタンClickへ偽装しない。ブラウザー標準メニューをUIが置換したとみなさない。

canvas外へのpointer downはホストのdocument observerからNotifyOutsidePressへ通知し、blurに頼らず閉じる。そのDOM入力自体は他の要素へ通常どおり届き、UIがページ全体をmodal化しない。observerはpopupがある間だけ登録し、canvas削除・host終了時に解除する。canvas内のoutside pressの系列消費は以下のrouter規則に従う。

### 公開APIとcallback

比較元はmain `20f666ff8f64a63e65407936d32fb98cfa19c9d8`、UIの前提はUI-0002〜0010のAPIとする。UiRemoteTargetの変更行はUI-0010の宣言との差分を示す。Widgetは`Lumyte.UI.Compose.Definitions.Widget`の略記である。

```diff
+namespace Lumyte.UI.Windowing
+{
+    public enum UiPopupInputMode { Menu, Passive }
+    public enum UiPopupState { Pending, Opening, Open, Closing, Closed, Failed }
+    public enum UiPopupCloseReason
+    {
+        Requested, Escape, OutsidePress, OwnerUnavailable, AnchorUnavailable,
+        Replaced, PresenterLost, OpenFailed, Shutdown
+    }
+    public sealed record UiPopupOpened(Guid PopupId, UiWindowPresentation Presentation,
+        string FallbackCode);
+    // Codeは成功時空、失敗時は下記の固定code。終了通知は成功/失敗を通じて一度。
+    public sealed record UiPopupClosed(Guid PopupId, UiPopupCloseReason Reason, string Code);
+    public sealed class UiPopupRequest
+    {
+        // anchorはowner/親popupの通常内容Widget。clientSizeはfiniteかつ各軸正。
+        // roleはPopup/ContextMenuのみ。gapはfiniteかつ非負。
+        public UiPopupRequest(UiElement anchor, Vector2 clientSize, Func<Widget> createContent,
+            UiWindowPolicy policy = UiWindowPolicy.HostedOnly,
+            UiWindowRole role = UiWindowRole.Popup,
+            UiWindowPlacementSide side = UiWindowPlacementSide.Below, float gap = 0,
+            UiPopupInputMode inputMode = UiPopupInputMode.Menu,
+            Action<UiPopupOpened>? onOpened = null, Action<UiPopupClosed>? onClosed = null);
+    }
+    public sealed class UiPopupManager : IDisposable
+    {
+        // windowsは借用。clock省略時System。同じowner threadで作成する。
+        public UiPopupManager(IUiWindowManager windows, TimeProvider? clock = null);
+        // windowとsurfaceは既に同managerでbinding済み。同じ組の重複を拒否する。
+        public UiPopupOwner RegisterOwner(UiWindowId window, UiSurface surface);
+        // callback内でも可。資源取得やlayoutはここで行わず要求をqueueする。
+        public UiPopupHandle RequestOpen(UiPopupOwner owner, UiPopupRequest request);
+        public UiPopupHandle RequestOpen(UiPopupHandle parent, UiPopupRequest request);
+        // InputはOriginのNative client論理座標。通常画面も含め一度だけ配送する。
+        public bool Dispatch(UiWindowInputEnvelope input);
+        // ownerのclient外のpress観測。Browser document等からUI threadで通知する。
+        // 外部DOM/OSの入力を合成せず、当該chainの終了要求だけをqueueする。
+        public void NotifyOutsidePress(UiPopupOwner owner);
+        // 全surfaceのIdle時、一frame一回。Taskを同期waitしない。
+        public void Advance();
+        public IReadOnlyList<UiPopupInfo> CapturePopups();
+        // owner threadのIdleのみ。保持する登録はBindLifetimeで終了drainへ参加。
+        public bool TryGetSurface(Guid popupId, out UiSurface? surface);
+        // Open時のみ。同じtokenの重複を拒否。終了時のDisposeAsyncをmanagerへ移譲。
+        // Automation/Diagnosticsの取消完了を待ってから描画先・surfaceを解放する。
+        public void BindLifetime(Guid popupId, IAsyncDisposable participant);
+        public void BeginStop();
+        public bool IsStopped { get; }
+        // BeginStop後、Advanceを続けIsStoppedになってから呼ぶ。
+        public void Dispose();
+    }
+    public readonly record struct UiWindowInputEnvelope(InputWindowId Origin, UiInput Input,
+        InputDeviceId? Device = null);
+    public sealed class UiPopupOwner : IDisposable
+    {
+        public Guid Id { get; }
+        // 新規受付停止→popup全終了→入力清掃後true。借用対象を破棄しない。
+        public bool IsReleased { get; }
+        public void Dispose();
+    }
+    public sealed class UiPopupHandle
+    {
+        public Guid Id { get; }
+        public UiPopupState State { get; }
+        public UiWindowPresentation? Presentation { get; }
+        // 未発生時空。fallbackは成功情報、FailureCodeは失敗情報。
+        public string FallbackCode { get; }
+        public string FailureCode { get; }
+        // 冪等でqueueだけを行う。Close callbackからも呼べる。
+        public void Close();
+    }
+    // detachedな値。未接続のPendingではSurfaceIdはGuid.Empty。
+    public sealed record UiPopupInfo(Guid Id, Guid OwnerId, Guid? ParentPopupId,
+        Guid SurfaceId, UiPopupState State, UiWindowPresentation? Presentation,
+        UiPopupInputMode InputMode, string FallbackCode);
+}
+namespace Lumyte.UI
+{
+    public enum UiFocusNavigation { First, Last, Next, Previous }
+    // surface IDと対象の接続世代を内包し、外部から構築・変更できない値。
+    public sealed class UiFocusBookmark { }
+    public sealed class UiSurface
+    {
+        // Idleのみ。focusなしも記録する。WindowやWidgetを操作しない。
+        public UiFocusBookmark CaptureFocus();
+        // 別surfaceのbookmarkはArgumentException。外された/再接続した対象はfalse。
+        // 不適格ならfalseで変更なし。成功時は入力ownerをClickなしで取消。
+        // focusなしのbookmarkはfocusを解除してtrue。
+        public bool TryRestoreFocus(UiFocusBookmark bookmark);
+        // UI-0006の候補順/適格性で移動。候補なしfalse。キーcallbackを合成しない。
+        public bool MoveFocus(UiFocusNavigation navigation);
+    }
+}
```

factoryとcallbackは要求パラメーターとして一つだけ保持し、public eventによる追加購読を設けない。CreateContentは最初のAdvanceで一度だけ呼び、親・surface付きrootやnullを返した場合とfactory例外はinvalid-contentで失敗する。fallbackでも再実行しない。

OnOpened/OnClosedはmanagerが状態を確定してからowner thread上で呼び、内部列挙中に呼ばない。callbackが要求したopen/closeは次のdrainで処理し、Advance/Dispatchの再入は禁止する。callback例外は他の清掃と通知を終えてからAdvanceの呼び出し元へ報告し、完了済みcallbackを再実行しない。

引数違反と別thread/phase違反は同期的なArgumentException系/InvalidOperationException、終了後操作はObjectDisposedExceptionとする。BeginStop後の新規要求はInvalidOperationExceptionとする。queue済みの環境上の失敗はhandleのFailedとOnClosedの固定codeへ返す。主なcodeはowner-unavailable、anchor-unavailable、invalid-content、native-unavailable、native-open-failed、presentation-failed、opening-timeout。例外の文字列を成功receiptやfallback理由へ載せない。

利用側はイベントhandlerから同期的に要求をqueueする。内容のButtonも通常のcallbackパラメーターで構築する。次の例のAutomationNameは意味情報で、Textの描画機能を表すものではない。

```csharp
UiPopupHandle OpenMenu(UiElement anchor)
{
    UiPopupHandle? menu = null;
    menu = manager.RequestOpen(owner, new UiPopupRequest(
        anchor, new Vector2(240, 160),
        createContent: () => Stack(orientation: UiOrientation.Vertical)
        [
            Button(width: 220f, height: 32f, automationName: "保存", onClick: () =>
            {
                menu?.Close();
                Save();
            })
        ],
        policy: UiWindowPolicy.PreferNative,
        onClosed: result => ReportPopupClosed(result)));
    return menu;
}
```

### 論理的な親と表示上の親

一つのownerにMenuのroot chainは一つとし、新しいroot要求は旧chainをReplacedで閉じ、清掃完了後に開く。各menuにも子submenuは一つで、新しい子は旧子の終了を待つ。owner登録ごとのroot menu数とは別に、keyboard scopeはOS active Nativeを共有する範囲で一つに制限する。Passiveは既定最大8個、menu chainは最大深さ8とし、満杯時は変更前に拒否する。Passiveをsubmenuの親にはしない。

Popupの論理親はanchorを持つowner/親popupであり、寿命・入力scope・focus復帰を決める。managerの表示上の親と同一にはしない。anchorは通常内容で可視・有効、祖先を含むclipとviewportの交差面積が正であることを開く条件とする。取り外し・再接続・不可視化・clipで完全に隠れた場合はAnchorUnavailableで閉じる。Widgetの再接続世代を保存し、別の接続として再利用された同じ参照に追従しない。

表示上の親、Hostedの提示root直下への配置、Native owner、PreserveFocus、z順はUI-0014のmanagerに委譲する。UiPopupManagerはanchorのsurfaceに対応するUiWindowIdと確定Boundsを渡し、論理親の終了を子から親へ伝播する。通常内容の後にPopup/ContextMenuを描き、ImeCandidatesの層を追い越さない。

PassiveはUiWindowInputMode.Passiveへ、MenuはInteractiveへ対応させる。HitTestは注入managerを通し、Passiveを除いた提示済みのz順・祖先clipを使う。候補表示のCandidate modeはMenu/Passiveとは別に扱い、候補controllerが消費した系列をMenuのoutside判定へ重ねて配送しない。

### 寸法・配置・DPI

初期APIはClientSizeを明示する。intrinsic計測、内容サイズによるWindow伸縮、overflowの自動scrollは追加しない。managerが要求寸法を提示rootまたはmonitor/work areaへ制限し、同じsurfaceへSetViewport/UpdateLayoutを適用する。項目が収まらない場合は呼び出し元がページ分割等を設計する。

anchorはWidgetの確定Boundsをownerのclient論理座標へ変換した矩形とし、UiWindowPlacementのAnchorWindowとともに渡す。Gapも同じ単位である。希望方向→反対側→clamp、Nativeのscreen変換、popup自身または提示rootのScaleはUI-0014の契約を使い、NativeのBoundsからscreen位置を推測しない。

Place/ShowのUiWindowRequestIdとsnapshotを追跡し、要求成功と実寸法・可視状態を確認するまでOpen/OnOpenedにしない。OSが別ClientSizeを採用した場合は実寸法を次frameのtight制約へ反映し、要求寸法を書き戻し続けない。

anchor移動、root resize、DPI、将来のscrollによる配置変更はlayout確定後にPlaceを要求する。入力はmanagerが公開した提示済み幾何を使う。owner/popupが0寸法や最小化・非表示なら入力を止めて閉じる。別提示先の座標変換に失敗したらmanager.TryTransformPointのfalseを受け、系列をCancelして残りを消費する。

### keyboard scopeとpointer系列

MenuをOpenにする直前にowner/親menuのfocus bookmarkを保存し、旧surfaceへFocusLostを渡して押下を取消す。新menuの最初のfocus候補へMoveFocus(First)する。IUiWindowManager.GetKeyboardTargetは元の画面を維持し、UiPopupManagerだけが有効なmenu chainへkeyboard配送を上書きする。UI内focus復帰のためにNative ActivateやHosted Activateを呼ばない。

MenuではDown/Upの配送先を固定し、最も深いOpenのmenuがキーを受ける。Tab/Shift-Tabと上下矢印はNext/Previous、Home/EndはFirst/Last、Enter/Spaceは既存Buttonの通常Dispatchを使う。Escapeは最深menu、Leftはsubmenuを閉じる。Rightによる自動submenu展開、hoverによる遅延展開、ローカライズされた文字検索は対象外で、submenuはButtonのOnClickからRequestOpenする。認識したnavigation/Escape/Leftはrepeatと物理Upまで消費し、閉じた後のUpをownerへ渡さない。未処理キーもMenu scope内で消費してゲームのショートカット発火を防ぐ。

Popup表示の原因となったpointer/key系列は配送途中で新popupへ移さない。Menuの外側でDownしたらchainをOutsidePressで閉じ、そのDown/Move/Upを終端まで消費する。同じ入力で背面Buttonを押す動作を行わず、次の独立したDownから通常画面を操作できる。親menu内へのDownは子submenuだけを閉じて親へ一度配送する。この場合はDownで決めた親のfocusを優先し、子のbookmark復帰で上書きしない。別のHosted/native surfaceに入っても、一度始めた系列を他のButtonへ受け渡さない。Passiveの表示・終了はfocusを動かさず、系列を奪わない。

ホストはInputWindowIdのoriginをイベント取得時に確定し、ResolveInputRootで対応する提示rootを確認してUiPopupManagerへ順番通り渡す。UI-0007の重複除去、欠落時Reset、同じ物理系列の固定配送を引き継ぐ。Native originだけが変わって同じmouse device系列のUpが来た場合も元のownerを識別できるよう、hostはNative popup条件としてdevice/contactの安定IDを提供する。unknown originはbroadcastしない。cross-originの位置を変換できなければCancel後の終端を消費する。

親Nativeの非活性化、canvasのblur、入力履歴欠落、device切断ではchain全体を取消して閉じる。PopupのPreserveFocusに伴う観測と親Nativeの実際の非活性化を混同しない。close時はpopupの入力を取消した後、親menuまたはownerがまだ同じ接続で操作可能ならbookmarkを復帰する。対象が消えたらfocusを空にし、OSが非活性の間は復帰を保留せず破棄する。終了した別画面へActivateして復帰させない。

### 開く・失敗・終了の順序

```text
Pending → Opening → Open → Closing → Closed
              └→ 部分資源の清掃 → HostedでOpening（PreferNative、表示前のみ）
              └→ 部分資源の清掃 → Failed
Pending / Opening → Closing → Closed（Close/owner終了）
```

UiPopupManager.Advanceは要求drain、owner/anchor検証、完了Taskの回収、終了参加者のdrainとアプリcallbackを行う。IUiWindowManager.AdvanceはOS要求・描画準備・fallbackの完了を処理し、ホストは先にwindow managerを、次にPopup managerを一frame一回進める。Task.Wait/Resultでframeを止めない。

開く順序は次のとおりとする。

1. CreateContentを一度だけ実行し、独立UiSurfaceへ接続する。
2. 内容を作り直さず、共通policy・role・input mode・anchorを渡してIUiWindowManager.Createする。
3. BindPresentationAsyncで非表示Readyを待つ。managerは同じsurfaceを使ってNative/Hostedの準備・必要なfallback清掃を行う。
4. Ready snapshotを確認し、Showを一度要求する。要求成功とOpenの実状態を確認してPopupのOpenとOnOpenedを確定する。

各方式の2秒deadline、Show発行前だけのfallback、遅延Taskを含む完全清掃はUI-0014へ従う。Opening中の入力は消費して背面へ渡さず、PopupのOpen commit前に内容callbackを呼ばない。全方式が失敗した場合はReason=OpenFailedで一度通知する。factory/OnClickを再実行せず、同じsurfaceを二つの表示先へ同時登録しない。

Open後のdevice/presenter喪失はPresenterLostで閉じ、再表示には新しい要求・PopupIdを使う。終了は子→親の順に入力停止→Automation/Diagnostics/IMEの終了参加者drain→binding.DisposeAsyncによる描画停止・GPU退役→surface.Dispose→window manager.CloseとClosed確認→Release→通知・参照解放とする。待機中もOS pump、window manager、Popup managerのAdvanceを続ける。

UiPopupManagerは注入managerとownerのwindow/surfaceを借用し、生成Popupのsurface接続、UiWindowId、描画bindingだけを所有する。実Native/Hostedの所有権はIUiWindowManagerが管理する。owner解放はchain全終了を待ち、元のroot登録やWindowを閉じる前にUiPopupOwner.Dispose→IsReleasedを確認する。外部から先に親が閉じた場合も新規描画を止めて清掃するが、その順序でbackendのGPU使用が安全になるとは保証しない。

IMEとの連携は[UI-0012](UI-0012-ime-and-text-editing.md)のPR 11で具体化する。Menuのopen前guardでcomposition終了をdrainし、IMEが消費したEnter/Escape/矢印をMenuへ渡さない。OS候補Windowを通常Popupとして生成・診断登録しない。OS focusと描画先を区別してcaret位置を渡す。同PRで追加するmanagerのProbePointerへrouting hostも委譲し、TextBox adapterがDownを消費する点を親Buttonの操作可能点として返さない。

### AutomationとDiagnosticsの接続

Popupは独立surfaceなので[UI-0009](UI-0009-ui-automation.md)のpageから通常子として検索しない。任意の`Lumyte.UI.Windowing.Automation`がmanagerの確定したOpen/Closing状態をowner thread上で読み、PopupごとのIUiAutomationHostとUiAutomationPageを生成する。host factoryは登録したsurfaceのSurfaceIdを検証し、実際のNative/Hosted合成描画と同じ入力router・clip・leaseを使う。Core UI.WindowingからAutomationへ依存しない。

UI-0009の初期driverはsurfaceへ直接Dispatchするため、hostを差し替えるだけではmenu scopeを通らない。次の任意routing契約をUI.Automationへ追加し、driverのprobe、key対象判定、合成入力、取消を全て同じ経路へ切り替える。この契約を実装しない単一surface/headless hostは従来どおりUiSurfaceを使う。

```diff
+namespace Lumyte.UI.Automation
+{
+    // 各メソッドはRunOnUiThreadAsync内のIdleで呼ぶ。surfaceはhostの対象だけ。
+    public interface IUiAutomationRoutingHost
+    {
+        // popup/ancestor clip/前面Windowを含む実配送先。別surfaceならnullのprobe。
+        // 前段で入力を消費する部品も実配送と同じ判定を使い、親Buttonへfallbackしない。
+        UiPointerProbe ProbePointer(UiSurface surface, Vector2 logicalPosition);
+        // 最深menu等、実際のkeyboard scopeであることを確認する。
+        bool CanReceiveKeys(UiSurface surface);
+        // surface論理座標からOrigin/root座標へ変換し、managerへ一度配送する。
+        bool Dispatch(UiSurface surface, UiInput input);
+    }
+}
+namespace Lumyte.UI.Windowing.Automation
+{
+    public sealed class UiPopupAutomationSession : IAsyncDisposable
+    {
+        // 全hostは借用。dispatcherHostはmanagerと同じUI threadへ配送する。
+        public UiPopupAutomationSession(UiPopupManager manager, IUiAutomationHost dispatcherHost,
+            Func<UiSurface, IUiAutomationHost> hostFactory);
+        public Task<IReadOnlyList<UiPopupInfo>> ListPopupsAsync(
+            CancellationToken cancellationToken = default);
+        // 明示IDのpageを返す。各操作時にOpenを検証し、他はpopup-unavailable。
+        public UiAutomationPage Popup(Guid popupId);
+        // owner threadで登録する。rootにも同じrouterを使う。
+        public IUiAutomationHost CreateOwnerHost(UiSurface surface);
+        public IUiAutomationHost PopupHost(Guid popupId);
+        public ValueTask DisposeAsync();
+    }
+}
+namespace Lumyte.Diagnostics.UI.Automation
+{
+    public sealed class UiAutomationDiagnosticsRuntime
+    {
+        // parentは同じruntimeの生存中登録。PopupId/TargetIdは再利用しない。
+        // parentを辿ったrootと同じcontrol groupへ登録する。
+        public UiAutomationDiagnosticRegistration RegisterPopup(
+            UiAutomationDiagnosticRegistration parent, Guid popupId, string name,
+            UiSurface surface, IUiAutomationHost host,
+            UiAutomationTargetOptions? options = null);
+    }
+}
+namespace Lumyte.Diagnostics.UI.Automation.Contracts
+{
-    public sealed record UiRemoteTarget(Guid Id, string Name, bool AllowInput, bool AllowScreenshots);
+    public sealed record UiRemoteTarget(Guid Id, string Name, bool AllowInput, bool AllowScreenshots,
+        Guid? ParentTargetId = null, Guid? PopupId = null, Guid ControlGroupId = default,
+        string AutomationCode = "");
+}
+namespace Lumyte.Diagnostics.UI.Automation.Client
+{
+    public sealed class UiRemoteControl
+    {
+        // 同じgroupの生存中Targetだけ。同じcontrol・heartbeat・Sequenceを共有する。
+        public UiRemotePage PageFor(Guid targetId);
+    }
+}
```

Sessionの公開操作はdispatcherHostへqueueし、factoryとTryGetSurfaceはowner threadだけで使う。Popupのpageを保持しても終了したsurfaceを別のPopupへ再対応させない。windowing hostは上記routing契約を必須とし、AcquireInputAsyncはroot/Popup/submenuで共有するgroupの排他を取得する。入力leaseとrouterを通るので、合成したEscapeは通常操作と同じようにmenuを閉じる。lease清掃時はUiKeyCancelInputでmanager側の消費状態も解除し、物理入力の所有表を消さない。

アプリのframe順はWindow/Input取得→window manager.Advance→UiPopupManager.Dispatch/Advance→Popup登録の照合→Diagnostics runtime.Advance→通常layout/描画とする。

任意bridgeはOpenになったPopupをRegisterPopupし、[UI-0010の登録解除](UI-0010-diagnostics-ui-automation.md#切断取消終了)とIsReleased待ちを行うIAsyncDisposable tokenをBindLifetimeへ渡す。Closingでは新規操作を止め、既存Job/入力leaseの清掃後にtokenを完了する。managerは待機中もAdvanceを続け、先にsurface/hostを破棄しない。bridge自身の終了でも同じtokenを一度だけdrainする。

親画面とpopup/submenuを**一つのcontrol group**にする。rootのTargetIdをControlGroupIdとし、単独targetも自分自身をgroupとする。childでcontrol-acquireしても同じgroupのleaseを返し、別Actor/Sessionが兄弟や親から奪えない。action-startはtargetがcontrolのgroup内に属し、同じrootが生存中であることをゲーム側で検証する。ControlGroupIdはwireで必須・非空とし、上記defaultはソース互換のための引数既定値に限る。子のAllowInput/AllowScreenshotsは親の許可とのANDとする。

UI-0010のSequence high-water、再送同一payload比較、入力Job一つ、hostの入力lease一つという制約をTarget単位からgroup単位へ拡張する。payload比較にはTargetIdも含める。親画面をClick→Openを観測→一覧のParentTargetId/PopupIdで子を選択→同じcontrol.PageFor(child.Id)で項目をClickできる。表示方式でremote操作を変えず、callbackの直接実行やWindowを全検索するlocatorを追加しない。

観測Jobとartifactは引き続きTarget単位で結び付け、Popup分も既存Target16/Job64等の上限へ数える。登録枠がなければPopup表示自体は続けるが診断targetを省略し、rootの観測結果にautomation-unavailableを有界に報告する。開いてから閉じるまでに登録できなかったPopupを後で架空登録しない。子解除はそのTargetのJobを取消し、入力清掃完了までsurface/hostを保持する。親解除・control期限・Session切断ではgroup全体を取消し、全lease清掃後に操作権を解放する。

targetsのschema version 1へ上記の3フィールドを追加し、この段階のContracts/Client/Runtimeを同時に更新する。planJsonの閉じたschema、Diagnostics protocol version 2、Operation IDは変更しない。この版のContracts/Clientを同時に利用する。旧clientとのmetadata互換を保証せず、group制約は常にサーバー側で適用する。

HostedのScreenshotは実際の合成済みroot targetから対象矩形を切り出す。Nativeはその提示先の実画像を取得する。snapshotはPopup自身だけで、複数surfaceを一frameに固定した統合snapshot/スクリーンショットを保証しない。Menuが背面のrootを遮る間、root hostのactionability/Probeはmanagerの実配送先も検査し、rootだけのhit testでクリック可能と判断しない。owner側のAutomation/Diagnostics登録にも同じrouting hostを使い、単一surfaceの直接配送を迂回経路にしない。Passiveは入力の遮蔽候補へ含めない。

### 宣言的なPopupの利用者API

2026-10-29追記。PR #86で実装済みなのは上記UiPopupManagerの命令的な基盤である。通常の利用者には、静的なComposition factoryで表示意図を宣言するPopupを提示する。この節の宣言API・照合・portal接続はPR #93で実装する。標準Menu/Tooltipはコンポーネント拡充でこのAPIへ接続する。

追加する型は任意のLumyte.UI.Windowingの`Compose.Definitions.Popup : Widget`、`PopupPlacement`、`PopupPresentation`、`PopupDismissReason`、`PopupDismissRequest`とする。UI本体へWindow依存を追加しない。以下は生成factoryの主要引数の抜粋であり、他の継承Widget引数と実際の順序は省略する。property/constructorは通常のComposable定義に従う。keyは再構築の識別子で、factoryでは他のWidgetと同じ任意引数だが、Popupの接続前検証で必須・surface内一意を要求する。Contentは一回初期化するWidgetChildrenの宣言slotとする。

```csharp
namespace Lumyte.UI.Windowing;

public enum PopupPlacement { BottomStart, BottomEnd, TopStart, TopEnd, LeftStart, RightStart }
public enum PopupPresentation { Automatic, Hosted, NativeRequired }
public enum PopupDismissReason { OutsidePress, Escape, ItemInvoked, OwnerUnavailable }
public sealed record PopupDismissRequest(PopupDismissReason Reason);

// using static Lumyte.UI.Windowing.Compose; で使う生成factoryのsignature。
// PR #93の主要引数。callbackはActionのまま。
public static Definitions.Popup Popup(string anchor, UiArgument<string?> key = default,
    UiArgument<bool> isOpen = default,
    UiArgument<PopupPlacement> placement = default,
    UiArgument<PopupPresentation> presentation = default,
    Action<PopupDismissRequest>? onDismiss = null,
    Action<UiPopupOpened>? onOpened = null, Action<UiPopupClosed>? onClosed = null);
```

```csharp
using static Lumyte.UI.Compose;
using static Lumyte.UI.Windowing.Compose;

Panel()[
    Button(key: "file-anchor", onClick: OpenFileMenu),
    Popup(key: "file-menu", anchor: "file-anchor", isOpen: showFileMenu,
        onDismiss: _ => CloseFileMenu())[
        Stack()[Button(onClick: Save), Button(onClick: CloseDocument)]
    ]
]
```

例のshowFileMenuはboolであり、変更時は構築関数を再実行する。Signal対応後は`isOpen: menuOpenSignal`を直接渡せる。専用Bind、ambient runtime、利用者が開く構築scopeを要求しない。anchorは同じowner surfaceのWidget keyから解決し、別surfaceを暗黙探索しない。同じsurface内で参照先が複数なら候補宣言のcommitを拒否する。keyは兄弟内の照合に使うものでもあり、anchorに使うkeyはそのsurface内でも一意にする。初回layoutでanchorが見つからない/非表示なら開かず、表示後に消えたら通常の終了経路へ進める。古いWidget参照やAutomation IDを再構築後のanchorとして保持しない。

Popupはowner側では寸法ゼロのportal宣言であり、内容のlayoutは独立surfaceで行う。Contentをowner surfaceの通常の子として接続してからPopupへ移す実装にしない。reconcilerが宣言slotを保持し、その内容を新しいPopup専用surfaceへ一回接続する。親のclipとlayout制約は継承せず、owner/anchorとモデル寿命は宣言位置に属する。新しい宣言に同じkey/型があれば既存の表示要素とPopup sessionを照合し、内容だけを通常の再構築で更新する。値変更だけでClose/Openやfactoryの再実行を起こさない。各openで一回というUiPopupRequestのfactory規則は維持し、同一sessionの内容更新を新たなRequestOpenで代用しない。標準Applicationは各viewのUiPopupPortalCoordinatorを自動管理し、独自hostはowner layoutの後にAdvanceし、owner解放前にDisposeする。初期実装の宣言内容は生成Composition factoryで構築する。手書きnewの内容は低水準のUiPopupRequestで扱う。

isOpenは利用者のモデルが正本である。外側press/Escapeでは理由付きonDismissを一入力系列につき一回通知し、Signalやboolをライブラリが書き換えない。trueが継続しても同じ要求をframeごとに繰り返さない。通常のdismiss要求だけでは表示を閉じず、次の構築結果でfalse/除去となってから閉じる。実装時にはUiPopupRequestの末尾へ任意の`Action<PopupDismissRequest>? onDismissRequested = null`を追加し、省略した既存呼出しは現在の自動close、指定した宣言adapterは要求通知だけという契約を追加する。現在のDispatchが行うcloseをadapter側で事後的に取り消す方式にはしない。この追加parameterはPR #93で実装する。owner/anchor喪失、停止、能力/描画失敗は安全のため直ちに入力を停止して終了し、onClosedで結果を通知する。強制終了後の同じtrue宣言を毎frame再開せず、false→trueまたは新keyで再試行する。onOpened/onClosedは要求通知と区別した実際の寿命通知であり、一session一回とする。

AutomaticはPreferNative、HostedはHostedOnly、NativeRequiredはRequireNativeへ変換する。低水準UiPopupRequestの既定HostedOnlyは変更しない。表示中にpresentationが変わった場合は旧sessionをdrain後に新sessionを準備し、表示済みNativeをHostedへそのまま移送しない。placement/anchorの変更は提示幾何の更新として処理し、入力は新revisionの提示後に切り替える。Start/Endは横書きLTRのanchor左右端（Left/Rightでは上端）を表し、RTLの自動反転は初期対象外とする。既存のsideだけではEnd整列を表せないため、PR #93で配置要求へ整列を追加し、反対側へ開く場合も整列を保持して最後にclampする。内容から有限の希望寸法をlayoutし、managerのroot/monitor制限でclampする。NativeとHostedで同じ内容を二重接続しない。

false/宣言除去では新規入力を先に止め、既存系列取消→子Popup→Automation/IME→描画/GPU→surface/binding→Windowの順に既存の終了契約へ接続する。親Widgetの取り外しを非同期setterへ変えず、hostが旧表示資源をdrain完了まで所有する。最終的なvisual exitはanimation統合側が追加し、能力のないhostでは即時の視覚切替とする。UI停止時にアプリのonDismiss応答を待つことはしない。

受け入れ条件は、静的factoryだけで構築可能、同key再構築で入力/focus維持、別keyで新session、bool/Signalからの開閉、anchor消失/重複、dismiss通知一回、応答しないモデル、準備中の反転、除去中のGPU/診断lease保持、Native/Hostedで同じ操作結果となることとする。PR #93の検証結果はUI-0019の実装記録へ記載する。

## 検討した代替案

- 既存WindowSystemのNative失敗を暗黙にHosted化すると、独立Windowが必要なToolまで意味が変わる。UI-0014の注入managerと要求policyで明示的に選ぶ。
- PopupをanchorのWidgetの子やHostedの子にすると祖先clipで切れる。論理的な親とPresentationRoot直下の表示nodeを分離する。
- Browserでwindow.openを代用すると既存canvasの描画・入力・認証された診断targetとの対応が崩れる。独立Browser Windowは別機能として設計する。
- Popup表示ごとにOS focus/KeyboardTargetを変えるとPreserveFocusと競合し、親のキー押下が残る。managerのkeyboard scopeと系列取消を使う。
- UI-0009の単一surfaceへ別Windowを子として隠すとID、座標、Screenshotの契約が変わる。Popupを明示選択し、操作権だけをgroupで共有する。

- Popupの開閉を通常利用でも命令的なhandle操作にすると、再構築とモデルの表示意図が分離するため、宣言APIを追加し既存managerはhost向け基盤として維持する。
- anchorを古いWidget参照で保持すると再構築後の接続先が曖昧になるため、同じsurfaceで一意なkeyから解決する。

## 結果と影響

NativeとBrowserで同じComposition内容、callback、開閉規則と自動操作を使える。Hostedは別swapchainを必要とせず、Browserでもcanvas内のPopupを実現できる。Nativeは元画面外へ配置できる一方、出所付き入力とGPUを含む清掃が前提になる。

Hostedの領域外表示、内容の自動採寸、overflowの自動scroll、独立Tool Windowまでの代替は提供しない。origin/捕捉/描画の条件を満たさない接続ではHostedを使い、Nativeのfixture検証だけを実環境対応完了と記載しない。公開focus復帰とDiagnosticsのgroup化はPR 10で追加し、先行PRの単一surface契約を単独で変更しない。

## 検証方針

- 独自IUiWindowManagerを注入し、通常owner登録とPopupの開閉がWindowSystemなしで動くことを確認する。各policyとcapability、Browser、0寸法、Create/Bind/Place/Show失敗、timeout、遅延完了で、fallback前の完全清掃、factory一回、OnOpened/OnClosed一回を検証する。
- 寸法・配置について、小さいHosted owner、submenu、root端、anchor移動/取り外し/再接続、非一様DPI、monitor移動、OS実寸法変更を確認する。描画・hit test・automationの原点/clipを一致させる。
- 入力について、開く原因のUpを新popupへ渡さないこと、outside Downから終端までの消費、親menuへの一度だけの配送、Escape/Left、非活性化、同deviceの跨Native系列を確認する。背面のOnClickが発火しないことを検証する。
- focusについて、PreserveFocusでKeyboardTargetを変えずmenuへ配送すること、消えたfocus対象、復帰世代、Close callbackの再要求、Passiveの透過を確認する。
- 終了時はGPU実行中Close、親終了、子→親終了、Borrowed owner、Dispose前のdrainを確認し、解放前のNative Closeと閉じたsurfaceへの入力がないことを検証する。
- 自動操作ではNative/Hostedで同じLocator操作を行い、他Actorの親子同時操作、Targetを変えた同Sequence、子終了中lease、group失効、target上限、Hosted合成Screenshotを確認する。
- Browser実機でcanvas端、resize、DPI、contextmenu、blur、canvas削除を確認する。Windows/Linuxは必要な出所付き入力契約が揃った後に別Windowの実操作を確認する。未実行の環境は未検証として残す。

## 別途決定する事項

イベント時点の出所はINPUT-0007、取得側captureはUI-0007、manager注入はUI-0014で定義する。一般modal/dialog、scrollと内容採寸、メニュー固有Widget/hover navigation、アクセシビリティ、独立Browser Windowの相互通信は別途設計する。

## 参考資料

- [注入manager・Hosted実装・fallback](UI-0014-injectable-window-manager.md)
- [Windowingの所有権・一時Window・Hosted](../window/WINDOW-0001-window-system.md)
- [Browser backendの対応範囲](../../../src/Platform/Lumyte.Platform.Browser/README.md)
- [一方向layout](UI-0003-layout-and-invalidation.md)、[focusとキー入力](UI-0006-focus-and-keyboard.md)
- [Window入力とDPI](UI-0007-window-input-and-dpi-integration.md)、[GPU寿命](UI-0008-render-graph-and-resource-lifetime.md)
- [UI自動操作](UI-0009-ui-automation.md)、[Diagnostics bridge](UI-0010-diagnostics-ui-automation.md)

### MenuとWidgetの入力順序

既にmanagerが所有している系列の取消・終端は先に扱う。新しい通常キーは配送先surfaceを選んだ後、focused WidgetのOnInputを含むUiSurface.Dispatchへ一度だけ渡す。TextBox等が消費したキーをMenuへ重ねて配送しない。未処理の場合だけMenu固有の矢印・Escape・Left等を認識し、その系列を終端まで消費する。TabはWidgetが未処理の場合の既存focus巡回を使い、Menuのscope内候補へ限定する。Enter/SpaceのClickはButtonのhandlerが判断し、managerやUiSurfaceからOnClickを直接実行しない。

## 実装時の具体化

Popup(id)はpageだけを遅延生成し、factory・surface取得・終了参加者登録は初回操作時にdispatcherのUI ownerで行う。CreateOwnerHost/PopupHostはowner上でのDiagnostics接続用である。閉鎖後はadapterがsurface/hostへの参照を解放し、Sessionの一覧取得等で終了済みadapter記録を退役させる。

UI.Automationのrouting leaseは通常hostの物理入力排他を保持したまま、実routerへの送信と取消を追跡する。DiagnosticsのJob hostも同じrouting契約を転送する。親子でcontrolを共有し、fingerprintへTargetIdを含め、別targetへの同Sequence再配送を拒否する。

UiAutomationDiagnosticRegistrationはIAsyncDisposableも実装し、BindLifetimeへ直接渡せる。DisposeAsync自体はframeを進めず、Diagnostics runtime.Advanceによる取消完了を待つ。枠不足はroot targetsのAutomationCode一つでautomation-unavailableを報告する。Popup側はOnOpenedで一度だけ診断登録を試みる。

Browserサンプルの?popupsは同じcanvas内のroot/Popup/submenuを描画し、対象canvasのcontextmenuだけを同期抑止する。documentのoutside observerはPopup保持中だけ登録し、停止時に解除する。canvas削除ではhost全体を停止する。旧サンプルの通常Hosted領域とは専用モードを分ける。

公開契約の利用手順は[UI.Windowing](../../../src/UI/Lumyte.UI.Windowing/README.md)、[Automation接続](../../../src/UI/Lumyte.UI.Windowing.Automation/README.md)、[実行サンプル](../../../samples/Lumyte.UI.Popup.Sample/README.md)へ記す。

取得deviceをenvelopeのDeviceへ明示し、同じKeyの系列もdeviceごとに保持する。nullは独立した合成/legacy domainであり、実取得deviceを推定する値ではない。pointer identityは従来どおりホストがdevice/contactから安定して発行する。

通常Hosted画面をpointerで選ぶ場合は論理keyboard targetを更新し、Popup/Passive/Candidateの表示では変更しない。借用rootは最小化・非表示・0寸法になっても登録と描画bindingを維持し、Popupだけを閉じる。復帰後は同じroot IDで再び開ける。

## 検証記録

2026-10-29にWindowing 30件とDiagnostics UI Automation 40件がRelease／Debugで成功した。既存UI 178件、Automation 49件、UI統合32件、Diagnostics統合131件も成功し、Releaseの関連テストは合計460件となる。入力を辞退した系列の保持禁止、終了待ち中のMenu遮蔽、非表示／0寸法からのroot復帰、通常Hostedのkeyboard選択、device別key所有、source/presentationへの公開ID・提示revision変換を含める。

実Chromiumのinput-onlyモードでは、contextmenu、submenu、Click／Enter／Escape、canvas内外のoutside press、DPI、resize、blur、canvas削除と終了を確認した。これは入力とCPU合成記録の検証であり、Browser GPU提示／PNGの成功を意味しない。

Vulkan／wgpu（Mesa lavapipe）では合成root targetを実際に描画・readbackし、Popup領域を切り出した180×100 PNGのCRCと緑5,120画素を確認した。新規三パッケージのpackとNuGetのみを参照する外部consumerの実行も成功した。既存HTTP／MagicOnionの別プロセスUI操作も回帰確認した。親子targetとPageForのwire検証は別owner threadのloopback fixtureで行い、Popupを実transportで操作した結果とは区別する。

Windows/Linuxの実OS上の別Window操作、Browser GPU提示／PNG、Native AOT、実機GPUロストは未検証として残す。Native fixtureは実WindowSystemの要求・PreserveFocus・表示前清掃・Hosted fallback・Native親内のHosted submenuを確認する。

### 共通の再構築基盤との接続

focus bookmarkは宣言の参照だけでなくlogical AutomationIdから現行要素を解決する。ownerの再構築後も一致する要素へ復帰し、消えた要素には復帰しない。

## 前後順と終了状態の単一化

Windowのlayer判定は共通関数へ集約する。Hosted描画とhit testは同じlayer・提示順を使い、公開IDへの変換で順序を再計算しない。Popupの終了判定はhandleのClosing/Closed/Failedから導き、別のclosing flagは保持しない。Guard取消による再入callbackより前にClosingを設定する。

### Element分離後の実装

低水準UiPopupRequestのanchorはUiElementを受け取り、配置・入力・寿命をその実体から読む。
Widgetは独立surfaceの内容を構築する設定として返す。宣言再構築時にアンカーを付け替えず、
同じElementならpopupを継続し、除去・不適格化で閉じる。focus bookmarkもElementを保持し、
通常の復元では全ツリーをID検索しない。除去済み実体や別surfaceへの復元を拒否する。
ReleaseのWindowing 34件でHosted、Native fallback、入力、停止・解放、Automationを確認した。

UI本体214件とDiagnostics UI Automation 41件も成功した。headless popupサンプルで
owner・submenu・Click・Enter・後処理を通して保存callbackが3回になることを確認した。
