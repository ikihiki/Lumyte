# ADR-UI-0015: IME候補の任意描画と世代付き候補操作

- 状態: 採用
- 日付: 2026-10-28
- 対応: 実装PR 11の任意機能、前提: UI-0012・UI-0014

## 背景

[UI-0012](UI-0012-ime-and-text-editing.md)の編集文書とPlatform adapterに、アプリの見た目に合わせたIME候補表示を追加する。候補文字列を描けることと、IMEの選択・確定を操作できることは別の能力である。WindowsではTSF UIElementを利用できるTIPだけが対象となり、BrowserのtextareaはOS候補の一覧取得・表示抑止を提供しない。

候補の正本はIMEにある。表示済みのindexを別世代の候補へ適用したり、候補の文字列をTextDocumentへ直接挿入したりせず、IMEの操作protocolと文書編集通知を使う必要がある。また、候補を押しただけで編集元のfocusやMenu scopeを失わず、Diagnosticsからも同じ候補操作を許可付きで実行したい。

## 決定

### 範囲・方針・依存方向

既定はSystemとし、PreferCustomが指定され、backendと提示先が対応する場合だけImeCandidateViewを使う。未対応・準備失敗はSystemへ戻す。OS候補を使えないことを理由に文字入力自体を疑似実装しない。読みウインドウ、TIP固有の設定UI、予測候補の独自生成は対象外とする。

候補の不変モデルはBCLのみのLumyte.TextEditingへ、OSとのprotocolはPlatform.TextInput.Abstractionsと各backendへ置く。ImeCandidateViewはLumyte.UI.TextEditing、controller/presenterは任意のLumyte.UI.Windowing.TextInputへ置く。presenterは[UI-0014](UI-0014-injectable-window-manager.md)のIUiWindowManagerを借用し、WindowSystemを直接要求しない。UI.TextEditing.Automationが候補モデルとUI.Automationの独立したDTOを変換する。UI本体、UI.Automation、Diagnostics.ContractsへTSF/DOMやWindowSystem依存を追加しない。

| 環境 | 独自候補表示 | 選択・確定 | Systemへの復帰 |
| --- | --- | --- | --- |
| Windows TSF | UIElement対応TIP、対象文書の確認、提示準備が成立する場合 | Behavior対応と各操作の結果を確認 | pbShow=TRUE、抑止後はShow(TRUE) |
| Browser textarea | 非対応。候補一覧取得・抑止のcapabilityはfalse | 候補専用操作はUnsupported | Browser/OS候補を継続 |
| Headless fixture | 模擬snapshotと提示先で対応 | 世代・選択・更新・終了を模擬 | fixtureで成功/失敗を指定 |
| その他backend | 明示したcapabilityに従う | 未対応ならUnsupported | backendのSystem UI |

### 候補モデルとbackendの公開契約

比較元はmain `20f666ff8f64a63e65407936d32fb98cfa19c9d8`、UI-0012の提案APIへの追加とする。sessionと候補要素の世代を分け、OSのUIElement IDはadapter内部に留める。

```diff
+namespace Lumyte.TextEditing
+{
+    public enum ImeCandidateUiPolicy { System, PreferCustom }
+    [Flags]
+    public enum ImeCandidateOperations { None = 0, Select = 1, Page = 2, Finalize = 4, Abort = 8 }
+    public enum ImeCandidateActionKind { Select, Page, Finalize, Abort }
+    public readonly record struct ImeCandidateListId(Guid SessionId,
+        ulong SessionGeneration, ulong ElementGeneration);
+    public sealed record ImeCandidateItem(int Index, string Text);
+    // Itemsは全候補。indexは0始まり。選択なし・空listではnullable値を使う。
+    public sealed record ImeCandidateSnapshot(ImeCandidateListId Id, ulong Revision,
+        ulong DocumentRevision, IReadOnlyList<ImeCandidateItem> Items,
+        int? SelectedIndex, IReadOnlyList<int> PageStarts, int? CurrentPage,
+        ImeCandidateOperations Operations);
+    // Selectは候補index、Pageはpage index、Finalize/AbortはIndex=null。
+    public sealed record ImeCandidateAction(ImeCandidateListId Id, ulong ExpectedRevision,
+        ImeCandidateActionKind Kind, int? Index = null);
+    // AcceptedはTIPが操作を受理した意味。文書commit完了の通知ではない。
+    public sealed record ImeCandidateResult(ImeCandidateAction Action, bool Accepted, string Code);
+    public sealed record ImeCandidateSupport(bool CanReadList, bool CanSuppressSystemUi,
+        bool CanRequestBehavior, string Code);
+    public enum ImeCandidatePresentationState { System, Preparing, Custom, ReturningToSystem, Closed, Failed }
+    public sealed record ImeCandidateState(ImeCandidateListId? Id,
+        ImeCandidatePresentationState State, string Code);
+}
+namespace Lumyte.Platform.TextInput.Abstractions
+{
+    // 対応するITextInputSessionだけが実装する任意契約。
+    public interface ITextInputCandidateSession
+    {
+        ImeCandidateSupport CandidateSupport { get; }
+        ImeCandidateState CandidateState { get; }
+        // owner threadのIdle。prepared=trueはhidden提示先と入力routeが使用可能という契約。
+        // BeginUIElementで参照する値だけを更新し、ここからWidgetを呼ばない。
+        void ConfigureCandidateUi(ImeCandidateUiPolicy policy, bool prepared);
+        // 不変コピー。System表示中や有効な一覧がなければnull。
+        ImeCandidateSnapshot? CaptureCandidates();
+        // owner threadでqueueしservice.Advanceで実行。COM callback内では実行しない。
+        ValueTask<ImeCandidateResult> RequestCandidateAsync(ImeCandidateAction action,
+            CancellationToken cancellationToken = default);
+        // 活動中の抑止済み要素をSystemへ返す。完了前にpresenterを破棄しない。
+        ValueTask ReturnCandidatesToSystemAsync(CancellationToken cancellationToken = default);
+    }
+}
```

backendのcapabilityは利用可能なprotocolを示し、任意のTIPが各操作を成功させる保証ではない。実行時のOperationsとHRESULTを併用する。Browser sessionは候補契約を実装し、三つのcapabilityをfalse、Code=system-ui-only、CaptureCandidates=nullとする。候補操作は変更せずUnsupportedとなる。

各Beginに新しいElementGenerationを発行し、同じOS IDの再使用でも旧IDを復活させない。候補文字列、件数、選択、ページ、操作能力の変更ごとにRevisionを増やす。各snapshotは受付時にコピーし、取得中にEnd/再入更新があればそのsnapshotを公開しない。DocumentRevisionは同じsessionの文書とcaret geometryを照合するために使い、候補revisionの代用にはしない。

公開操作はowner thread専用で、別thread・再入はInvalidOperationException、破棄後はObjectDisposedException、無効なenum・引数・IDは変更前にArgumentException系とする。古いが形式上有効なtokenはstale-candidate、終了済みはcandidate-unavailable、backend非対応はunsupported-operationとして結果へ返す。Requestのqueue受付とTIP受理を区別し、callbackは実行結果に対して一度だけ呼ぶ。取消がTIP呼び出し後なら清掃を待ち、文書rollbackや未送信を保証しない。System復帰の環境上の失敗はCandidateStateのCodeへ残し、復帰不能ならValueTaskを失敗させる。

一つのkeyboard scopeで独自表示する一覧は一つとする。別の同時UIElementはSystemへ任せる。一覧は最大1024候補、候補一つ4096 UTF-16 code unit、一覧全体262144 code unit、queueは4096件に制限する。index、page先頭の単調性、件数との整合、Unicode、世代overflowを検査する。上限超過・不整合を切り捨てて成功扱いせずSystemへ戻し、invalid-candidates/result-too-largeを報告する。

### Windows TSFとの接続

WindowsTextInputServiceは同じSTAのthread managerからITfUIElementMgrを取得し、ITfSource経由でITfUIElementSinkを登録する。UIElement対応TIPだけに制限するTF_TMAE_UIELEMENTENABLEDONLYを通常の既定値にせず、非対応TIPのSystem UIを維持する。sink、COM interface、cookieは終了まで強参照し、.NET例外をCOM境界から出さない。

BeginUIElementは表示の採否を同期的に決める境界とする。

1. pbShowをTRUEで初期化する。sinkの失敗HRESULTによるfallbackには依存しない。
2. System方針、提示先未準備、対象sessionがEnding、別候補を独自表示中、candidate interface非対応ならTRUEのまま返す。
3. Prepare済みpresenterと現在のTSF document associationが有効な場合だけ、要素の識別と強参照を保存してpbShow=FALSEにする。この時点で候補文字列やページを読み出さず、Widget生成・layout・Window作成・待機を行わない。

候補内容は最初のUpdateUIElementから読み、ITfCandidateListUIElementのGetDocumentMgrで実際の対象を現在のsessionのdocument managerと照合する。GetDocumentMgr失敗・対象不明は、active editorの推測で補完せずSystemへ戻す。Begin時の抑止はこの照合が済むまで仮の状態であり、別文書の内容を表示・操作しない。Systemを選んだ要素にはUpdate通知が来る前提を置かない。

UpdateではIDとGetUpdatedFlagsの値を取得して未処理flagへORし、Widgetを呼ばず更新を蓄積する。service.Advanceの安全なSTA地点でGetCount/GetString、GetSelection、GetPageIndex/GetCurrentPageを読み、不変snapshotを公開する。対象文書もTF_CLUIE_DOCUMENTMGRで変わり得るため、公開前にGetDocumentMgrを再照合する。初回と不整合検出時は全項目を読み、複数Updateを一frameへ集約しても先の変更を落とさない。GetSelectionのS_FALSEは選択なしとして扱う。再入Updateで取得結果を捨てた場合はdirty/flagsを残して次のAdvanceで再取得し、追加の通知を待たない。EndUIElementでは即座に対象世代を無効化し、未処理commandを拒否して次のUI反映で表示を閉じる。

Behaviorの操作は次の意味に限定する。

- SelectはITfCandidateListUIElementBehavior.SetSelection(index)を呼ぶ。文書確定を指示しない。
- PageはsnapshotのPageStartsから目的pageの先頭候補を求め、SetSelectionを要求して実際の選択・現在pageの更新を待つ。ページを選べるというcapabilityはこの方式に対応するbackendだけが公開する。SetPageIndexはページ分割の設定であり、ページ移動の代用には使わない。
- Finalizeは現在選択を再検査してFinalize()を呼ぶ。候補文字列をTextDocumentへ挿入しない。確定はTIPからの通常文書編集・composition通知で成立する。
- AbortはAbort()を要求する。候補一覧の終了要求であり、compositionのrollbackを保証しない。アプリが編集全体を取消す場合はUI-0012のRequestEnd(Cancel)を使う。

commandの直前にsession、element世代、revision、document association、index、Operationsを再検査する。古ければstale-candidateで変更せず拒否する。BehaviorのQI成功だけで全操作を保証せず、E_NOTIMPL等はunsupported-operationとして返し、未対応が判明した操作をその一覧のOperationsから外してRevisionを更新する。TIP呼び出し後の失敗・未知結果は自動再送しない。戻り値をUIの選択状態へ先行反映せず、後続Update/Endを正本とする。

### Systemへのfallbackと終了

抑止後のsnapshot取得失敗、対象不明、提示失敗、show timeout、presenter喪失では、活動中のITfUIElement.Show(TRUE)を安全なSTA地点で呼ぶ。同じ活動中世代でShow/IsShownが成功し表示がTRUEになったことを確認してから独自描画を終了する。途中でEndが来ればClosedとして清掃する。同じ要素を再び自動で独自表示へ取り戻さず、復帰失敗を成功と報告しない。

復帰に失敗しても対象文書と独自表示が有効なら維持する。対象が当該sessionと確認済みで表示先も失われた場合だけRequestEnd(Cancel)でsession終了をdrainし、candidate-ui-unavailableを返す。対象不明・別文書なら現在のeditorを推測で取消さず、candidate-system-restore-failedを報告して新規独自表示を止め、Endまたはthread managerの終了まで当該COM参照と清掃記録を保持する。

対象不明の未復帰要素はserviceが保持し、controllerのsurface/window寿命とは分ける。controllerは復帰失敗を報告して自分の入力・描画資源を清掃でき、未知の要素からのEndを待って借用managerを永久保持しない。service終了時は通知受付を閉じてthread managerをdeactivateし、残る参照を解放する。現sessionに対する確認済みの未完了編集・lockは引き続きUI-0012の順序でdrainする。

candidate表示準備は既定2秒で失敗を確定し、非同期資源の回収はその後も完了まで続ける。Systemへの復帰中・Ending中の新commandは拒否する。Endを受けた要素へShow/Behaviorを呼ばず、遅れて届いた通知・完了Taskは元の世代の清掃だけに使う。ReturnCandidatesToSystemAsyncの取消は復帰や資源解放を途中で放棄する意味にはしない。

停止順は新command停止→System復帰またはsession終了→候補入力系列の取消→描画停止・GPU退役→binding解放→candidate surface解放・manager Close→実際の終了確認とする。UIElement sinkのunadviseとCOM解放は未完了command・通知のdrain後に行う。Window/Platform終了はUI-0012・UI-0014の所有者がその後に進める。

### ImeCandidateViewとcontroller/presenter

```diff
+namespace Lumyte.UI
+{
+    // UI-0012の既存値を維持し、ImeCandidates=4を追加する。
+    public enum UiRole { None, Group, Button, TextBox, ImeCandidates }
+    public enum UiAutomationExposure { Default, RedactedSubtree }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public abstract class Widget
+            {
+                // 既定Default。RedactedSubtreeは自身の本文metadataと全子孫を通常snapshotから除く。
+                protected virtual UiAutomationExposure AutomationExposure { get; }
+            }
+        }
+    }
+}
+namespace Lumyte.UI.TextEditing
+{
+    // Color2Dはstraight sRGB。全成分finiteかつ[0,1]、寸法はfiniteかつ非負。
+    public sealed record ImeCandidateStyle(Color2D Background, Color2D Foreground,
+        Color2D SelectedBackground, Color2D SelectedForeground, float Padding = 6, float RowSpacing = 2)
+    {
+        // 白背景/黒文字、選択背景(0.2,0.4,0.8,1)/白文字、Padding=6・RowSpacing=2。
+        public static ImeCandidateStyle Default { get; }
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            // Focusable=false、Role=ImeCandidates。候補行を通常Button/focus対象として生成しない。
+            [Composable]
+            public partial class ImeCandidateView : Lumyte.UI.Compose.Definitions.Widget
+            {
+                public ImeCandidateView();
+                [ComposeParameter] public ImeCandidateSnapshot? Candidates { get; set; }
+                [ComposeParameter] public IUiTextLayoutProvider? LayoutProvider { get; set; }
+                // 既定Default。代入時に値を検証・コピーする。
+                [ComposeParameter] public ImeCandidateStyle Style { get; set; }
+                [ComposeParameter] public Action<ImeCandidateAction>? OnAction { get; set; }
+                protected sealed override UiAutomationExposure AutomationExposure { get; }
+            }
+        }
+    }
+}
+namespace Lumyte.UI.Windowing.TextInput
+{
+    public sealed record UiImeCandidateOptions
+    {
+        public ImeCandidateUiPolicy Policy { get; init; } = ImeCandidateUiPolicy.System;
+        public UiWindowPolicy WindowPolicy { get; init; } = UiWindowPolicy.HostedOnly;
+        // finite・各軸正。内容によるWindow再計測は行わない。
+        public Vector2 ClientSize { get; init; } = new(320, 240);
+    }
+    public sealed class UiImeCandidatePresenter
+    {
+        // manager/providerは借用。資源取得はcontrollerのPrepareAsyncで開始する。
+        public UiImeCandidatePresenter(IUiWindowManager windows, IUiTextLayoutProvider provider,
+            ImeCandidateStyle? style = null);
+    }
+    public sealed class UiImeCandidateController : IDisposable
+    {
+        // connectionは借用、presenterの所有権は移譲。一connectionに一controller。
+        // callbackはowner threadのIdleで状態commit後に呼び、public eventは設けない。
+        public UiImeCandidateController(UiTextInputConnection connection,
+            UiImeCandidatePresenter presenter, UiPopupManager router,
+            UiImeCandidateOptions? options = null,
+            Action<ImeCandidateState>? onStateChanged = null,
+            Action<ImeCandidateResult>? onCommandCompleted = null);
+        public ValueTask PrepareAsync(CancellationToken cancellationToken = default);
+        public ImeCandidateState State { get; }
+        public ImeCandidateSnapshot? CaptureSnapshot();
+        // callbackから可。コピー・検査してqueueするだけで、直接COMを呼ばない。
+        public void Request(ImeCandidateAction action);
+        // owner Idleでsnapshot・command結果をviewへ反映する。
+        public void Advance();
+        // editor/viewのlayoutとconnection.PublishGeometry後。Place/Show要求を進める。
+        public void PublishGeometry();
+        public void BeginStop();
+        public bool IsStopped { get; }
+        // BeginStop後にPump/Advanceを続け、IsStoppedから呼ぶ。
+        public void Dispose();
+    }
+}
+namespace Lumyte.UI.Windowing
+{
+    public sealed class UiPopupManager
+    {
+        // ImeCandidates/Candidate windowのsurfaceだけ。Menu outside判定より前に配送する。
+        // 解除は新規入力停止と系列取消後。surface/windowは借用する。
+        public IDisposable RegisterCandidateInput(UiWindowId window, UiSurface surface);
+    }
+}
```

controllerはComposition factoryへCandidates、LayoutProvider、Style、`onAction: controller.Request`を名前付きパラメーターで渡してviewを構築する。色・選択色・余白・行間は不変のStyleで変更でき、文字組みはproviderへ委譲する。Candidates=nullでは候補を描画・操作せず、LayoutProvider=nullは準備をinvalid-presenterで失敗させる。viewは現在pageを制約内に一度layoutし、収まらない行はclipする。前後page、現在の選択、確定・中止の操作部分を同じ確定幾何で描画・hit testする。Operationsにない操作、選択なしの確定、範囲外のpage移動は無効にしてOnActionを呼ばず、その位置の入力を背面へ透過させない。

行のpointer Down/Upは同じ候補token/indexへ固定する。正常なUpはSelectを一度要求し、確定の操作部分だけがFinalizeを要求する。候補やrevisionが途中で変わればその系列を取消す。通常ButtonのOnClickから候補文字列を勝手にcommitしない。OnActionは操作要求であり、callbackを直接呼ぶことをbackend操作の成功とみなさない。IMEキーは引き続き編集元のTSF/DOMへ渡し、候補viewへTabやEnterを二重配送しない。

presenterはmanager.CreateへImeCandidates/Candidateを渡し、BindPresentationAsyncでhidden Ready、専用入力routeの登録まで済ませてからprepared=trueを公開する。CreateのpolicyはUI-0014へ委譲し、Native条件を満たさなければPreferNativeはHostedへfallbackする。独自表示自体が使えない場合のSystem fallbackとは区別する。候補viewをUiPopupManagerのMenu chainへ入れず、Show完了を確認してからCustomにする。

System方針では独自提示資源を取得しない。PreferCustomでも実sessionのcapabilityがfalseならpreparedを公開せずSystemを継続する。準備がBeginに間に合わなかった一覧を途中から横取りせず、準備完了は次の一覧から使う。pointerのhit testはUiWindowSnapshotの最新作業値ではなく、UI-0014のPresentedGeometryRevisionで提示済みと確認できる幾何だけを使う。

anchorは編集元の最新の文書/layout revisionに対応するcaretを、manager snapshotのPresentationRootのclient論理座標へ変換したものとする。UiWindowPlacement.AnchorWindowもそのPresentationRootを指定し、Ownerには編集元windowを指定する。KeyboardRootは維持し、候補windowをActivateしない。非活性Native/Hostedの位置・clip・DPIはUI-0014の同じ変換を使い、候補pointerをeditor focus喪失やMenu外側pressと解釈しない。候補専用routeが消費した系列を通常routerへ再送しない。新しい有効geometryが得られず提示できなければSystemへ戻す。

End後はcandidate window/bindingを終了し、Closed/Failedを確認してmanager.Releaseで終了snapshotと要求結果を退役させる。次の一覧へ備える場合も新しいhidden提示先を準備する。OS callbackから資源を取得しない。connectionのfocus変更・BeginStopはcontrollerの停止・候補復帰を含めてdrainし、controllerが停止するまでservice/session/managerを破棄しない。callback例外は残りの清掃後にhostへ報告し、成立済みcommandや通知を再実行しない。

通常frameではservice.Advanceで候補更新を取得し、入力配送とconnection.Advance後にcontroller.Advanceでviewへ反映する。manager/PopupのAdvanceはホストが各一回進める。editor/viewのlayoutとconnection.PublishGeometry後にcontroller.PublishGeometryを呼び、同じ文書revisionのcaretからPlaceを要求する。ShowはPlaceの成功と有効geometryを確認してから要求する。geometry待ちでは旧文書の矩形を新候補へ流用せず、準備期限内に得られなければSystemへ戻す。各controllerがmanagerのAdvanceを重複して呼ばない。

### AutomationとDiagnostics

ImeCandidateViewはAutomationExposureをRedactedSubtreeに固定する。通常snapshotにはview自身のID・role・幾何・可視性等だけを残し、Name/TestId/value等の本文metadataは空にする。内部の任意のButtonや装飾を含む子孫は列挙せず、内部partからprobeしたHitElementもこの境界のIDへまとめ、ActivationTargetはnullとする。候補文字を子Button.AutomationName等へ置いてinspectionを迂回することはできない。AllowTextInspection=trueでも通常snapshotのこの境界は解除しない。

ImeCandidateViewは一般Click/Fillの対象外で、候補専用APIを持たないhostではUnsupportedとする。候補を読む専用経路にはAllowTextInspectionを要求する。候補のScreenshotは既存Screenshot許可に従い、本文が写ることを別の権限で隠せるとはしない。

```diff
+namespace Lumyte.UI.Automation
+{
+    // BCLだけの転送値。backendのモデルとadapterで変換し、TSF型を参照しない。
+    public sealed record UiImeCandidateToken(Guid SessionId, ulong SessionGeneration,
+        ulong ElementGeneration, ulong Revision);
+    public sealed record UiImeCandidateSnapshot(UiImeCandidateToken Token,
+        IReadOnlyList<string> Candidates, int? SelectedIndex,
+        IReadOnlyList<int> PageStarts, int? CurrentPage, IReadOnlyList<string> Operations);
+    // Kindはselect/page/finalize/abortだけ。index規則はbackendのactionと同じ。
+    public sealed record UiImeCandidateCommand(UiImeCandidateToken Token, string Kind, int? Index = null);
+    public sealed record UiImeCandidateResult(bool Accepted, string Code);
+    public interface IUiAutomationImeHost
+    {
+        // host-bound。compositionは許すが、未処理入力/lock/物理押下をdrainして取得する。
+        ValueTask<IUiAutomationInputLease> AcquireImeCandidateInputAsync(UiImeCandidateToken token,
+            CancellationToken cancellationToken = default);
+        // RunOnUiThreadAsync内のIdle。本文/metadataともinspection許可が必要。
+        UiImeCandidateSnapshot? CaptureImeCandidates(UiSurface surface);
+        // 取得済みlease内。内部でownerへ配送し、通常controller/backend commandへ委譲する。
+        ValueTask<UiImeCandidateResult> ExecuteImeCandidateAsync(UiImeCandidateCommand command,
+            CancellationToken cancellationToken = default);
+    }
+    public sealed class UiAutomationPage
+    {
+        public Task<UiImeCandidateSnapshot?> CaptureImeCandidatesAsync(CancellationToken cancellationToken = default);
+        public Task SelectImeCandidateAsync(UiImeCandidateToken token, int index, CancellationToken cancellationToken = default);
+        public Task ChangeImeCandidatePageAsync(UiImeCandidateToken token, int page, CancellationToken cancellationToken = default);
+        public Task FinalizeImeCandidateAsync(UiImeCandidateToken token, CancellationToken cancellationToken = default);
+        public Task AbortImeCandidatesAsync(UiImeCandidateToken token, CancellationToken cancellationToken = default);
+    }
+}
+namespace Lumyte.Diagnostics.UI.Automation.Contracts
+{
+    public sealed record UiRemoteImeCandidateToken(Guid SessionId, ulong SessionGeneration,
+        ulong ElementGeneration, ulong Revision);
+    public sealed record UiRemoteImeCandidateSnapshot(UiRemoteImeCandidateToken Token,
+        string[] Candidates, int? SelectedIndex, int[] PageStarts, int? CurrentPage, string[] Operations);
-    public sealed record UiRemotePlan(int SchemaVersion, string Kind,
-        UiRemoteLocatorStep[] Locator, string? Key = null, string[]? Modifiers = null,
-        UiRemotePoint? Position = null, string? Predicate = null, int? Count = null, string? Text = null);
+    public sealed record UiRemotePlan(int SchemaVersion, string Kind,
+        UiRemoteLocatorStep[] Locator, string? Key = null, string[]? Modifiers = null,
+        UiRemotePoint? Position = null, string? Predicate = null, int? Count = null, string? Text = null,
+        UiRemoteImeCandidateToken? Candidate = null, int? CandidateIndex = null);
+}
+namespace Lumyte.Diagnostics.UI.Automation.Client
+{
+    public sealed class UiRemotePage
+    {
+        public Task<UiRemoteImeCandidateSnapshot?> CaptureImeCandidatesAsync(CancellationToken cancellationToken = default);
+        public Task SelectImeCandidateAsync(UiRemoteImeCandidateToken token, int index, CancellationToken cancellationToken = default);
+        public Task ChangeImeCandidatePageAsync(UiRemoteImeCandidateToken token, int page, CancellationToken cancellationToken = default);
+        public Task FinalizeImeCandidateAsync(UiRemoteImeCandidateToken token, CancellationToken cancellationToken = default);
+        public Task AbortImeCandidatesAsync(UiRemoteImeCandidateToken token, CancellationToken cancellationToken = default);
+    }
+}
```

候補専用leaseはFillのAcquireTextInputAsyncと異なり、現compositionを許したまま取得する。待機中は排他を持たず通常IME入力を進め、group全体の取得済み入力、未反映DOM編集、未完了TSF lock、物理押下がなくなったIdleでtoken/revision/対象editorの所属を再検査して予約する。旧候補ならstale-candidate、終了済みならcandidate-unavailableで失敗し、別一覧を再検索して操作しない。focusやcompositionを自動取消してleaseを得ない。

取得後は新しい物理入力をgroupへ割り込ませず、TIPの非同期更新/Endは止めない。command直前の世代・revision再確認、実行、未完了入力の清掃まで同じleaseで保護する。TIP側で一覧が変われば送信前に拒否し、送信後に変わっても同じ操作を自動再送しない。Pageの既定期限とhost.NextFrameAsyncで完了を観測し、候補serviceのAdvanceを止めたままTaskを待たない。Finalize成功はTIP受理までであり、文書の確定値は後続のToHaveValue等で確認する。

Diagnosticsの候補captureはObserveかつAllowTextInspection、操作はOverrideInputかつAllowInput/AllowTextInputを要求する。子targetは親の許可とのAND、操作権とSequenceは既存control groupを共有する。対象targetがそのsession/editorを所有することも確認する。候補専用hostの取得口もwrapperが転送し、lease取得成功でInputMayHaveBeenSent=trueを保持する。候補本文は通常trace/log/失敗messageへ記録しない。

UI subsystemとplanをschema version 3にし、kind=ime-candidates/ime-select/ime-page/ime-finalize/ime-abortとrole=ime-candidatesを追加する。候補kindはlocatorを空にし、key/modifier/position/predicate/count/textを拒否する。ime-candidatesはCandidate/CandidateIndexを持たずobserve-startで実行する。操作kindはCandidateを必須とし、select/pageだけ非負のCandidateIndexを要求し、finalize/abortでは省略する。対象はそのtargetの活動中candidate sessionであり、グローバルなWindow探索は行わない。

capture結果は上記snapshotまたはnullを有界artifactへ格納する。UInt64はUI-0010の精度を失わない規則に従い、候補本文にも既存artifact上限を適用する。action-startの同一payload比較はTargetId、候補token、kind、indexを含む。catalogを確認してから送信し、Runtimeはversion 1/2も従来の閉じたschemaで受付する。旧versionでは候補field/kindを拒否し、Diagnostics protocol 2とOperation IDは変更しない。

## 検討した代替案

- 候補を通常Menu/Buttonへ変換するとfocusとoutside-press、一般Clickの権限がIMEと衝突するため、Candidate入力modeと専用commandを使う。
- 全環境で独自候補を必須にすると、UIElement非対応TIPやBrowserの入力を失うため、Systemを既定とする。
- 選んだ候補文字列を文書へ直接置換するとTIPの文節・再変換・終了処理を迂回するため、Behavior操作と通常文書通知を使う。

## 結果と影響

対応するWindows TIPと独自managerで、Compositionによる候補表示と型付きの候補操作を利用できる。System fallback、入力scope、世代、資源清掃と診断権限を明示できる。

TIPごとのprotocol対応と復帰失敗を扱う必要があり、すべてのIMEで独自候補を保証するものではない。BrowserはSystem候補を維持する。候補APIの成功と文書確定の完了は引き続き区別する。

## 検証方針

- headlessでBegin→Update→End、同じOS IDの再使用、古いrevision/index、部分更新、選択なし、空一覧、page境界、不正値・上限を確認する。
- Begin時は内容未準備でもpbShowを安全に決め、System時にUpdateが来なくても待機し続けないことを確認する。GetDocumentMgr失敗、BehaviorのE_NOTIMPL、Show(TRUE)失敗、終了後の遅延callbackをfixtureで検証する。
- Selectで文書を直接commitせず、FinalizeはTIPからの文書通知に従い、Abortをrollbackと誤認しないことを確認する。操作中の一覧更新、未完了操作の取消、同じcommandの自動再送禁止も確認する。
- Hosted/非活性Nativeの候補clickでKeyboardRoot・editor focus・親Menuを保ち、候補を外側pressとして再配送しないことを確認する。caret移動、DPI、clip、geometry不一致、GPU実行中終了とSystem復帰を確認する。
- Windows実機でMicrosoft日本語IMEと利用可能なUIElement対応TIPを確認し、非対応TIPはSystem候補へ戻ることを確認する。x64/arm64 Native AOTでCOM寿命も検証し、fixtureと実機の結果を分ける。
- Browserでcapability=false、独自候補操作Unsupported、通常の日本語compositionとOS候補が継続することを確認する。
- Diagnosticsでinspection未許可の一般snapshot/logに候補本文が出ず、汎用Click/Fillでは操作できないこと、専用権限・group lease・schema 1/2/3・stale token・送信後再試行禁止を確認する。候補待機中の手動操作を妨げず、取得後もTIPのUpdate/Endが進むことを確認する。

## 参考資料

- [UI-0012: IMEと編集文書](UI-0012-ime-and-text-editing.md)
- [UI-0014: 注入可能なWindow manager](UI-0014-injectable-window-manager.md)
- [TSF UILess Mode Overview](https://learn.microsoft.com/en-us/windows/win32/tsf/uiless-mode-overview)
- [ITfUIElementSink.BeginUIElement](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfuielementsink-beginuielement)
- [ITfUIElementSink.EndUIElement](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfuielementsink-enduielement)
- [ITfThreadMgrEx.ActivateEx](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfthreadmgrex-activateex)
- [ITfUIElement.Show](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfuielement-show)
- [ITfUIElement.IsShown](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfuielement-isshown)
- [ITfCandidateListUIElement.GetUpdatedFlags](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfcandidatelistuielement-getupdatedflags)
- [ITfCandidateListUIElement.SetPageIndex](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfcandidatelistuielement-setpageindex)
- [ITfCandidateListUIElementBehavior](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nn-msctf-itfcandidatelistuielementbehavior)
- [ITfCandidateListUIElementBehavior.Abort](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfcandidatelistuielementbehavior-abort)
- [Microsoft SampleIME CandidateListUIPresenter](https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/IME/cpp/SampleIME/CandidateListUIPresenter.cpp)

候補viewもWidget.OnInputとUiInputContextのcapture操作でpointer系列を処理する。RegisterCandidateInputはWindow/surfaceの配送順だけを登録し、surface手前の専用入力adapterを持たない。行選択・確定・取消の認識は候補コンポーネント内で行う。

### PR 11の要求完了と観測の境界

`UiImeCandidateController.RequestAsync(ImeCandidateAction, CancellationToken = default)`はownerのqueueへ一度だけ積み、Advanceが結果を観測してから完了する。callbackも同じownerのAdvanceで呼ぶ。取消がsubmissionより前ならbackendを呼ばず、送信後の待機取消は再送しない。

WindowsのSelect/PageはBehaviorへの要求成功を先行表示せず、次のsnapshotで選択index/current pageを確認する。終了・世代変更・2秒以内に確認できない場合は失敗結果とし、呼び出しを繰り返さない。候補Windowのshowにも2秒の期限を適用する。controllerのコンストラクター末尾に任意`TimeProvider? clock = null`を持たせ、presentation期限を再現可能にする。

Diagnosticsはsubsystem/plan schema 3を公開し、旧schema 1の通常操作とschema 2のFill/値検証を継続受付する。候補counterはJSON上でcanonical decimal stringとし、JavaScriptの整数精度で失わない。既存targetのAutomationCodeを保持した上でAllowTextInput/AllowTextInspectionを追加する。新clientはcatalogの対応versionを送信前に確認する。

## 検証記録

候補controllerはHosted managerと模擬backendを接続し、非表示準備、focus維持、世代/revisionによる古い要求の拒否、要求完了、binding退役とWindow releaseを確認した。Menu guardは文書終了待ちからGPU退役まで保持することを既存Window fixtureで確認した。候補専用APIとDiagnosticsの閉じたschema/権限の試験を含め、全体の実行結果と未検証範囲は[UI-0012の検証記録](UI-0012-ime-and-text-editing.md#検証記録)へ集約する。

Windows UIElement/Behaviorによる実TIPの一覧取得・System復帰・選択/確定、および実Native候補Windowは未検証である。fixtureの候補操作を実OS候補の成功とは記述しない。

## 解放済みsessionとの境界

候補controllerより先に接続側がsessionの解放を観測した場合、controllerはそのsessionへConfigureCandidateUi等を再送しない。解放済みITextInputSessionを識別して候補表示の退役だけを続ける。sessionの通常解放がsystem候補への返却を完了していることを前提とし、次の接続へ旧候補の操作を渡さない。

回帰試験は、focusを失ってsessionが解放された後にcontrollerをAdvanceしても旧sessionへアクセスせず、停止を完了することを確認する。
