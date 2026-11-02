# ADR-UI-0017: ページnavigationと切替・履歴・寿命の契約

- 状態: 採用
- 日付: 2026-10-30
- 対応: 実装PR 13、UIアプリケーション拡張PR 2
- 前提: UI-0016、UI-0002〜0015

## 背景

[UI-0016](UI-0016-ui-application-framework.md)の専用アプリで、同じWindowにページを切り替え、戻る・進む・置換を扱いたい。読み込み中は現在のページを維持し、準備失敗で履歴や入力先を半分だけ変更しない必要がある。

通常のUiSurface.Rebuildは同じsurface内でWidget宣言を照合し、互換性のあるElementを保持する。ページ切替では旧ページと候補ページのElement・focus・編集session・DI scopeの寿命を分ける必要がある。ページごとの新しいtree/surfaceを非公開で準備し、安定したWindow IDに対する表示bindingを切り替える境界を追加する。

## 決定

### 配置と範囲

任意の`Lumyte.UI.Navigation`を追加する。UI、UI.Windowing、BCLに依存し、DI container、Platform backend、Diagnostics通信、Browser DOMを参照しない。ページの生成・清掃はIUiPageActivationProviderから受け取るleaseへ委譲する。後続のUI-0018がDI scopeと非ComposableなIUiPageをこの境界へ接続する。

初期は一つのWindow view slotに一つのnavigatorを置く。Push、Replace、Back、Forward、静的route、型付き引数、非同期準備、離脱guard、取消と履歴上限を扱う。nested router、複数outlet、ページのKeepAlive、遷移animation、URL同期は初期範囲外とする。公開callbackはコンストラクター引数で受け、public eventを追加しない。

### 任意packageとしての依存境界

- `Lumyte.UI.Application.Navigation`はApplicationとNavigationに依存し、`using Lumyte.UI.Application.Navigation;`で起動・content・slot拡張を利用する。通常のApplication、Desktop／Browser／Headless、Application.Diagnosticsの依存graphにNavigationを含めない。
- Application内部のview交換portはページ、route、履歴を知らず、統合層が所有するopaqueな公開metadataだけを同期commitで保持する。portは内部契約とし、今回の分離のために汎用router APIを公開しない。
- Navigationのslot adapterは同じslotに対して同じhostを返す。登録表はweak keyとし、終了済みWindowをstaticな表から保持し続けない。静的rootとNavigationの同時所有は拒否する。
- `Lumyte.UI.Application.Navigation.Diagnostics`はApplication.Navigationと基本Application.Diagnosticsへ依存する。`UseDiagnostics`後の`UseNavigationDiagnostics`が`ui.navigation/state`だけを追加する。未設定・二重登録はbuilder構成時に拒否する。
- 通常AutomationのTarget予約・原因Jobのdrain・Target交換は、Navigationに依存しないview交換participantとして基本Diagnosticsに残す。Navigation metadataを登録しなくてもClick起点の遷移を処理できる。
- `UiNavigationCommittedView`、`UiNavigationWindowSnapshot`はApplication.Navigation、readerと診断contributorはApplication.Navigation.Diagnosticsに置く。既存の表示・失敗・清掃契約を変更しない。

### 実装との対応

`Lumyte.UI.Navigation`はDIなしのprovider/leaseと`UiNavigator`を提供し、任意の`Lumyte.UI.Application.Navigation`がslot adapterと`UiNavigationApplicationContent`、`UseNavigation<TArgs>`を提供する。controllerの`Navigation`と`WindowContext`は初期化後の借用参照で、同じWindow instance内で維持する。UseWindowsの各content factoryは新しいcontrollerを生成し、再追加で以前のcontrollerを使い回さない。標準hostがAdvance・停止・GPU退役・清掃を管理する。

実行例は`Lumyte.UI.Navigation.Sample`と`Lumyte.UI.Navigation.Browser.Sample`、回帰試験は`Lumyte.UI.Navigation.Tests`および既存Application/Windowing/Diagnostics試験に置く。DIページ登録はUI-0018、Signalと視覚遷移は後続PRの範囲を維持する。

### route・引数・履歴

routeはアプリ内の登録keyであり、1〜256文字の`/`または`/`で区切った小文字ASCIIのsegmentとする。segmentは英小文字で始まり、以後は英小文字・数字・`-_.`を許す。末尾`/`、空segment、`.`、`..`、`%`、query、fragment、placeholder、wildcardを拒否する。比較はOrdinalとし、大小文字変換・URL decode・ファイルpath解決を暗黙に行わない。例えば`/settings/general`は有効で、`/documents/{id}`のIDは初期には別の型付き引数で渡す。

同じrouteを異なる引数型へ重複登録しない。`UiPageKey<TArgs>`のValueと登録型を一致させ、key instanceの参照同一性、実行時の型推測、objectからの暗黙変換を使わない。引数なしはUiNoArgsとする。Captureは登録した純粋なcopier/validatorを使い、入力値の変更から独立したsnapshotを一度作る。`GetArguments<TArgs>`もそのcopierで新しいコピーを返し、ページが履歴の正本を変更できないようにする。immutableな値なら同じ値を返してよい。copierはDI scope、Widget、delegate、Window/GPU資源をsnapshotへ保持せず、同じ有効値から決定的にコピーする。入力拒否の例外は許すが、外部状態や副作用で結果を変えない。

履歴はEntryId、route、引数snapshotだけを保持する。Back/Forwardでも新しいActivationId、page lease、tree、UiSurfaceを作る。旧Widget、DI scope、focus bookmark、編集sessionを履歴へ保存しない。form/scroll等の状態を復元したい場合はアプリのmodelを明示して引数や長寿命のserviceへ保存し、KeepAliveを実装済みとはしない。

Pushはcommit時にforward履歴を捨てて新EntryIdを追加する。Replaceは現在位置を新EntryIdで置き換え、履歴が空なら最初のentryを作る。Back/Forwardは対象EntryIdを維持してindexを移す。候補がなければNoHistoryとし、guardやfactoryを呼ばない。同じrouteへのPushも新しいentryとして扱う。上限は既定64件（現在とforwardを含む）で、Pushで超えた分は最も古いback entryから除く。準備中・拒否・取消・失敗では履歴を変更しない。

### navigationとページ生成の公開契約

比較元はPR #89 `04893264fd169d5b1085a00e85f89d935dc8caef`。UI-0016までの実装APIへ追加する。Widgetは`Lumyte.UI.Compose.Definitions.Widget`の略記である。

```diff
+namespace Lumyte.UI.Navigation
+{
+    public readonly record struct UiNoArgs;
+    public sealed class UiPageKey<TArgs>
+    {
+        // 上記route文法を検証。不正値はArgumentException。
+        public UiPageKey(string value);
+        public string Value { get; }
+    }
+    public sealed class UiPageDestination
+    {
+        public string Route { get; }
+        public Type ArgumentsType { get; }
+        // 型は完全一致。返値はcopierによるコピー。型不一致はArgumentException。
+        public TArgs GetArguments<TArgs>();
+        // providerが登録済みcopierを渡す。入力を履歴の正本として直接保持しない。
+        public static UiPageDestination Capture<TArgs>(UiPageKey<TArgs> key,
+            TArgs arguments, Func<TArgs, TArgs> captureArguments);
+    }
+    public enum UiNavigationMode { Push, Replace }
+    public enum UiNavigationKind { Push, Replace, Back, Forward }
+    public enum UiNavigationStatus { Committed, Rejected, Canceled, Failed, Busy, NoHistory }
+    public enum UiNavigationPhase
+    {
+        Idle, Preparing, AwaitingGuard, Quiescing, Retiring,
+        RetirementBlocked, Stopping, Stopped, Faulted
+    }
+    public sealed record UiNavigationEntry(Guid Id, UiPageDestination Destination);
+    public sealed record UiNavigationResult(Guid RequestId, UiNavigationStatus Status,
+        string Code, Guid? EntryId, Guid? ActivationId, Exception? Error = null);
+    // listはコピー。空履歴のCurrentIndexは-1。引数の正本は変更できない。
+    public sealed record UiNavigationSnapshot(Guid NavigatorId, UiNavigationPhase Phase,
+        IReadOnlyList<UiNavigationEntry> Entries, int CurrentIndex, Guid? ActivationId,
+        bool CanGoBack, bool CanGoForward, UiNavigationResult? LastResult,
+        Exception? RetirementFailure);
+    public sealed record UiNavigationOptions
+    {
+        // 1以上。不正値は構築時ArgumentOutOfRangeException。
+        public int MaxHistoryEntries { get; init; } = 64;
+    }
+    public sealed class UiNavigationRequest
+    {
+        public Guid Id { get; }
+        // 非同期失敗/取消もResultとして完了する。未観測のfaulted Taskを作らない。
+        public Task<UiNavigationResult> Completion { get; }
+        // thread-safeな取消通知のみ。commit前だけ有効、冪等。清掃を捨てない。
+        public void Cancel();
+    }
+    public interface IUiNavigator
+    {
+        // owner threadからcallback中も可。コピー・検証してqueueし、遷移を実行しない。
+        UiNavigationRequest RequestNavigate<TArgs>(UiPageKey<TArgs> key, TArgs arguments,
+            UiNavigationMode mode = UiNavigationMode.Push,
+            CancellationToken cancellationToken = default);
+        // routeはUiNoArgsとして登録されていること。
+        UiNavigationRequest RequestNavigate(string route,
+            UiNavigationMode mode = UiNavigationMode.Push,
+            CancellationToken cancellationToken = default);
+        UiNavigationRequest RequestBack(CancellationToken cancellationToken = default);
+        UiNavigationRequest RequestForward(CancellationToken cancellationToken = default);
+        UiNavigationSnapshot CaptureSnapshot();
+        // owner Idle。RetirementBlockedの保持中leaseを再試行。通常入力の受付は不要。
+        ValueTask RetryRetirementAsync(CancellationToken cancellationToken = default);
+    }
+    public sealed class UiNavigator : IUiNavigator
+    {
+        // provider/hostは借用。全て同じWindowとowner threadに固定する。
+        public UiNavigator(IUiPageActivationProvider provider, IUiNavigationHost host,
+            UiNavigationOptions? options = null, Action<UiNavigationResult>? onCompleted = null);
+        // owner Idleで一frame一回。Taskを同期waitしない。
+        public void Advance();
+        public ValueTask RetryRetirementAsync(CancellationToken cancellationToken = default);
+        public void BeginStop();
+        // 新transition/guard/prepareが停止済み。slotのGPU退役やscope破棄を待たない。
+        public bool IsStopped { get; }
+        // hostのGPU drain後、surface有効中。pageの再試行可能な清掃だけを行う。
+        public ValueTask RetireAsync(CancellationToken cancellationToken = default);
+        // hostのsurface解放後に一回だけ呼ぶ。DI等の最終破棄の失敗はterminal。
+        public ValueTask ReleaseAsync();
+    }
+    public sealed class UiPageActivationContext
+    {
+        // navigatorが生成し、変更できない。ActivationIdは毎回新規発行する。
+        public Guid ActivationId { get; }
+        public UiWindowId Window { get; }
+        public UiPageDestination Destination { get; }
+        public IUiNavigator Navigation { get; }
+        public IUiDispatcher Dispatcher { get; }
+        // 準備取消・退役開始で取消。要求元tokenとは別に寿命全体を表す。
+        public CancellationToken LifetimeCancellation { get; }
+    }
+    public interface IUiPageActivationProvider
+    {
+        // 未登録route/型不一致はArgumentException。登録済みcopierでsnapshotする。
+        UiPageDestination Capture<TArgs>(UiPageKey<TArgs> key, TArgs arguments);
+        // 軽量leaseだけを返す。DI scope/資源取得/ユーザーfactoryはここで開始しない。
+        IUiPageActivation Create(UiPageActivationContext context);
+    }
+    public interface IUiPageActivation : IAsyncDisposable
+    {
+        bool IsPrepared { get; }
+        // 成功後だけ参照可。親/surfaceなしのroot。未準備はInvalidOperationException。
+        Widget Root { get; }
+        IUiPageLeaveGuard? LeaveGuard { get; }
+        // 一回だけ。データ準備後、同期BuildをDispatcherでowner Idleへ戻して行う。
+        ValueTask PrepareAsync(CancellationToken cancellationToken);
+        // 完了段階を保存し、失敗時に同じleaseで再試行できる清掃。
+        ValueTask RetireAsync(CancellationToken cancellationToken);
+    }
+    public sealed record UiPageLeaveContext(UiNavigationEntry Current,
+        UiPageDestination Destination, UiNavigationKind Kind, IUiDispatcher Dispatcher);
+    public sealed record UiPageLeaveDecision(bool Allowed, string Code);
+    public interface IUiPageLeaveGuard
+    {
+        // Allowed時Codeは空、拒否時は短い固定code。callbackを同期waitしない。
+        ValueTask<UiPageLeaveDecision> CanLeaveAsync(UiPageLeaveContext context,
+            CancellationToken cancellationToken);
+    }
+}
```

要求は同じnavigatorで一件だけ進める。競合はBusyで即時完了し、隠れたqueue、latest-wins、factoryの再実行は行わない。guardや準備処理からの再帰navigationもBusyとなる。onCompletedは状態確定後のowner Idleで一度だけ呼び、callbackの例外はアプリhostへ報告して結果・commitを変えない。新しい要求はcallbackからqueueできるが、旧pageの退役が残る間はBusyとする。

非同期の失敗はResultのFailed、guard拒否はRejected、要求token/Cancel/停止はcommit前ならCanceledとする。Completion自体はこれらを理由にfault/cancel状態へしない。未登録key、引数、別thread、終了後等の契約違反は要求受付時に同期例外で返す。LastResultとonCompletedで結果を観測できるので、ButtonのActionからasync voidやTask.Waitを使う必要はない。

要求とCaptureSnapshotはowner threadから呼び、Advance、停止・退役、host/lease操作の開始はowner Idleとする。CancelとCompletionの待機だけは別threadから使える。RetryRetirementAsync/RetireAsyncのcaller tokenは待機の取消であり、開始済み清掃を中断するtokenとしてpageへ渡さない。null引数はArgumentNullException、未定義enumはArgumentOutOfRangeException、phase/寿命違反はInvalidOperationExceptionとし、失敗前の公開状態を維持する。

provider.Createで取得したleaseは、Prepare開始前からnavigatorが所有する。Prepareでscope生成、service解決、データ取得、Buildのどこが失敗しても同じleaseを清掃する。準備完了前にRootを公開することと、準備成功後にRootを交換することを拒否する。構築時の所有親が付いた宣言はhostの候補Surface作成時に拒否する。同じ受動的な宣言を別Surfaceへmountする場合は独立Elementを作り、旧ページの状態を移送しない。UI操作はDispatcherへ明示して戻し、非同期Prepareのcontinuationがowner threadへ戻るとは仮定しない。

### view slotと原子的な表示切替

```diff
+namespace Lumyte.UI.Navigation
+{
+    // 引数本文は観測metadataへ出さない。
+    public sealed record UiNavigationPublication(Guid NavigatorId, Guid RequestId,
+        Guid EntryId, Guid ActivationId, string Route);
+    public interface IUiNavigationHost
+    {
+        UiWindowId Window { get; }
+        IUiDispatcher Dispatcher { get; }
+        // 現在のinput dispatchの因果ID。プログラムからの要求ではnull。
+        Guid? CaptureInputOperation();
+        // pageのPrepare前に呼ぶ。資源取得前にtracked cleanup枠とtokenを返す。
+        // Currentがない初期navigationも同じ入口。
+        IUiNavigationViewChange BeginChange(Guid activationId);
+    }
+    public interface IUiNavigationViewChange : IAsyncDisposable
+    {
+        // 候補surfaceのlayout/描画/入力adapter/診断予約を非公開で準備する。
+        ValueTask PrepareAsync(Widget root, CancellationToken cancellationToken);
+        // 新規入力を止め、旧操作/IME/Popupをdrain。旧viewは維持する。
+        ValueTask QuiesceAsync(Guid? originatingInputOperation, CancellationToken cancellationToken);
+        // 同期・読取のみ。viewport/描画世代等が変わった場合はfalseと固定code。
+        bool TryValidateCommit(out string code);
+        // owner Idle、準備済み・quiesce済みのみ。await/ユーザーcallback/資源取得をしない。
+        // Current view・input route・公開metadataを一括で交換する。
+        void Commit(UiNavigationPublication publication);
+        // commit後の旧viewのGPU退役まで。旧surfaceはまだ有効に保つ。
+        ValueTask DrainRetiredAsync(CancellationToken cancellationToken);
+        // page.RetireAsync完了後。旧binding/surface等を最終解放する。
+        ValueTask ReleaseRetiredAsync(CancellationToken cancellationToken);
+        // commit前の候補GPUをdrain。候補surfaceがあれば有効に保持する。
+        ValueTask DrainCandidateAsync(CancellationToken cancellationToken);
+        // このchangeに属するpage.Retireの呼出し全体へcleanup admissionを与える。
+        // Prepare失敗でsurfaceが未生成でも可。停止後の通常workを許可しない。
+        ValueTask RunPageCleanupAsync(Func<CancellationToken, ValueTask> cleanup,
+            CancellationToken cancellationToken);
+        // commit前、候補page.Retire後だけ。候補surface解放と旧gate再開。
+        // host停止中は再開しない。旧view/historyを変更しない。
+        ValueTask DiscardAsync(CancellationToken cancellationToken);
+    }
+}
+namespace Lumyte.UI.Application.Navigation
+{
+    public static class UiNavigationViewExtensions
+    {
+        // slotごとに同じhostを返す。static rootとnavigationを同時に所有しない。
+        public static IUiNavigationHost CreateNavigationHost(this UiApplicationViewSlot slot);
+    }
+}
```

Application本体はNavigationを参照せず、内部の汎用view交換portで表示・入力・IME・GPUの寿命だけを管理する。Application.NavigationがApplicationとNavigationを参照して両者を接続し、NavigationからApplicationを参照しない。IUiNavigationViewChangeはpageのPrepareより先にnavigatorが所有する。RunPageCleanupAsyncは承認済みchangeのRetireをUI-0016の失効可能なadmissionへ包み、停止後やConfigureAwait(false)後も同Dispatcherのcleanup queueへ戻れるようにする。未準備のscopeにも使えるが、別Window/世代や未awaitの子仕事へ受付権を持ち出さない。DisposeAsyncは完了したtokenの保持を返す操作であり、未完了のDiscard/退役を成功扱いにしない。清掃に失敗したtokenとactivation leaseは保持して再試行する。

slotは候補surfaceを入力、focus、Popup owner、Automation/Diagnosticsへ公開せず、layout・描画準備・TextBoxのprovider/編集capability検査を終える。必要なcapabilityがなければinvalid-editor等でcommit前に失敗する。新しい同一Window bindingの準備には次の任意capabilityを使う。

```diff
+namespace Lumyte.UI.Windowing
+{
+    public interface IUiWindowPresentationReplacementManager
+    {
+        // current=nullは初期binding。非nullは同windowの現在bindingそのもの。
+        // source/adapterに必要能力がなければ、旧bindingに触れずNotSupportedException。
+        IUiWindowPresentationReplacement BeginReplacement(UiWindowId window,
+            UiWindowBinding? current, UiSurface candidate);
+    }
+    public interface IUiWindowPresentationReplacement : IAsyncDisposable
+    {
+        ValueTask PrepareAsync(CancellationToken cancellationToken);
+        bool TryValidateCommit(out string code);
+        // Readyのみ。旧bindingを非activeにし、候補のactive bindingを返す。
+        // Window ID/SourceId/OS Windowは保持し、focus/Show/Closeを呼ばない。
+        UiWindowBinding Commit();
+        // 旧frameのGPU完了まで待つ。完了後も旧surfaceは利用側が保持する。
+        ValueTask DrainPreviousAsync(CancellationToken cancellationToken);
+        // 旧bindingだけを解放する。候補bindingの所有権はCommitで利用側へ移譲済み。
+        ValueTask ReleasePreviousAsync(CancellationToken cancellationToken);
+        // 未commit候補のGPUだけをdrainし、binding/surfaceは保持する。
+        ValueTask DrainCandidateAsync(CancellationToken cancellationToken);
+        ValueTask DiscardAsync(CancellationToken cancellationToken);
+    }
+    // IUiWindowSourceとNative presentation factoryが任意に実装する準備能力。
+    public interface IUiWindowPresentationStager
+    {
+        // current/view/candidateは借用。ここでは資源を取得しない。
+        IUiStagedWindowPresentation BeginReplacement(IUiWindowPresentation? current,
+            UiWindowPresentationView view, UiSurface candidate);
+    }
+    public interface IUiStagedWindowPresentation : IAsyncDisposable
+    {
+        ValueTask PrepareAsync(CancellationToken cancellationToken);
+        bool TryValidateCommit(out string code);
+        // 非公開で準備した描画登録をactiveへ交換し、tokenの所有権を返す。
+        IUiWindowPresentation Commit();
+        ValueTask DrainCandidateAsync(CancellationToken cancellationToken);
+        // commit前の候補資源だけを回収。旧presentationには触れない。
+        ValueTask DiscardAsync(CancellationToken cancellationToken);
+    }
+}
```

UI-0014の初期BindPresentationAsyncを後から二重binding可能と読み替えない。このPRでHostedUiWindowManager、WindowSystemUiWindowManager、FallbackUiWindowManagerへreplacement capabilityを追加し、standard source/native presentation factoryへstager実装を接続する。custom manager/sourceは独自実装を注入でき、能力不足はnavigation初期化時に明示する。通常の静的rootやPopupだけのホストには追加能力を要求しない。

stagerは旧viewを記録し続けながら候補のlayout済みDrawList・GPU準備を非公開で保持する。Commitは描画登録の参照交換だけを行い、旧tokenを破棄しない。旧GPUの完了確認とbinding返却はmanager側のDrainPrevious/ReleasePreviousが行う。source/native factoryに追加能力がない場合、通常PreparePresentationAsyncを二度呼んで同じ描画先へ候補を先に公開する代用はしない。

commit後の入力gateは新bindingの初回提示とGeometryRevisionの適格性を確認してから開く。旧bindingの提示済みrevisionを候補へコピーして早期にhit-testしない。原子的な切替は公開されたviewの登録を指し、GPUの画面走査を同期完了した保証ではない。初回提示失敗はcommit後のhost障害・復旧として扱う。

window manager、slotのinput route、履歴/Current、Diagnosticsの公開切替は同じowner Idleの同期区間で行う。失敗可能な検証・割当・登録枠予約・生成処理はPrepare/Quiesceまでに済ませ、Commitは契約上例外を投げず、外部callbackを実行しない。custom実装がこの契約に違反した場合はhostをFaultedとして入力を止める。既に観測可能になった新pageを旧pageへ黙って戻さない。

候補にはwindow/sourceの接続・geometry revision、viewport/scale、候補tree/layoutの変更世代、renderer/device epochを記録する。Quiesceのawaitから戻った後、同じowner Idle turnでslotからmanager/stagerまでTryValidateCommitを行い、間にawait・pump・callbackを挟まずCommitする。resize/DPI/device復旧や候補tree変更で一致しなければcandidate-staleとして旧viewを維持し、候補をdiscardする。Commit内の再layoutや自動再Prepareで隠さない。別threadのdevice loss通知もownerで反映し、commit後に判明した喪失はUI-0016の復旧経路へ渡す。

### 遷移手順と取消

1. 要求受付時に引数をsnapshotし、現在のActivationIdと入力の因果IDを保存する。Back/Forwardの行き先もこの時点で確定する。
2. 次のIdleでview changeと新activation leaseを取得し、pageのPrepareAsyncでデータ準備と同期Buildを終える。view changeへrootを渡し、候補surfaceを非公開で準備する。旧pageは表示・操作を続ける。
3. 現pageのLeaveGuardを一度呼ぶ。拒否・例外・取消なら候補を清掃し、旧pageと履歴を維持する。初期表示ではguardは呼ばない。guardは自身のアプリ状態・外部更新の整合を担当し、その副作用をnavigationはrollbackしない。
4. hostが入力gateを閉じ、既存操作、IME/candidate、Popup、診断操作をdrainする。待機中もOS pump、dispatcher、関連Advanceを続ける。要求元と現在のActivationId、候補のcommit適格性を最後に再検査する。
5. 同期commit区間でview、input route、公開metadata、Current、履歴を切り替える。旧activationのLifetimeCancellationを取消し、ResultをCommittedとして確定する。onCompletedは区間を抜けてから実行する。
6. 旧viewのGPU退役→RunPageCleanupAsyncで旧activation.RetireAsync→旧binding/surface解放→旧activation.DisposeAsyncの順で清掃する。

取消はcommit直前まで受け付ける。候補資源の回収と旧入力gateの再開を終えてからCanceledを返し、取消済みcaller tokenで清掃を中断しない。commit後のCancelは結果を変えず、旧pageの清掃を続ける。準備失敗した入力を自動再送したり、同じfactoryを再実行したりしない。

未公開の候補もDrainCandidateAsync→RunPageCleanupAsyncでcandidate.RetireAsync→DiscardAsyncによるbinding/surface解放→candidate.DisposeAsyncの順とする。pageのPrepare途中で失敗してroot/surfaceがなければdrainとsurface解放はno-opだが、取得済みscopeのRetireは省略しない。清掃失敗では要求結果をFailed/cleanup-blockedとして固定し、未解放leaseをRetirementBlockedへ保持する。起動中の要求もCompletionを返すため、InitializeAsyncと停止Retireを相互待ちにしない。

原子性の対象はCurrent、履歴、可視view、入力先、公開Targetである。guardの保存処理、既に完了した入力、Popup終了、IMEの取消/確定まで逆実行しない。Quiesce後に取り消した場合は旧surfaceの同じ接続が残るため、まだ適格なfocus bookmarkを復帰できるが、閉じたPopupやIME preeditを再生成しない。

退役が完了するまで次のnavigationはBusyとし、未退役旧pageを一件に制限する。RetireAsync/host清掃失敗ではRetirementBlockedとし、tokenとscopeを保持してRetryRetirementAsyncで未完了段階だけを再試行する。scopeの最終DisposeAsyncは一度だけ呼ぶ。.NET DI等が破棄開始を先に確定する場合、例外後の再呼出しで残余解放できるとは扱わず、terminal errorを保存してFaultedとする。commit済みのページを失敗前へ戻さない。

退役失敗はローカルsnapshotのRetirementFailureへ保持し、既に確定したCommitted結果を書き換えない。標準contentでもページcontextから借用したIUiNavigatorとDispatcherをアプリの制御側へ渡して観測・再試行できる。旧input gateが閉じたrollback失敗でもdispatcherの管理queueは進むため、外部制御側がownerへRetryRetirementAsyncの開始を配送し、そのTaskを外側でawaitできる。再試行を同じページのButton操作だけに依存させず、Taskを返すdelegateや同期waitをDispatcherへ渡さない。停止開始後は通常の再試行要求を受けず、hostのRetryStopAsyncへ引き継ぐ。

### 視覚遷移の追加範囲

実装PR 17の[UI-0021](UI-0021-connected-and-presence-transitions.md)でpage/connected演出を追加する。上記commitでCurrent/履歴/TargetとCommitted結果を確定する契約は維持し、準備済みの新旧snapshotをcommit後のAnimating phaseで合成する。Request.VisualCompletionとSkipAnimationを追加し、論理commitと視覚完了を分離する。最終状態の提示後に新入力gateを開き、旧captureの最終GPU使用をdrainしてから既存の旧page退役手順へ進む。演出中と退役中は次navigationをBusyに保ち、省略/中断で旧pageへrollbackしない。

候補のkey/capture/予算と全generationはPrepare/Quiesceで検証し、Commit内に失敗可能なcaptureやcallbackを追加しない。通常候補の不正と任意演出の能力不足を分け、後者だけを即時切替へfallbackする。演出完了待ちをRequest.CompletionやDiagnostics Clickのlease待ちへ持ち込まない。

### focus・IME・Popup・Diagnostics

新しいpageは別SurfaceId/Automation接続IDを持ち、focusなしから開始する。Back/Forwardでも旧UiFocusBookmarkを新treeへ使い回さない。ページを跨ぐpointer capture、key owner、消費系列、編集session、候補panelを引き継がず、終端入力を旧所有先へ固定して消費する。WindowのOS focus/UiWindowIdは維持する。

guardが許可した後、hostはUI-0012のSuspendAsyncによって既定のCancel終了をdrainし、候補表示・Menu/Passive Popupを子から閉じる。確定してから移動したいアプリは先にSuspendAsync(Commit)の完了と変更callbackを待って要求する。新editorの接続はcommit後のfocus同期で開始し、旧TSF lock/DOM通知/session tokenを先に解放しない。新scopeの開始失敗はcommit後のhost障害として報告する。

ButtonのOnClickからのRequestNavigateはqueueだけを行う。特にDiagnosticsのClick Jobが保持するinput leaseの中で、そのnavigationのCompletionを待たない。hostは因果IDで要求元の操作を識別し、そのUp、結果の確定、自身のinput cleanupとlease返却を待ってからcommitする。他の旧TargetのJobは取消・drainし、新規Jobの受付を一時停止する。guard/prepare中も既存JobのAdvanceを止めないため、navigationとClickの相互待ちを作らない。

Application slotのDiagnostics bridgeは、次の公開契約を既存`Lumyte.Diagnostics.UI.Automation`へ追加して使用する。runtimeはNavigation/Applicationを参照しない。候補TargetのID・登録枠・hostを非公開で予約し、旧Targetを保持したままpause/drainする。通常Register/Disposeの組合せで旧IDを失わせない。

```diff
+namespace Lumyte.Diagnostics.UI.Automation
+{
+    public sealed class UiAutomationDiagnosticsRuntime
+    {
+        // owner threadのみ。同一name/currentの置換枠を予約する。current=nullは初期登録。
+        // 同じ登録への未完了予約は一つ。候補はtargets/Startから見えない。
+        public UiAutomationDiagnosticReplacement ReserveReplacement(string name,
+            UiAutomationDiagnosticRegistration? current, UiSurface candidate,
+            IUiAutomationHost candidateHost, UiAutomationTargetOptions? options = null,
+            UiAutomationDiagnosticRegistration? parent = null, Guid? popupId = null);
+        // 現dispatchのJob ID。別runtime/control group・通常物理入力はnull。
+        public Guid? CaptureCurrentOperation(UiAutomationDiagnosticRegistration registration);
+    }
+    public sealed class UiAutomationDiagnosticReplacement : IAsyncDisposable
+    {
+        public Guid CandidateTargetId { get; }
+        // current groupの新規Job/controlを止め、原因Jobの完了と他Jobの取消を待つ。
+        // runtime.Advanceは外側hostが続ける。現存する原因IDは同groupのJobだけを許す。完了して保持期限を過ぎた原因は待機対象がないため除外対象にもせず、他の現存Jobを除外しない。
+        public ValueTask QuiesceAsync(Guid? originatingOperation, CancellationToken cancellationToken);
+        public bool TryValidateCommit();
+        // quiesce済みのowner Idleのみ。旧Targetを外し、同じ登録枠へ候補を公開する。
+        // 割当・callback・awaitなし。返した登録はslotへ所有権を移す。
+        public UiAutomationDiagnosticRegistration Commit();
+        // 未commitのみ。旧Jobの清掃後に予約を返し、旧IDを再受付可能にする。
+        // runtime停止中は再開しない。解除済みcontrolを復活させない。
+        public ValueTask DiscardAsync(CancellationToken cancellationToken);
+        // Commit/Discardの完了後にtokenを返す。未完了を成功扱いにしない。
+        public ValueTask DisposeAsync();
+    }
+}
```

parent/popupIdは追加Windowの初回公開を既存の入力control groupへ参加させる場合だけ指定する。子Targetの入力・テキスト・値閲覧・画像の許可は親の許可との積とし、初回公開と置換の両方で権限を拡張しない。root Targetの交換時は子Targetと準備中予約のgroup/parentを同じcommitで更新し、同時にquiesceしている別Windowのgateを早期に再開しない。

予約は現在の一登録枠を引き継ぐため、MaximumTargetsに達した現在Targetも置換できるが、同時に公開できるTarget数を増やさない。最初の登録では通常の容量検証を行う。Prepare失敗で予約できなければ旧Targetへ触れず失敗する。Quiesceの取消後も旧Jobのcleanup完了を待ち、Discardで旧Targetの同じIDを再開する。commitは旧Targetの公開解除と候補Targetの公開を同時に行い、旧登録はIsReleasedへ進む。確定済みJob結果は既存の有界保持期間と所有者照合を維持し、旧Job IDから照会できる。結果保持にsurface/hostを残さない。

runtimeはJob実行の非同期呼出し範囲へJob IDを設定し、標準Automation hostはowner配送へその因果contextを引き継ぐ。CaptureCurrentOperationはその場のdispatchと同じcontrol groupの実行中Jobだけを返し、完了後に残った古いcontextは無効とする。Popup由来の同group操作もslotのnavigation要求と結び付ける。remote payloadに任意の原因IDを指定させず、別groupのJobをdrain対象から除外できないようにする。gateは新規操作を止めるもので、受理済み原因JobのUp・結果固定・cleanupのowner配送を妨げない。旧Targetの制御権・host lease・Sequenceを新Targetへ移譲しない。

旧surfaceに束縛されたUiAutomationPage/Locator/remote Targetは新pageへ再対応させず、target-unavailableで失敗する。クライアントは次節のmetadataから対象を取り直す。初期は通常Button操作とmetadata照会で検証し、専用Navigate RPC、URL操作、開いた文字列のscript実行は追加しない。物理入力domainの排他はUI-0014どおり、登録Targetが変わっても同じhostが維持する。

### navigationの観測

Application slotは公開済みviewのコピーだけを返す。WindowContextIdはUI-0016のUiApplicationWindowContext.Idで、ページ切替でも維持する。Currentは初期commit前と停止後にnull、候補の準備中・拒否・失敗では旧値のままとする。Revisionは公開内容を交換するたびに増やし、履歴の引数本文、例外内容、編集文字列を含めない。

```diff
+namespace Lumyte.UI.Application.Navigation
+{
+    public sealed record UiNavigationCommittedView(Guid NavigatorId, Guid EntryId,
+        Guid ActivationId, string Route, Guid SurfaceId, Guid? TargetId);
+    public sealed record UiNavigationWindowSnapshot(Guid WindowContextId, UiWindowId Window,
+        ulong Revision, UiNavigationCommittedView? Current);
+    public static class UiNavigationViewExtensions
+    {
+        // owner上で取得。navigationを設定していなければInvalidOperationException。
+        public static UiNavigationWindowSnapshot CaptureNavigationSnapshot(this UiApplicationViewSlot slot);
+    }
+}
+namespace Lumyte.UI.Application.Navigation.Diagnostics
+{
+    // UseDiagnostics().UseNavigationDiagnostics()が既存pointへ登録するScoped ui.navigation。
+    public sealed partial class UiNavigationDiagnostics
+    {
+        // 空は登録中のnavigation slot一覧、非空はGuidのD形式一件。最大36文字。
+        [DiagnosticOperation(DiagnosticPermission.Observe, Id = "state")]
+        private DiagnosticResult<UiNavigationStateReceipt> State(
+            [DiagnosticArgument(MaxLength = 36)] string windowContextId);
+    }
+    public sealed record UiNavigationStateReceipt(
+        [property: DiagnosticMember(MaxLength = 32768)] string Json);
+}
```

`window-context-id`は既存の生成診断schemaに合わせて必須のstringとし、空文字列で一覧を指定する。省略可能なC#引数や新しいwire schemaは追加しない。公開reader `UiNavigationStateSchema.Parse`は以下の閉じた形式を検査してdetachedなsnapshotへ変換する。

Diagnostics bridgeはApplication側の内部registryからsnapshotをowner上で取得してdetachedな結果へ固定する。public contributorを登録するための内部constructor/registryは同assemblyに置き、ページや送信threadへregistryを公開しない。UseDiagnostics未指定ではcontributorも照会用workも作らない。登録済みnavigation slotがなければ一覧は空、存在しないIDはnot-found、不正なIDはinvalid-argumentとする。

`state`は既存ProtocolVersion 2とカタログのObserve操作を使い、新RPC/transportやui.automation schema 1〜3の変更を必要としない。内側JSONは`{"schemaVersion":1,"windows":[...]}`に固定し、各要素は`windowContextId`、`windowManagerId`、`windowValue`、`revision`、`current`だけを持つ。currentはnullまたは`navigatorId`、`entryId`、`activationId`、`route`、`surfaceId`、`targetId`の値objectとする。GuidはD形式、windowValue/revisionはJSON整数、targetIdはDiagnostics登録がなければnull。未知member、別version、型違いをclient側で拒否する。初期hostの一slotを対象とし、将来の複数Window一覧でも32KiBを超えた結果を黙って切らない。

Current/history、上記snapshot、Target交換は一つの同期commit区間で確定し、同じ観測に旧EntryIdと新TargetIdを混在させない。stateで得たTargetIdをui.automationの通常操作へ渡し、remote controlは新Targetについて取り直す。取得後にさらにnavigationされた場合は古いTargetをtarget-unavailableとして扱い、route名やWindow IDから新Targetへ自動再送しない。

### アプリ停止とBrowser

UI-0016のcontent controllerはBeginStopで新要求とcommitを止め、全activationのLifetimeCancellationとguard/prepareを取消す。guard/prepareの実行が静止したら未commit要求をCanceledで固定し、IsStoppedをtrueにする。停止時は候補清掃の所有をcontentの終了手順へ移すため、要求Completionは資源解放の完了を表さず、host Completionで終了を確認する。slot解放待ちやRetirementBlockedをIsStoppedの条件へ含めず、初期InitializeAsyncと停止の相互待ちを避ける。取消に協力しないユーザー処理を強制終了したとは報告しない。

その後のslot GPU drain→contentの再試行可能なRetireAsync→slotのsurface/input/binding最終解放→ReleaseAsyncによるscope破棄の順を守る。content.RetireAsyncでは保持中の候補・旧viewのGPUもdrainし、全activationのpage cleanupを終える。候補・旧viewはそのsurfaceも返し、現在viewのsurfaceだけはslotの次段階へ残す。ReleaseAsyncはまだ破棄していないscopeを一回だけ解放する。途中の失敗でleaseを捨てず、既にterminal failureとなったDIの破棄を再呼出ししない。

Browserでも初期は同じin-app履歴だけを使う。ブラウザーのURL、戻る/進む、reload、deep linkとは同期せず、popstateを横取りしない。URL bridgeは任意の後続adapterとする。追加時には型付き引数の明示codec、公開可能な値の選別、初期URLの検証、popstate拒否/取消時の復元、commit後だけのpushState/replaceState、重複イベントと再入、reload時のscope再生成を別途定義する。

### 宣言された複数Windowとの関係

2026-10-29追記。[UI-0016のUseWindows](UI-0016-ui-application-framework.md#複数windowの宣言api)で生成した各instanceのview slotに、独立したUiNavigatorとactivation providerを接続する。「一つのWindow view slotに一つ」はアプリ全体を一Windowへ制限する意味ではない。route登録表は共有してもNavigatorId、履歴、現在activation、要求queue、guardは共有しない。同じrouteを二つのWindowで開いた場合も別ActivationId/page leaseとする。

同keyのWindow宣言継続、title変更、並べ替えではnavigationを再初期化しない。初期destinationはinstance開始時だけ適用し、宣言を再構築するたびに現在ページを初期ページへ戻さない。明示的なページ変更はそのWindowのUiPageActivationContext.Navigationへ要求する。Window終了はそのnavigatorを停止して既存のRetire/Releaseへ接続し、他Windowの履歴を消さない。通常のnavigation guardでWindow寿命終了を暗黙拒否せず、閉じる承認はWindowのonCloseRequestedでモデルを変更する前にアプリが行う。

異なるWindowへの暗黙navigation、Widgetの移送、履歴/DI scopeの共有は行わない。Windowの再追加は同keyでも新instance/navigatorとなる。検証には同routeの同時表示、個別Back/Forward、一方の閉鎖中に他方が操作可能なこと、旧Windowの遅延prepare/入力/Diagnostics応答が新instanceへ届かないことを含める。

## 検討した代替案

- root Panelの子置換は単純だが、切替後のlayout失敗から接続IDやfocusを復元できない。別tree/surfaceの準備とbinding交換を採用する。
- 履歴へpage instance/DI scopeを保持すると、IME、Popup、資源、診断Targetの停止時点が曖昧になる。初期はdestinationだけを保持して再生成する。
- 複数遷移の自動queue/latest-winsはguardの再実行や準備中scopeの増加を招く。まずBusyと明示取消で一件に制限する。
- routeをそのままBrowser URLへ接続すると、非公開の引数や拒否されたnavigationまで履歴へ反映し得る。共通navigationとBrowser bridgeを分離する。

## 結果と影響

DIやOSへ依存せず、同じpage activation契約でデータ準備、履歴、表示切替と清掃を検証できる。旧pageを維持したまま候補を準備できる一方、ピーク時は二つのtree/scopeと描画資源を保持する。再生成方式なので、アプリが明示保存していないページ内状態はBack/Forwardで復元されない。

## 検証方針

- route文法、登録型照合、引数の変更遮断とGetArgumentsのコピー、Push/Replace/Back/Forward、上限、空履歴、forward切捨て、同じrouteへの新entryを確認する。
- DIなしのproviderで初期表示、遅延Prepare、owner同期Build、guard拒否/例外、取消、Busy、再入要求を確認する。失敗前後で旧view/EntryId/SurfaceIdを維持することを検証する。
- replacementの能力不足、layout/描画準備/編集capability/診断枠の失敗、resize/DPI/device世代変更、commit直前取消、commit後取消を確認し、候補の早期公開と旧pageの再attachを防ぐ。
- native/Hostedで安定したWindow IDのまま切り替え、旧GPU使用中のscope保持、Retire失敗の再試行、最終scope Dispose失敗のterminal扱いを確認する。
- IME変換中、候補/Popup表示中、pointer/key押下中の遷移と取消を確認し、旧系列のUpを新pageへ渡さないことを検証する。
- Diagnostics Clickからの要求でJob結果・lease返却・commitが相互待ちせず、新Targetの制御権を別途取得すること、旧Locator/Targetが再利用されないことを確認する。予約中の不可視性、失敗時の旧Target ID再開、既存Jobの結果保持、Observe専用stateの閉じたschemaと同一commitのID対応を検証する。
- 停止中のPrepare遅延完了、guard、清掃失敗を含め、IsStoppedとslot解放の循環がなく、GPU退役前のDI破棄がないことを確認する。失敗候補のsurface保持、停止後ConfigureAwait(false)からのRetire配送、admission失効と別appへの持出し拒否を検証する。
- Browserでin-app履歴を操作してもURL/historyを変更せず、同じheadless契約で決定的に検証できることを確認する。

## 実装検証の記録

2026-10-30、Linux、.NET SDK 10.0.401で実施した。

| 検証 | 結果と確認範囲 |
| --- | --- |
| Release solution build | 警告0、エラー0。追加package・sample・試験projectを含む |
| managed試験 | 全37 projectで2,001件成功、75件skip、失敗0。Navigationは42件。Application 24件、Windowing 37件、Diagnostics UI Automation 47件を含む |
| 遷移と寿命 | 非公開準備、guard拒否/例外、取消、Busy、履歴上限と再生成、同時に遷移する複数Window、閉鎖・再追加、清掃再試行と最終破棄一回を確認 |
| 入力・IME・診断 | 旧pointerのUp抑止、旧IME sessionの解放待ち、新editorのfocus後接続、Click Jobとの相互待ち防止、満杯枠のTarget交換、子Targetの再接続・許可の継承、閉じたmetadata schemaを確認 |
| Native AOT | Headless navigation sampleのstrict publishと生成実行ファイルで、型付き行き先・Surface交換・同じWindow ID・終了が成功 |
| Browser | trimming publish、Chromium/SwiftShader WebGPU、CPU倍率6で通常クリックと切替前後のGPU PNGを検証。灰色128±2からRGB(64,128,191)±2へ変化し、Window IDとURL/historyは保持、Surface IDは交換 |
| package・文書 | 標準host・3 driver・基本Diagnostics・2統合packageをpackし、DLL・README・依存関係を検査。標準5 packageのproject／restore／実行時／NuGet依存にNavigationがないことを確認。Markdown lint（後続を含む244ファイル）と相対リンクを確認 |

終了クリックとlease返却の競合、およびBrowser sampleのソフトウェアGPU向け待機期限は、元のUI-0016/PR #89へ修正を置き、本PRへ継承する。通常Automation APIの既定期限は変更しない。

ソフトウェアGPUと偽IME backendの結果は実機検証と区別する。Windows実機のSTA/TSF/native Windowでのページ切替、Linuxの実Window、物理GPUのdevice loss、Browserの実IME入力はこの環境では未検証。BrowserのURL同期、DIページ登録、Signal、視覚遷移は対応する後続PRの範囲とする。

## 交換段階と清掃進捗

交換tokenの段階はPreparing、Quiesced、Committed、ReleasedまたはDiscarded、Disposedとする。相互排他的な状態を独立boolで重ねない。入力gateは取消時の復元に必要な独立状態として保持する。旧View・候補Viewの清掃進捗はView自身へ委ね、解放完了時にtokenの所有参照を外す。論理commit、演出終了、GPU退役、ページRetire、DI scope解放の順序と各失敗の再試行方針は維持する。

ViewはVisuals drain（演出導入後）、menu guard、text接続、automation host、所有binding、surfaceの順に清掃進捗を保持する。同時の解放要求は進行中のtaskへ合流し、失敗した手順から再試行する。成功済み手順は繰り返さない。交換先へ移管したbindingはViewから解放せず、交換tokenの退役手順に委ねる。

## Element分離の適用

ページactivationはWidget宣言とページscopeを用意し、hostが候補Surfaceと独立Elementを作る。
通常の部品再構築ではElementの状態を保持するが、ページ切替・Back・Forwardでは新しい
Surface / Elementの寿命を開始する。履歴にElementやDocumentを保存しない。
候補の親付き宣言はSurfaceの共通所有権検証で拒否し、Navigatorでmounted親を調べない。
確定後は旧Elementツリーの入力・編集を停止し、最後の提示・GPU退役・ページRetire・
DI scope解放を既存の段階に従って進める。

### Element分離後の再検証

ReleaseのNavigation 42件で、履歴、guard、入力系列、IME、Window、失敗・取消・資源退役を確認した。

Application 28件、Windowing 41件も成功した。headless sampleで型付き遷移、新しいSurface、
同じWindow ID、順序付き停止までを確認した。
