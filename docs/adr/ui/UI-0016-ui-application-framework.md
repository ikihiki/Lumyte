# ADR-UI-0016: UIアプリケーションの起動・描画・終了を所有する標準ホスト

- 状態: 採用
- 日付: 2026-10-30
- 対応: 実装PR 12、前提: 実装PR 1〜11

## 背景

既存のUIはCompositionでツリーを組み立てられるが、利用側がPlatformのpump、入力配送、DPI、Drawing2Dの準備、GPU実行・提示、IME、Windowの終了を接続する必要がある。[UI-0007](UI-0007-window-input-and-dpi-integration.md)・[UI-0008](UI-0008-render-graph-and-resource-lifetime.md)のサンプル補助だけでは、UIを主目的とするアプリでもGPUループを書くことになる。

Graphics.Hostingはdeviceの世代とleaseを管理し、ExecutionRuntimeは固定Hostからのpumpを要求する。いずれもUIのevent loopを所有しない。これらを再利用する標準ホストを設け、ページの変更やDI scopeの解放が、処理中の入力・IME・GPU使用を追い越さない境界を定める。

## 決定

### 範囲と依存方向

`Lumyte.UI.Application`を追加する。アプリはbuilderでdriver、内容factory、サービスを登録し、BuildとRunAsyncで起動する。標準ホストが入力、layout、描画、swapchain、色変換、GPU退役、Window manager、Popup、IMEおよび任意のAutomation/Diagnostics接続を所有する。利用者へRenderer2D、RenderGraph、Submit/Presentの記述を要求しない。

範囲は一つのprimary app root、そのPopup・IME候補、および宣言された独立文書/Tool Windowとする。独立Windowの宣言は本ADR、Windowごとのページ接続はUI-0017/0018、Signal引数はUI-0019で追加する。3D/ゲームへの埋め込み、透過top-level window、独自描画driverの公開拡張は対象外とする。ゲーム等への低水準な埋め込みは従来のUiSurface経路を維持する。ページ遷移はUI-0017、Minimal API風のMapPage/MapGroupとDIページ登録はUI-0018で追加し、本ADRはそれらを前提にせず静的rootだけで動作する。

UI本体へ追加する依存はBCLだけのIUiDispatcherである。ApplicationはUI、UI.Windowing、UI.TextEditing、Input、Drawing2D、Graphics.Hosting、ExecutionとMicrosoft.Extensions.DependencyInjection 10を参照する。Desktop/Browser/Headless driverは別packageに分け、Platform/backend依存をUI本体へ戻さない。Resourcesは任意の既存登録を使用し、UI専用ResourceManagerを新設しない。Diagnostics接続も任意の`Lumyte.UI.Application.Diagnostics`へ分ける。

| driver package | 初期対応 | 実行場所・描画 | 文字入力 |
| --- | --- | --- | --- |
| Lumyte.UI.Application.Desktop | Windows 10 1809以降、Linux X11 | host所有thread、標準wgpu backend | WindowsはUI-0012のTSF。LinuxのIME接続は未対応 |
| Lumyte.UI.Application.Browser | 既存.NET Browser/WebGPU対応環境 | 現在のJS thread、canvas、rAF | UI-0012のtextarea。独自候補capabilityはfalse |
| Lumyte.UI.Application.Headless | .NETテスト環境 | host所有の単一thread、CPU frame | 初期は文字入力backendなし。物理IME試験を代用しない |

上表は実装対象であり、実機検証済みの宣言ではない。Desktopの初期backendは既存WgpuDevice、BrowserはBrowserDeviceを使用する。Vulkanへの自動切替は行わない。WebGPU/backend利用不能は起動失敗として返す。

### 実装との対応

本PRはApplication、Desktop、Browser、Headless、任意のDiagnosticsの5 packageを追加する。各packageのREADMEと、`samples/Lumyte.UI.Application.{Desktop,Browser,Headless}.Sample`を実行可能な入口とする。ページ、MapPage、Signal、自動アニメーション、C8 Dialogの実装は対応する後続PRの範囲を維持する。

追加した公開APIは下記の基本APIに加え、WindowContextのServices/Automation/Popups/PopupOwner/Chrome/RequestStop/GetPopupAutomation、ApplicationのArguments/LastWindowUpdateFailure、`IUiApplicationGraphicsContent.RebuildGraphicsAsync`である。PopupOwnerは借用であり、利用者がDisposeしない。ChromeはHostedだけに存在し、Header/ResizeGripを同じ入力domainのAutomation hostとして提供する。内部の装飾用Tool Windowは本文の座標系や子コレクションを変更しない。

IUiWindowManagerのResize/BringToFrontは要求結果を提示後に完了する。未提示の同種要求はsupersededとする。独自managerはこの2メソッドを実装し、非対応対象にはunsupportedの要求結果を返す。新しい提示順はUiWindowPresentationView.StackingOrderに保持し、入力順は提示済みviewから決める。

### 公開APIと構成

比較元はmain `20f666ff8f64a63e65407936d32fb98cfa19c9d8`。UI-0001〜0015およびINPUT-0007の提案APIを前提とする。Widgetは`Lumyte.UI.Compose.Definitions.Widget`、Color2DはGraphics.Drawing2D、Vector2はSystem.Numericsを使う。

```diff
+namespace Lumyte.UI
+{
+    public interface IUiDispatcher
+    {
+        // owner threadならtrue。現在のphaseがIdleであることまでは意味しない。
+        bool CheckAccess();
+        // 常にqueueし、ownerのIdleで一回実行する。同期delegateだけを受け付ける。
+        ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default);
+        ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default);
+    }
+}
+namespace Lumyte.UI.Application
+{
+    public enum UiApplicationState
+    {
+        Created, Starting, Running, Recovering, Stopping, StopFailed, Stopped, Failed
+    }
+    public sealed record UiApplicationOptions
+    {
+        public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
+        // 描画対象全体の不透明背景。各成分finiteかつ[0,1]、A=1を要求する。
+        public Color2D Background { get; init; } = new(1, 1, 1, 1);
+        // 正かつ有限。Desktopの最大描画頻度。BrowserはrAF頻度も上限とする。
+        public TimeSpan FrameInterval { get; init; } = TimeSpan.FromSeconds(1d / 60);
+        // UI queue/Executionの各turnへ適用する。同期callbackを強制中断しない。
+        public int MaximumWorkItemsPerTurn { get; init; } = 256;
+        public TimeSpan MaximumWorkTimePerTurn { get; init; } = TimeSpan.FromMilliseconds(4);
+        public int MaximumQueuedUiWork { get; init; } = 4096;
+        public int MaximumFramesInFlight { get; init; } = 2;
+    }
+    public sealed class UiApplicationException : Exception
+    {
+        // 固定code。原因はInnerException、cleanup失敗はFailureにも保持する。
+        public string Code { get; }
+    }
+    public sealed class UiApplicationBuilder
+    {
+        // MS DI 10。Build後は読み取り専用。登録中にproviderを作らない。
+        public IServiceCollection Services { get; }
+        public UiApplicationBuilder Configure(UiApplicationOptions options);
+        // factoryをowner threadで一回呼び、戻ったmanagerの所有権をhostへ移す。
+        public UiApplicationBuilder UseWindowManager(
+            Func<UiWindowManagerContext, IUiWindowManager> factory);
+        // UseContentと排他。factoryはowner Idleで一回呼ぶ。
+        public UiApplicationBuilder UseRoot(Func<UiApplicationWindowContext, Widget> factory);
+        // factoryにはroot providerを借用で渡す。戻ったcontentはhost単独所有。
+        public UiApplicationBuilder UseContent(Func<IServiceProvider, IUiApplicationContent> factory);
+        // layout provider/そのfont engineは借用。OS serviceは選択driverが用意する。
+        public UiApplicationBuilder UseTextInput(Func<IServiceProvider, IUiTextLayoutProvider> provider);
+        // 一回だけ。登録をfreezeしてproviderを作る。OS/GPU/Window/UI内容は作らない。
+        public UiApplication Build();
+    }
+    public sealed class UiApplication : IAsyncDisposable
+    {
+        // argsをコピーする。reflectionによるassembly/ページの自動走査は行わない。
+        public static UiApplicationBuilder CreateBuilder(string[]? args = null);
+        public UiApplicationState State { get; }
+        public Exception? Failure { get; }
+        public IUiDispatcher Dispatcher { get; }
+        // terminal状態までのTask。StopFailedでは未完了のまま保持する。
+        public Task Completion { get; }
+        // 一回起動。token取消は停止要求となり、開始済みcleanupを放棄しない。
+        public Task RunAsync(CancellationToken cancellationToken = default);
+        // thread-safe。callbackから可。queueするだけで待たない。
+        public void RequestStop();
+        // 同時停止は集約。tokenはcallerの待機だけを取消す。
+        public Task StopAsync(CancellationToken cancellationToken = default);
+        // StopFailedだけ。成功済み段階を繰り返さず、失敗したretryable cleanupを再開。
+        public Task RetryStopAsync(CancellationToken cancellationToken = default);
+        // 停止完了まで待つ。DI Disposeの失敗後は再試行せず、保持済み失敗を返す。
+        public ValueTask DisposeAsync();
+    }
+    public sealed class UiWindowManagerContext
+    {
+        // 全て借用。factoryはrootを自分でRegisterしない。
+        public IUiWindowManager DefaultManager { get; }
+        public IUiWindowSource RootSource { get; }
+        public IUiDispatcher Dispatcher { get; }
+        public TimeProvider TimeProvider { get; }
+    }
+    public sealed class UiApplicationWindowContext
+    {
+        // app内で再利用しないWindow context ID。Windowは選択managerのID。
+        public Guid Id { get; }
+        public UiWindowId Window { get; }
+        public IUiWindowManager Windows { get; }
+        public IUiDispatcher Dispatcher { get; }
+        public UiApplicationViewSlot View { get; }
+        // UseTextInputで指定したprovider。Compositionの引数へ明示して渡す。
+        public IUiTextLayoutProvider? TextLayoutProvider { get; }
+    }
+    public sealed class UiApplicationViewSlot
+    {
+        // owner Idle専用の借用参照。callerはDisposeしない。空/解放後はnull。
+        public UiSurface? CurrentSurface { get; }
+        // 初期の空slotに一回。rootは未接続であること。UI/描画接続までを待つ。
+        public ValueTask SetInitialRootAsync(Widget root, CancellationToken cancellationToken = default);
+    }
+    public interface IUiApplicationContent
+    {
+        // ownerで開始。slotは空。成功時には初期viewを一つ公開済みであること。
+        ValueTask InitializeAsync(UiApplicationWindowContext context, CancellationToken cancellationToken);
+        // owner Idleで一turn一回。未完了Taskを同期waitしない。
+        void Advance();
+        // 新規work/transitionを止める。IsStoppedはscope解放を意味しない。
+        void BeginStop();
+        bool IsStopped { get; }
+        // slotのGPU drain後、surfaceを保持したままUI依存cleanupを行う。再試行可能。
+        ValueTask RetireAsync();
+        // slot解放後に一回だけ。所有scopeのDisposeAsync等を行う。再呼出ししない。
+        ValueTask ReleaseAsync();
+    }
+}
+namespace Lumyte.UI.Application.Desktop
+{
+    public sealed record UiDesktopApplicationOptions
+    {
+        public string Title { get; init; } = "Lumyte";
+        public Vector2 ClientSize { get; init; } = new(800, 600);
+    }
+    public static class UiDesktopApplicationExtensions
+    {
+        public static UiApplicationBuilder UseDesktop(this UiApplicationBuilder builder,
+            UiDesktopApplicationOptions? options = null);
+    }
+}
+namespace Lumyte.UI.Application.Browser
+{
+    public sealed record UiBrowserApplicationOptions
+    {
+        // 空を拒否。既存canvasを借用し、host終了時にDOM要素自体を削除しない。
+        public string CanvasId { get; init; } = "lumyte";
+    }
+    public static class UiBrowserApplicationExtensions
+    {
+        public static UiApplicationBuilder UseBrowser(this UiApplicationBuilder builder,
+            UiBrowserApplicationOptions? options = null);
+    }
+}
+namespace Lumyte.UI.Application.Headless
+{
+    public sealed record UiHeadlessApplicationOptions
+    {
+        public Vector2 ClientSize { get; init; } = new(800, 600);
+        public Vector2 Scale { get; init; } = Vector2.One;
+    }
+    public static class UiHeadlessApplicationExtensions
+    {
+        public static UiApplicationBuilder UseHeadless(this UiApplicationBuilder builder,
+            UiHeadlessApplicationOptions? options = null);
+    }
+}
```

builderは構成threadで使用し、driver、content（UseRoot/UseContent/UseWindowsのいずれか）、manager factory、text providerの重複指定をArgumentExceptionで拒否する。driverと内容は必須で、未指定のBuildをInvalidOperationExceptionとする。寸法・scaleはfiniteかつ正、件数上限は1以上、時間は正かつ有限、文字列は非nullで検証する。Build後の設定変更・二回目Build/RunはInvalidOperationException。サービスのValidateScopes/ValidateOnBuildを有効にし、Build時の検証ではサービスをresolveしてOS資源を作らない。利用者のconstructorもOS/GPUを生成せず、生成は起動factoryへ移す。

State/Failure/Completionと停止要求は任意threadから利用できる。window context/slot/managerの参照はownerの有効期間内で使い、別threadからの操作はInvalidOperationException、解放後はObjectDisposedExceptionとする。主な環境失敗codeはplatform-unavailable、graphics-initialize-failed、content-initialize-failed、presentation-failed、text-input-unavailable、graphics-recovery-failed、cleanup-failedとし、既存UI/Window操作の詳細codeはInnerExceptionへ保持する。

UseContentはfactoryが新規生成したcontentを受け取る。DIが所有するdisposable serviceをresolveして返すことは認めず、hostとproviderで二重解放しない。contentはIServiceScopeFactory等のroot serviceを借用できる。factory成功時に所有権を移し、戻る前に失敗したfactoryは自身の部分資源を回収する。UseRootは内部contentがrootを生成してSetInitialRootAsyncを呼ぶ構成へ展開する。Root factory失敗・初期slot準備失敗では部分資源を回収し、rootWindowを表示しない。

UseWindowManagerを省略したDesktopはWindowSystem adapterとHostedのfallback、Browser/HeadlessはHostedを使用する。factoryへ渡す既定manager/sourceは借用で、返したmanagerだけで通常画面からPopup/候補までを管理する。返却managerと既定managerが同一なら一回だけ停止・Disposeし、wrapperなら返却側を先に停止してから借用元の既定managerを停止する。hostは参照同一性で重複を除く。factoryが返す他のmanagerもhostへ所有権を移し、外部から並行利用・破棄しない。root登録はhostが一回行い、UiWindowId/SourceId/InputWindowIdを明示対応させる。

### 起動とowner thread

RunAsyncがdriverを起動してownerを確立し、その上でPlatform、非表示primary Window、graphics factory/service、presentation source、manager、contentを順に準備する。Resources利用時は同じownerでExecutionをPrepare/Startし、pumpを続けながら初期resource準備を観測する。content.InitializeAsyncの成功とslotのlayout・描画準備を確認してから、driverがborrowed primary WindowのShowを要求する。UI-0014の登録rootへmanager.Showを呼ばず、非表示WindowでのPresent成功をShowの前提にしない。初回の提示済み幾何が成立するまでは入力を開始しない。

Executionを使用する場合はhostが唯一のPrepare/Start/Stop担当となる。BrowserはOwnerPlacement.Host、CpuExecutionMode.Cooperative、同時数1を使用し、Desktopは固定Hostを維持してCPU workflowを既存ThreadPool経路へ送れる。UIのDispatch/Layout/RecordをCPU workflowへ移さない。AddResourcesが既に登録した同一Runtimeを再利用し、未起動の別Runtimeを暗黙生成しない。初期統合はworkflowだけを持つ標準ResourceRuntimeに限定し、未対応IExecutionModuleの追加はPrepare前に拒否する。任意moduleのHost phaseを暗黙RunHostPhaseで駆動する契約は追加しない。contentの初期化完了時にslotが空ならinvalid-contentとして起動を失敗させる。

Windows driverは専用threadをSTAで開始し、COM/Platform/TSFとUIをそのthreadで生成・使用・終了する。Linuxも固定owner threadで実行する。BrowserのRunAsyncはcanvas/moduleへアクセスできる現在のJS threadから呼び、Task.Runや専用threadへ移さない。Headlessは専用ownerでCPUのlayout/Recordとframe barrierを進め、GPUやOS入力を持つというcapabilityを公開しない。Headless driverは合成入力sourceと仮想提示rootを明示所有し、接続ごとに新しいInputWindowIdを発行してRootSourceへ登録する。Automation/Popupもこの合成originで通常routerを通し、null originをactive Windowで補完しない。これはOS取得・物理IMEの試験とは区別する。

hostはowner queueとSynchronizationContextを用意する。host/contentの通常await後の継続はownerへ戻し、frame内に未完了Taskを保持してpumpを続ける。ConfigureAwait(false)等で離れた処理からUIへ戻る場合はDispatcherを明示して使う。IUiDispatcherはownerからの呼出しも次のIdleへqueueし、再入実行しない。取消は実行開始前ならdelegateを実行せず、開始後は結果を観測する。async voidやTaskを返すdelegateを`Action`/`Func<T>`へ渡す使い方は契約外とする。UI callback内でInvokeAsync/StopAsync等を同期waitしない。

起動前のdispatcher入力は拒否し、停止開始後は新しい外部workを拒否する。hostはcontent.InitializeAsync/RetireAsync等の追跡する呼出し全体に、app・owner・呼出し世代へ結び付く内部admissionをAsyncLocalで設定する。その範囲で同Dispatcherへ戻るInvokeAsyncは停止中もcleanup queueへ入れ、ConfigureAwait(false)後のUI依存清掃を可能にする。別appには使えず、呼出しのTask完了で失効する。未awaitの子仕事やasync voidへ寿命を持ち出すことは認めない。将来のnavigation取消/退役もhostが追跡するchangeの同じadmissionを使い、通常の外部workへ停止後の受付権を与えない。

受理済みIME/manager/Graphicsの継続も内部queueで処理する。外部queueはMaximumQueuedUiWorkで制限し、受付不能はui-queue-fullで変更前に失敗する。cleanupは受理するlifecycle/changeごとに予約した別枠を使い、通常workの満杯で停止処理を受理不能にしない。callback例外を再実行・rollbackせずFailureへ保持し、入力の清掃後に停止へ移る。

### フレーム・休止・入力・IME

標準hostは次の順序を一つのowner上で管理する。manager、Popup、content、DiagnosticsのAdvanceを利用者に重ねて呼ばせない。

1. Platformの入力取得、Window状態観測、wgpu等のbackend event処理を進める。INPUT-0007の取得列の順を保ち、pointer focus変更後は次の文字取得前に取得済みprefixを配送してconnection.SynchronizeFocusを完了する。Pumpを再入しない。
2. TextInput service.Advance、通常入力とTextInputKeyの一回配送、connection.Advance、Popup/candidate・managerのAdvanceを進める。全surfaceのIdleを守り、入力domainのAutomation leaseを適用する。
3. Execution使用時はPumpHostLifecycleとPumpJobsを各turnのbudget内で進める。既存Diagnostics.Pump/Advance、hostのUI queue、content.Advanceを処理し、navigation等の内容変更をIdleでcommitする。ここでfocusが変わった場合も、次の取得前に同期する。
4. 論理viewportからlayoutし、IME/candidate geometryを公開する。rootとHostedを同じ提示先へ順序通りRecordし、描画準備、Submit/Presentを進める。
5. 新しいUI frame barrierを通知し、完了済みsubmission/presentationと旧frame ownerを回収する。NextFrameAsyncは実frameの進行を示し、GPU完了や画面走査時刻を保証しない。

PixelSize=0、非表示、最小化、Closing/Closed、不正scaleでは新規描画・押下を止める。BrowserのvisibilityStateも別に観測し、canvas pixel寸法だけで可視性を判定しない。描画休止中も実行可能なturnでIME、入力取消、Execution lifecycle、Diagnostics期限、GPU完了、停止処理を進める。rAFは一つだけ予約し、休止中の保守turnを別に設ける。JSがfreeze/unloadされた後の非同期終了完了は保証しない。遅延callbackにはdriver世代を付け、終了後は旧世代の清掃だけに使う。

DPIはUI-0007の変換をそのまま使う。surface.SetViewportにはClientSizeを渡し、CanvasへVector2 ScaleとHosted原点・clipを一度だけ合成する。target/bindingのPixelScaleは1。入力は既に論理座標なので再除算しない。resizeは観測したPixelSizeと論理viewportを同じframeに適用し、in-flight画像の解放後にswapchainを明示再構成する。入力にはUI-0014の提示済みGeometryRevisionだけを公開する。

UseTextInputは文字layout/fontをUI-0012のIUiTextLayoutProviderとして明示注入する。Windows TSF/Browser textarea service、connection、Menu guard、候補controllerとの寿命接続はdriver/hostが用意する。factoryのproviderと、そのengine/fontはDIまたは呼出し側の所有物としてhost終了まで生かす。Widget側は`layoutProvider: new(context.TextLayoutProvider)`等のComposition引数で供給し、hostがpropertyを暗黙上書きしない。

初期viewと後続viewの準備時にTextBoxのLayoutProviderと編集capabilityを検査（DocumentはTextBox内部で所有し、利用者からの供給を要求しない）し、編集を要求する対象で不足があればinvalid-editor/text-input-unavailableとして準備を失敗させる。UseTextInput未指定では編集serviceを起動せず、editable TextBoxを黙って無効化しない。矩形/ButtonだけならUseTextInputは不要。明示ReadOnlyの表示・選択は対応providerがあれば許す。Linuxで日本語IMEやTSF相当を利用できるとは報告しない。候補表示はSystem既定、独自表示はUI-0015のcapabilityとpolicyに従う。

IME接続は全通常Windowと編集Popupについてhostが集約する。同一backendの次のsessionを開始する前に、非選択connectionのSuspendAsyncが完了したことを確認する。論理keyboard target/Menu scopeと物理keyboard rootの活性を両方使って選択し、元Windowにfocusが保存されていても同時にBeginしない。Popupのconnection・候補controller・Automation hostはBindLifetimeへ登録し、popup surfaceの破棄より前に終了する。

`UiApplicationOptions.ImeCandidates`は既存`UiImeCandidateOptions`を受け取り、Systemを既定とする。PreferCustomを選んでもbackendの能力を拡張したことにはならず、UI-0015のsystem fallbackを維持する。

### アニメーション接続の追加範囲

実装PR 16の[UI-0020](UI-0020-animation-and-frame-lifetime.md)で、上記手順3のqueue/content処理後、手順4のlayout前にanimation phaseを追加する。標準hostが単調時計をturnごとに固定し、全surfaceのAdvanceAnimationsを呼ぶ。optionsのAnimationClockによる借用時計とTimeProviderからの既定時計、継続/一回frame要求、非表示・graphics復旧中の時計休止を同ADRで定義する。通常ページへtimerやframe更新を要求しない。Signalの自動配送停止とanimation休止をhostが両方管理し、cleanup/入力/IMEの進行は維持する。

### 表示とページの視覚遷移

実装PR 17の[UI-0021](UI-0021-connected-and-presence-transitions.md)で、motion options、要素のenter/exit、page/connected演出を追加する。標準frame coordinatorは既存Canvas/親RenderGraphの隔離captureとgroup合成、入力gate、予算、最終提示、GPU退役を所有する。UI本体とのcapture内部bridgeは既知の標準hostへのfriend accessに限定し、UIへRenderGraph依存を追加しない。通常値producerの休止とは別に、短いsnapshot演出は非表示/世代変更時に最終状態へ打ち切る。低水準hostとHeadlessのfallbackはUI-0021に従う。

### 描画とdevice世代

初期描画はUI-0004の全体再描画とする。提示先ごとに不透明背景を一回Clearし、root、Hosted surface、候補を描く。前frameのUIへLoadを重ねず、damage領域、保持buffer、部分Presentは導入しない。UiSurface.Recordを既存Canvas/Drawing2Dへ渡し、独自描画リストを追加しない。層が必要な場合も既存RenderGraphへAppendし、内側の独立graphを作らない。

標準rendererは対応するRgba8Srgb中間targetを用い、surfaceのcapabilityに合わせた最終present passを所有する。Rgba8Srgbが使えずRgba16Floatを使用する場合も、内容はdisplay-linear premultipliedとする。初期のprimary/Native提示はOpaqueとし、背景alpha=1を要求する。中間画像をsampleして得るlinear RGBをUnorm提示先へ一回sRGB encodeし、blendを無効にして出力する。sRGB提示先ではattachmentのencodeを使い、二重encodeしない。BGRAを手動でswizzleせず、texture copyを色変換の代用にしない。

Browserのcanvas textureはその描画turnで失効するため、Renderer2Dの非同期準備を中間targetに対して先に完了し、その後でswapchain imageを取得する。取得からcommand記録・Submit・Presentまでに非同期準備を挟まない。画像取得はCopySource対応の提示imageを優先し、非対応時は保持したRGBA8 sRGB中間targetを使う。提示imageのcopyも同じcommandに記録し、完了待機とmapは送信後に行う。BGRAの読み戻しはPNG用RGBAへ並べ替える（提示shaderでは並べ替えない）。

present shaderは既存Slang/offline artifactのビルド経路へ追加し、Browser/Native AOTでruntime shader compilerを要求しない。中間target、shader、pipeline、bindings、command、submission、surface frameはhostのframe coordinatorが所有する。最大in-flight数に達したら新しいGPU frameだけを延期し、UI/cleanup queueを停止しない。

UI/Window/TSFの固定ownerとGraphicsの実行制約は区別する。同じrendererへの同時Prepare、同じqueue/swapchainへの競合操作をhostが防ぎ、GPUが使用する資源は完了まで保持する。既存Graphics/Resourcesが許す別資源のbuild並行まで禁止するglobal gateは追加しない。Renderer.PrepareAsync内部のConfigureAwait(false)等で継続が別threadへ移ってもUIへ触れず、hostは完了をowner queueへ戻して世代・取消・targetを検査してからframeへcommitする。device loss/終了では関係するresource workflowと準備を静止させてから旧epochを退役させる。

AcquireのTimeoutは後続turnへ延期、Suboptimalは利用可能なframeを扱った後に再構成、Outdatedは画像解放後に再構成、Lostはsurface再作成、DeviceLostはdevice復旧へ分ける。Present結果も同じ分類で扱い、Submit済み仕事を取消で未送信扱いにしない。IGraphicsSubmission.WaitAsyncとIGraphicsSurfaceFrame.WaitForReleaseAsyncの両方を確認してから解放する。片方の待機取消を完了とみなさず、旧ownerを退役queueへ保持する。

device serviceはアプリSingletonで登録する。device loss時は新規Prepare/Submitを止め、入力をCancelし、旧epochのnative使用終了を確認してから旧frame/資源/leaseを返す。Graphics.HostingのRetire/Recover、必要なResources.RebuildRuntimeAsync、新Caps確認、surface/target/bindings/renderer再生成の後だけ再開する。CPUツリーとページ状態は維持するが、旧GPU viewを含むcustom内容は新世代へ更新するまで描画しない。

WgpuDevice.ProcessEvents等は描画休止・復旧中も進め、loss通知を止めない。Browserは同じGPUCanvasContextへ二つのsurfaceを接続できないため、旧swapchain/surfaceの退役・Disposeを先に完了してから新deviceへ接続する。旧資源のDisposeは生成時のdevice/dispatcherを使い、最新deviceを再解決しない。復旧が失敗した場合はgraphics-recovery-failedとして停止へ移り、旧資源へfallbackしない。

### DI・描画scope・所有権

MS DIのscopeは階層を作らない。アプリ、window context、将来のページは意味上の所有関係を持つが、CreateScopeで親scopeのscoped serviceを継承すると仮定しない。Applicationのroot provider、必要なら単一ResourceManager/ExecutionRuntime、IGraphicsDeviceServiceはアプリ寿命とし、ページscopeでdevice/rendererを複製しない。既存AddGraphicsDeviceはScopedが既定なので、標準登録はSingletonを明示する。利用者が同じ既定deviceを重複登録した場合は構成エラーにする。

IUiDispatcherはapp.Dispatcherと同じinstanceをrootのSingletonとして登録し、pageからconstructor injectionで借用できる。Build時にはまだownerへ接続しておらず、受付開始はRunのowner確立後となる。window/page固有のcontextやnavigation stateをroot Singletonへ保存しない。

既存UseGraphics2Dのscoped登録はhost専用のprivate render provider/scopeへ隔離する。そこに標準command rendererとDrawingCommandRenderers2Dだけを登録し、app所有のdevice serviceやResourceManagerを必要時に渡す場合は借用のImplementationInstanceとして登録する。factory登録で同じdisposableを返してprivate providerにも所有させない。private providerのDisposeはapp側のdevice/resourcesを解放しない。ページ向けServicesへUseGraphics2Dを暗黙登録せず、private scopeは全prepared frameの退役後に閉じる。

ResourceManagerが未登録なら既存Renderer2Dの組み込み資源を使用する。登録した場合は既存の単一manager、Graphics resource runtime、ExecutionRuntimeを利用し、hostが起動・終了を調整する。標準hostが起動するExecutionへ接続するmanager/deviceは、標準のDI factory登録でアプリが所有する構成に限る。外部で稼働中のmanager/runtimeを借用する構成は初期範囲に含めない。ResourceManagerのcleanupはExecutionへ仕事を送るため、Executionを先に停止しない。

ResourceManagerとGraphicsDeviceServiceに限り、既存の「失敗時の再試行、成功後の冪等Dispose」契約を使ってproviderより先に明示終了する。成功後のprovider.DisposeAsyncが同じserviceを呼んでもno-opとなる。明示終了が失敗した間はprovider破棄へ進まない。この扱いを任意のユーザーserviceやpage scopeへ一般化せず、MS DI scope/provider自身のDisposeAsyncは一回だけにする。

slotは現在viewのUiSurface、presentation binding、input adapter/connection、Automation host、Diagnostics登録を所有する。Widget自体はDispose契約を持たないので、rootを取り外すことと外部資源の解放を分ける。GPU viewを借用する内容は、そのviewを生成したscope/ownerをframe退役まで保持する。slotは表示先のWindow/sourceを借用し、元Windowを直接Disposeしない。

### 内容の停止・退役・解放

content.InitializeAsyncはownerで開始し、await中もhostはpumpと内部queueを進める。初期化が途中で失敗しても、生成済みcontentへBeginStopを一回通知して同じ終了経路を通す。Advance/IsStoppedによる静止判定は初期化未完了の観測も含む。遅い成功結果を新しいviewへ適用せず、当初の起動世代の清掃へ回す。

停止は以下の段階を分ける。これをcontentとslotが互いの解放を待つ循環にしない。

1. content.BeginStopで新規work/transitionを閉じ、slotも新規操作を止める。Diagnostics/Automationを取消し、IME RequestEnd、候補復帰、Popupの子→親終了と入力系列のCancelを進める。content.IsStoppedは新規transitionがなく受理済みworkが静止した意味とし、slotのGPU解放やReleaseAsyncを待つ条件にしない。
2. 操作の静止とcontent.IsStoppedを確認し、slotの新規Record/Prepareを止める。未完了submission/presentationをdrainする。この時点ではUiSurface、停止済みconnection、binding、window contextを保持する。入力/診断の新規受付はなく、GPUは旧内容を参照しない。
3. content.RetireAsyncをownerで実行する。surface/windowを使う購読解除等のthread依存cleanupはここで終える。失敗時は所有状態とsurfaceを保持してStopFailedへ移し、RetryStopAsyncでこの段階から再開する。完了済みcleanupを二重実行しない責任はcontent側にもある。
4. 成功後にinput/IME/Diagnostics登録の最終解除、binding.DisposeAsync、surface.Disposeを完了し、slotを空にする。bindingのGPU drainを省略せず、Window/sourceは引き続き保持する。
5. content.ReleaseAsyncを一回だけ実行し、内容が所有するDI scope等を解放する。thread依存処理は既にRetireで終えてあり、DIによるasync disposal継続をUI threadへ依存させない。失敗しても同じscopeのDisposeAsyncを再呼出ししない。
6. 診断scopeを終了し、root登録解除・manager停止、提示sourceのswapchain/surface・private render scopeの解放を進める。Window依存の保持tokenを返して生成Windowとprimary Windowの終了を確認し、Resources/device、Execution、Platform/dispatcher、app providerを依存順に解放する。借用Platform操作を必要とするdevice cleanupまでowner/Platformを維持し、cleanupに必要なExecutionも停止しない。

UI-0014のCloseはbinding drain後に実Windowを閉じる。primaryは借用rootなので、root登録のIsReleasedを待ってdriverがWindowを閉じる。driverは初期Window生成時からRetainNativeTargetの保持を持ち、IMEと当該Windowの提示資源の解放まで返さない。OSの取消可能なClosingではCancelと内部停止flagの記録だけを行い、通知外のturnでRequestStopへ進める。自らCloseする時は子windowと保持tokenの解放後とし、強制的なOS喪失は通常終了と区別して失敗処理へ渡す。Windowのretentionが残る場合はClosedまでpumpを続ける。Shutdownのために先にPlatformやJS callback loopを止めない。

driverはPlatform.IsQuitRequestedも停止要求へ接続し、予期しないprimary Closed、Browser canvas削除・JS実行context喪失はprimary-target-lostとして停止を開始する。WebGPU device lossは上記復旧経路で扱う。Popup/候補だけのCloseではアプリを終了しない。これにより通常のWindowの閉じる操作がRunAsyncの完了へつながり、Closedを描画休止として永久に待たない。

通常終了はStopped、起動/実行失敗がありcleanupは完了した場合はFailedとなりCompletionへ元の失敗を返す。retryableなretire/Graphics/manager cleanup失敗はStopFailedでowner loopと必要資源を保持する。RunAsync/当該StopAsyncはその失敗を呼出し側へ返すが、Completionはterminal状態まで未完了とする。RetryStopAsyncの成功で残りの段階を進める。StopFailedから通常実行へ戻らない。

ReleaseAsyncまたはMS DI scope/providerのDisposeAsync失敗は再試行不能とし、残るhost所有の清掃を可能な範囲で進めた後にFailedへ固定する。providerの内部cursorを巻き戻して再Disposeしない。Failureに未完了解放を含む例外を保持し、完了したと虚偽報告しない。RequestStopは冪等、StopAsyncは進行中の一回を共有し、RetryStopAsyncを明示した場合だけ失敗段階を再試行する。待機の取消は清掃の取消と分ける。

### AutomationとDiagnostics

slotはUI-0009の標準Automation hostを用意し、UI queueとframe barrierを同じloopへ接続する。UI-0011/0012/0015のrouting/text/candidate等の任意capabilityも維持し、物理入力domainの排他を共有する。Headlessは画像や物理IMEを提供できると偽らず、未対応操作をUnsupportedで返す。

```diff
+namespace Lumyte.UI.Application.Diagnostics
+{
+    public sealed record UiApplicationDiagnosticsOptions
+    {
+        public string TargetName { get; init; } = "main";
+        // UI-0010/0012のAllowInput/Screenshots/TextInput/TextInspectionは全て既定false。
+        public UiAutomationTargetOptions Target { get; init; } = new();
+        public UiAutomationDiagnosticsOptions Runtime { get; init; } = new();
+    }
+    // 標準hostがowner threadからpumpする既存Diagnosticsのexecution point marker。
+    public sealed class UiApplicationDiagnosticsPoint { }
+    public static class UiApplicationDiagnosticsExtensions
+    {
+        public static UiApplicationBuilder UseDiagnostics(this UiApplicationBuilder builder,
+            UiApplicationDiagnosticsOptions? options = null);
+    }
+}
+namespace Lumyte.Diagnostics.UI.Automation
+{
+    public sealed class UiAutomationDiagnosticsRuntime
+    {
+        // UI-0010へ本PRで加える契約。初回はownerかつIsStoppedを要求する。
+        // 成功したDispose後の再呼出しだけは、任意threadで何もせず戻る。
+        public void Dispose();
+    }
+}
```

既定はDiagnostics無効で、Job、画像取得、周期snapshot、transport接続を開始しない。UseDiagnosticsは既存point/contributor/runtimeを登録し、slotのsurface公開後に明示した名前と許可でtargetを登録する。Agent/transport/認証の設定は既存Services登録を使い、新socketや診断serverを暗黙起動しない。Observe/OverrideInput、親子許可AND、control group/Sequence、候補redaction、送信後の再試行禁止はUI-0010〜0015を維持する。view変更時は旧登録のIsReleasedを待ち、新surfaceへ新TargetIdを発行する。

point/runtimeはScopedなので、hostがowner上でページとは独立した一つの診断scopeを作り、全viewで共有する。root providerからScoped serviceを直接resolveせず、ページ遷移でruntime/control groupを作り直さない。停止時は全targetの受付停止・Job/入力leaseのdrainとruntime.BeginStop/IsStoppedを確認し、owner上でruntime.Disposeを先行する。その後に診断scope.DisposeAsyncを一回だけ進め、Agent等の非同期終了も待つ。scope途中のawaitでownerを離れた後のruntime再Disposeは、上記の成功後no-opだけを使う。scope自体のDispose失敗はterminalであり、再呼出ししない。runtime停止後に届く切断通知は新しい操作を始めず、終了世代の通知として処理する。

### 静的rootの利用例

```csharp
using Lumyte.UI.Application;
using Lumyte.UI.Application.Desktop;
using Lumyte.Graphics.Drawing2D;
using static Lumyte.UI.Compose;

var builder = UiApplication.CreateBuilder(args);
builder.UseDesktop(new() { Title = "Lumyte UI" });
builder.UseRoot(context => Panel()[
    Button(width: 160, height: 48, onClick: () => Console.WriteLine("clicked"))[
        Button.Background()[Rectangle(fill: new Color2D(0.2f, 0.4f, 0.8f, 1))]
    ]
]);
await using var app = builder.Build();
await app.RunAsync();
```

BrowserではUseDesktopをUseBrowserへ置き換え、公開済みcanvasのIDを指定してJS threadからRunAsyncを呼ぶ。静的root例にpage/navigation登録、Graphics device、GPU loopは不要である。TextBoxを使う場合はUseTextInputでfont設定済みproviderを渡し、root factoryでcontext.TextLayoutProviderをCompositionのlayoutProvider引数へ渡す。provider/engineの登録は既存UI-0012・GRAPHICSの契約に従い、フォントを暗黙探索しない。

例えば配布するfontのbytesを用意した上で、Build前に次を登録する。factoryはRun中にowner上で呼ばれ、engineはapp providerが所有する。fontの読込元・配布権はアプリが選ぶ。

```csharp
builder.Services.AddSingleton<ParleyTextEngine2D>(_ =>
{
    var engine = new ParleyTextEngine2D();
    try { engine.AddFont(fontBytes); return engine; }
    catch { engine.Dispose(); throw; }
});
builder.Services.AddSingleton<IUiTextLayoutProvider, ParleyUiTextLayoutProvider>();
builder.UseTextInput(services => services.GetRequiredService<IUiTextLayoutProvider>());
```

この例はMicrosoft.Extensions.DependencyInjection、Lumyte.Graphics.Text.Parley、Lumyte.UI.TextEditingのusingを追加する。fontBytesはアプリが用意したbyte[]であり、Parleyの既定設定は最初に登録したfontを使う。UseRootのTextBoxにはstringとonTextChangedを渡し、DocumentはTextBoxが内部で所有する。interface型のproviderをCompositionのOptional引数へ渡す場合は`layoutProvider: new(context.TextLayoutProvider)`と明示する（C#はinterfaceからOptionalへの利用者定義変換を適用しない）。

### 複数Windowの宣言API

2026-10-29追記。独立Windowは通常のWidgetではなく、Applicationが照合する表示先の宣言とする。Popupはページ内のportal宣言、独立Windowはアプリの宣言一覧という境界を維持する。以下の宣言APIと照合・表示処理を本PRで実装する。UseRoot/UseContentの簡易経路は一つのprimary宣言へ展開し、UseWindowsとの併用はBuildで拒否する。

```csharp
namespace Lumyte.UI.Application;

public enum WindowPresentation { Automatic, Hosted, NativeRequired }
public enum UiWindowCloseReason { User, OwnerClosing }
public sealed record UiWindowCloseRequest(string Key, UiWindowCloseReason Reason);
public sealed record UiApplicationWindowResult(string Key, Guid InstanceId,
    string Code, Exception? Error = null, UiWindowPresentation? Presentation = null);
public sealed class UiApplicationContext
{
    public IUiDispatcher Dispatcher { get; }
    public void RequestStop();
    // 構築関数を再実行する予約。callbackから呼べ、同一turnでは集約する。
    public void RequestRebuildWindows();
}
public sealed class UiWindowDeclaration { /* factoryで生成する不変の値 */ }
public static class Compose
{
    // page統合前はrootまたはcontentのどちらか一方を指定する。
    public static UiWindowDeclaration Window(string key, string title,
        Func<UiApplicationWindowContext, Lumyte.UI.Compose.Definitions.Widget>? root = null,
        Func<IServiceProvider, IUiApplicationContent>? content = null,
        bool isOpen = true, string? owner = null,
        Vector2? initialClientSize = null,
        WindowPresentation presentation = WindowPresentation.Automatic,
        Action<UiWindowCloseRequest>? onCloseRequested = null,
        Action<UiApplicationWindowResult>? onOpened = null,
        Action<UiApplicationWindowResult>? onClosed = null);
}
// UiApplicationBuilderへ追加。primaryKeyは一覧内の必須Window。
public UiApplicationBuilder UseWindows(
    Func<UiApplicationContext, IReadOnlyList<UiWindowDeclaration>> build,
    string primaryKey = "main");
```

```csharp
using static Lumyte.UI.Application.Compose;

builder.UseWindows(app => [
    Window(key: "main", title: "エディター", root: BuildEditor,
        onCloseRequested: _ => app.RequestStop()),
    Window(key: "inspector", title: "プロパティ", owner: "main",
        root: BuildInspector, isOpen: showInspector,
        onCloseRequested: _ => { showInspector = false; app.RequestRebuildWindows(); })
]);
```

buildはRunでownerが確立した後、必要な更新時に同期実行し、結果をhostがコピーする。通常の配列/List/collection expressionを受け取れ、利用者に専用コレクションの実装を要求しない。これはWidgetのContentCollectionとは異なる非Widgetの宣言一覧である。構築中はOS操作・DI content生成・GPU生成を行わない。keyは非空Ordinalでアプリ一覧内一意、primaryKeyは常に存在してisOpen=true、primaryはownerなし・Automaticとする。追加Windowのownerは省略可能で、指定時は同じ一覧の生存keyを参照し、循環/自己参照を拒否する。root/contentはちょうど一つを指定する。検証失敗は新一覧を公開せず旧Window群を保つ。

同じkeyと内容種別（root/contentの区分）の継続宣言は既存instance、UiWindowId、view slot、navigatorとDI activationを保持する。titleとcallbackを最新宣言へ更新する。root/content factoryのdelegate参照変更だけで内容を作り直さない。ページ内の更新はそのviewの再構築、navigation変更はnavigator経由とする。内容種別、owner、presentationの変更は旧instanceを完全終了してから新instanceを準備する置換となり、入力/focus/historyは引き継がない。initialClientSizeは初期値（既定800×600論理pixel）で、継続宣言によって利用者のresizeを巻き戻さない。独立Windowの種類は初期Normalとし、Tool固有のOS装飾や常時最前面指定は別の拡張とする。

false/一覧からの除去は閉鎖、再追加は新instanceとする。閉鎖中にtrueへ戻っても同じinstanceを再使用せず、旧清掃後に最新宣言から作る。keyとinstance IDは区別し、以前のTargetId/入力系列を新instanceへ移さない。宣言検証と実資源の準備は分け、追加の失敗ではその候補だけを清掃してonClosedへ失敗codeを通知し、既存Windowを維持する。primaryの初期化/提示失敗は既存のアプリ起動失敗とする。失敗した同じtrue宣言は毎frame再試行せず、false→trueまたは別keyで再試行する。一般の一覧をOS操作まで含むall-or-nothing transactionとはしない。

OSの取消可能な閉じる操作はonCloseRequestedを一要求一回通知し、モデルを変更しない。応答がない場合はWindowを保持する。primaryではアプリがRequestStopを呼び、追加WindowではisOpenをfalseへ更新する。宣言除去/停止は拒否可能なclose要求ではなく寿命終了なのでcallback応答を待たない。owner終了では子の新規操作を止め、OwnerClosingを一回通知して子から強制drainする。OSによる強制喪失は要求ではなく失敗終了として扱う。onOpened/onClosedは実際のsessionに一回通知する。

### 通常WindowのNative/Hosted管理と入力

primaryの物理sourceは選択driverが従来どおり生成し、managerへ借用登録する。追加のAutomaticは通常Native Windowを生成できるdriverならNative、できなければowner（省略時はprimary）の物理提示root内のHostedを選ぶ。Hostedはそのroot内、NativeRequiredは生成不能をnative-unavailableとして通知する。Browserでwindow.openを自動実行せず、同canvasのHostedへ解決する。Headlessでは仮想root内のHostedとし、OS Windowの検証済みとは扱わない。初回表示後の自動方式変更は行わない。

PR #86のWindowSystem adapterはPreserveFocusのPopup向けであり、通常Native Windowの生成/活性化能力を保証しない。本実装ではDesktop driverに通常Windowの作成・title変更・Closing/Closed観測・固有InputWindowId・提示sourceの所有を追加し、独立sourceをIUiWindowManager.Registerへ登録する。WindowSystemを使う場合も通常Windowの実能力を個別に確認し、Popup capabilityを流用しない。Hostedは既存managerのNormal/Interactiveとowner（省略時primary）の提示rootへ接続する。NativeRequiredにHostedを返すことは禁止する。owner指定のOS所有関係が実現不能ならAutomaticはHosted、NativeRequiredは失敗とする。元Window/sourceの所有者はdriver、登録/binding/表示slotの所有者はApplicationとし、借用と生成を混同しない。

Hostedのタイトルバー・閉じる要求・移動・resize・前後順・activeな通常画面の選択はApplicationのHosted window hostが提供する。見た目はComposition内容として構築し、managerは確定位置/寸法/提示順/hit testを担当する。Hosted領域はrootへclampし、primaryのUIより前、Popup/IME候補より後へ合成する。タイトルバー領域の操作もWindow IDと取得originを保つ通常入力として配送し、本文UiSurfaceと二重配送しない。Nativeの通常WindowはOSのfocus/activation観測から対象を決め、Popupを開いた時のPreserveFocus規則を持ち込まない。

Hostedの操作に必要なmanager契約として、`UiWindowRequestId Resize(UiWindowId window, Vector2 clientSize)`と`UiWindowRequestId BringToFront(UiWindowId window)`をIUiWindowManagerへ追加する。これはPR #86の現行APIではなく、本PRの実装時に全first-party managerと独自managerの移行説明を同時に更新する変更である。Resizeは正の有限寸法を要求し、Hosted Normal/Toolの描画viewとgeometry revisionを更新し、入力は新幾何の提示後に切り替える。BringToFrontは同じ提示root/役割層内だけを並べ替え、Popup/候補層を越えず、OS Activateやkeyboard target変更を含まない。keyboard targetは既存SetKeyboardTargetで別途更新する。外部登録rootや未対応Nativeへの操作はunsupportedの要求結果とし、通常Nativeのresize/activateはdriverのOS経路へ委譲する。

各Windowに独立したcontext/view slot/focus/navigation履歴/IME connection/Diagnostics root targetを作り、root DI provider・Graphics device・dispatcherはアプリで共有する。DIのscoped serviceが親Windowから継承されるとは扱わない。Popup/候補は所属Windowの子として同じ入力domainへ接続する。物理入力の排他domainを共有する複数WindowはAutomation leaseも共有し、別Diagnostics rootだから同じdeviceへ同時合成入力できるとは扱わない。

Native入力は取得時InputWindowIdから登録sourceへ、Hosted入力はそのrootから提示済み幾何で論理UiWindowIdへ解決する。pointer系列とkeyの配送先固定、Device付き入力、キャンセル、未知の跨source座標変換は既存router契約を維持する。別Windowのactive状態からoriginを補完しない。通常Window切替では旧focus/IMEを静止させ、旧pointer Upやキー終端を新Windowへ誤配送しない。

終了手順は既存slot単位のBeginStop→入力/IME/Popup/診断停止→GPU drain→RetireAsync→binding/surface→ReleaseAsyncを各Windowへ適用する。追加Windowは子から終了し、そのWindowのnative target保持を返してからdriverが実Windowを閉じる。primaryと共有Graphics/Platform/providerは全追加Windowの清掃後に解放する。追加Windowの閉鎖でアプリを終了せず、primary喪失/RequestStopだけが全体停止となる。清掃失敗は資源を保持したStopFailedとして既存再試行へ接続する。

受け入れ条件に、一覧追加/除去/並べ替えとkey維持、title変更でページ状態維持、同key再追加の新ID、OS閉じる要求の採否、owner連鎖、追加生成失敗、Hosted titlebar/移動/resize、NativeとHostedの混在、Browser同canvas fallback、Windowを跨ぐ入力取消、最後まで残るGPU/診断lease、全体終了時の子→親順序を加える。通常Native WindowはWindows/Linuxの実環境検証を別に記録し、PR #86のPopup試験で代用しない。

### ページ内Dialogの自動管理

[UI-0022](UI-0022-declarative-hosted-dialogs.md)の宣言的Dialogを、標準hostがportal・modal入力scopeとして自動接続する。DialogをUseWindowsへ登録したり、アプリからShow/Advance/Disposeを呼んだりする必要はない。独立Windowの宣言とページ内のDialog宣言を別々に照合し、同じkeyでも異なる識別領域とする。

Window instanceごとにDialog scopeを保持し、通常入力・IME・子Popup/候補・Diagnosticsと一つのowner loopで処理する。Modalは所属Windowの論理client領域だけを遮蔽し、他WindowやBrowserのcanvas外を遮蔽しない。Hosted/Nativeのroleだけでmodal判定を代用せず、coordinatorが背景入力を先に遮断する。Hostedのlayer/backdrop/装飾は合成へ、Nativeは通常Windowのdriver経路でhidden生成/owner/activation/Closing/source/DPIへ接続する。モーダルowner抑止はtokenで入れ子管理し、Hosted ownerに無関係な同rootのWindowをOS無効化しない。

presentationのAutomaticはNativeのDialog能力を満たせば独立表示、未対応/表示前の安全に回収できた失敗ならHostedへfallbackする。NativeRequiredはHostedへ置き換えず失敗を通知する。Browserは同canvasのHosted。生成Nativeはmanagerへ借用登録し、Dialog hostが登録/binding、driverが実Window/sourceを所有する。低水準埋め込みも任意のnative adapterを接続でき、UI.Controls.WindowingからApplication/Platformへ逆依存しない。表示済みの自動移送は行わず、方針変更では旧表示とowner抑止を完全終了してから新sessionを準備する。

ページ遷移のquiesceとWindow終了ではDialogへの新規操作を停止し、入れ子Dialog/Popup/IME/診断参加者、視覚資源とGPUの退役を待ってから元page scopeを解放する。モデルがisOpen=trueのままでも寿命終了を妨げず、清掃中のcallbackを解放済みpageへ配送しない。Dialog終了はアプリ終了を意味しない。詳細なAPI/失敗code/受け入れ条件はUI-0022を正本とし、C8導入時に標準hostと同時検証する。

## 検討した代替案

- サンプルloopのコピーを必須にすると、各アプリで入力順序、GPU寿命、IME停止を再実装するため、標準hostへ集約する。
- Graphics.HostingやExecutionRuntimeにUI loopを追加すると、device/仕事実行とPlatform/UIの責務が混ざるため、Applicationが既存契約を組み合わせる。
- IHostedServiceだけで起動すると固定STA/JS ownerとframe pumpが決まらないため、MS DIは利用しつつdriverがloopを所有する。
- page scopeへrenderer/deviceを登録するとnavigationでGPU資源の複製・早期破棄が起こるため、描画scopeを分離する。
- 終了を単一DisposeAsyncへ集約すると、UI依存cleanupとDI scope解放の失敗を区別できないため、停止・退役・一回の解放を分ける。

- Windowを通常Widgetの子として扱うとOS表示先とページlayoutの寿命が混ざるため、Applicationが照合する別の宣言一覧とする。
- 閉じる操作でライブラリがモデルを暗黙変更すると状態の正本が二つになるため、要求callbackと実際の終了通知を分ける。

## 結果と影響

利用者はCompositionの内容とcallbackを登録してUIアプリを起動できる。既存の入力、Window manager、IME、Graphics世代と診断の契約を、一つの標準loopで利用できる。

hostはOS thread、提示形式、休止、device復旧、部分起動失敗、cleanup再試行までを所有する。低水準埋め込みより依存が増えるためUI本体へ統合せず、driverとDiagnosticsを別packageにする。ページと登録方法は後続ADRへ分ける。

## 検証方針

- HeadlessでBuildの無副作用、driver/content重複、owner違反、dispatcher FIFO・取消・再入禁止、初期化失敗、停止中の遅い完了を検証する。
- content停止→GPU drain→Retire→surface解放→Releaseの順をfixtureで記録し、停止待ちの循環、Scopeの二重Dispose、失敗段階の過剰再試行がないことを確認する。
- WindowsでSTA生成/await継続/終了、Linux X11の通常入力、BrowserのrAF・visibility・canvas再接続を確認する。IME可否と実機未確認の範囲を分ける。
- 通常入力・Automation・Diagnosticsが同じ配送と排他を使い、pointer focus後の同pump文字、Popup/候補、旧TargetId、終了時CancelをUI既存試験へ接続する。
- DPI (1,1)/(1.5,1.5)/(2,1.25)、resize、0pixel、最小化、複数Hosted、非活性Native popupについて、提示幾何と入力位置、Clear一回を確認する。
- 実GPUでsRGB 0.5灰色が約128、不透明白へ半透明黒を合成した結果が約188となる基準画素を確認し、encodeの欠落・重複、BGRA取り違えを検出する。
- in-flight上限、Acquire/Present各status、Wait取消、device loss、旧Browser surface解除後の再作成、ResourceManager再構築をfixtureと利用可能な実GPUで分けて検証する。
- Native AOT/trimmingとBrowser publishを既存runnerへ追加し、Composition生成API、DI登録、offline提示shader、TSF COM/Browser moduleの起動と終了を確認する。既存Resources.Graphics.AotSmokeはSmokeDeviceを使うため、実提示・native loader試験の代用にしない。
- 静的rootだけのDesktop/Browser sampleにGPU loopがなく、UseDiagnostics未指定では診断workが開始されないことを確認する。

## 実装検証の記録

2026-10-30、Linux環境、.NET SDK 10.0.401で実施した。

| 検証 | 結果と確認範囲 |
| --- | --- |
| Release solution build | 警告0、エラー0。全driver、sample、試験projectを含む |
| managed試験 | 全体1,951件成功、75件skip。Applicationは23件で、停止の再試行、dispatcher取消・容量、Window照合、Hosted装飾、診断容量不足、共有IME sessionの切替、GPU待機境界を含む |
| Native AOT | Headless sampleのstrict publishと生成実行ファイルでのAutomationクリック・終了が成功 |
| Browser | trimming publish、Chromium/SwiftShader WebGPUで提示、Automation PNG、通常クリック、終了と借用canvasの保持が成功。提示imageからのPNGで灰色128±2、不透明白255を検査 |
| package | 5 packageのpackが成功。Browser package内の4 JS bridgeとbuildTransitive配布を確認 |

CIでBrowserのPNG取得が5秒の既定期限を超えたため、sampleの画像検証だけ30秒、CDP評価だけ45秒の有界待機にする。通常Automation APIの既定値は変更しない。ChromiumのCPU倍率6で3回連続取得し、同じGPU画素を検証した。`LUMYTE_BROWSER_CPU_THROTTLE`と`LUMYTE_BROWSER_CAPTURE_REPEATS`で再実行できる。

アプリ終了時は、dispatcherの通常受付停止から各hostのBeginStopまでの間にも、外部のinput lease返却が到着し得る。この間は同じownerによる強制返却の完了へ合流し、返却要求の拒否をクリック失敗として扱わない。終了Buttonを別threadのAutomationから操作する16回連続の回帰試験で確認した。

BrowserのソフトウェアGPU検証は実GPU・実IMEの検証と区別する。WindowsのSTA/native Window/TSF、Linuxの実Window、実device loss、Browser textareaでの実IME入力はこの環境では未検証であり、Headlessや偽backendの成功をその証明として扱わない。ページ、Signal、アニメーション、C8 Dialogは後続PRで検証する。

## 参考資料

- [Graphics.Hosting](../../../src/Graphics/Lumyte.Graphics.Hosting/README.md)
- [ExecutionRuntime](../../../src/Core/Lumyte.Execution/README.md)
- [Drawing2D DI](../../../src/Graphics/Lumyte.Graphics.Drawing2D.DependencyInjection/README.md)
- [UI-0007: Window入力・DPI](UI-0007-window-input-and-dpi-integration.md)
- [UI-0008: GPU資源寿命](UI-0008-render-graph-and-resource-lifetime.md)
- [UI-0012: IMEと編集文書](UI-0012-ime-and-text-editing.md)
- [UI-0014: 注入可能なWindow manager](UI-0014-injectable-window-manager.md)
- [UI-0015: IME候補表示](UI-0015-custom-ime-candidate-presentation.md)

## 全体レビューに基づく更新境界

Editor検証はrootの弱参照とStructureRevisionに対応してcacheし、接続後の子初期化も失効させる。Hosted chromeは一turn内のgeometry変更を集約し、完了した内部要求の結果を解放する。描画はMaximumFramesInFlightにより有界化する。submitゲートを保持するのは準備・Acquire・記録・Submit・Presentの間とし、GPU/presentation完了待ちとreadbackは外で行う。ownerのDrainはframeのreadbackと後処理完了も待つ。確認済みdevice退役だけが未完了waitを取消して資源解放を許可し、通常のwait取消をGPU完了とは扱わない。BrowserのAcquire→Presentを同じ描画turn内に保つ。

## Fallback提示の前後順

Fallbackの提示情報は内側のstacking orderを確定してから公開IDへ変換し、Hosted描画とhit testの前後順を一致させる。

## WidgetとElementの責務

UseRoot / IUiApplicationContentは受動的なWidget宣言を返し、各viewのUiSurfaceがElementを
生成・照合・破棄する。Window宣言の照合とは別の階層であり、一つのWidget宣言を別の
Surfaceへmountしても、入力・編集・幾何の状態は共有しない。
Hostedのtitle bar・close button・移動/resize部品も同じ基盤を使う。Widgetはchrome・title・
provider等の設定を持ち、Elementがcaption layoutと進行中のdragを保持する。
宣言の交換でdrag状態を別オブジェクトへコピーしない。

viewの編集能力検証は公開済みElementツリーを対象にし、SurfaceのHostValidationRevisionが進んだ時だけ
走査する。Widgetの構築中slotは初回mount前に検証する。更新のないframeで全体を再走査しない。
Elementの論理unmountと、最後の提示・GPU退役・DI scope解放は別々の完了点として扱う。

### Element分離後の再検証

ReleaseのApplication 27件とheadlessアプリの起動・通常Click・停止サンプルが成功した。
Window chromeも型付きElementで実装し、Widgetには実行時geometryや押下・drag状態を持たせない。

editor検証は共通のHostValidationElements indexを参照する。全Elementの再列挙は行わない。
TextBoxElementはIsReadOnlyとLayoutProviderの差分だけでサービス要件revisionを進める。
色の局所更新では検証を省略する一方、構造が同じでもeditable化・provider変更は再検証する。

## 公開済みSurfaceのサービス検証

初回mount前の宣言検証は維持し、標準compositorはSurfaceの更新後に
HostValidationRevisionと既存の対象集合からサービス要件を検証する。
検証callbackが追加したRebuildは適用して再検証し、100回で収束しない場合は失敗とする。
未完了frameは既存の例外時清掃で解放し、presentationの退役を妨げない。

### 提示用中間textureの再利用

UiHostPresenterは同一device世代の中間textureとviewを、寸法・formatが一致する場合に再利用する。
idle資源は最大2組、合計64 MiBのtexel容量まで保持し、古いidle資源から解放する。
この容量はRGBA8の4 bytes/texel、RGBA16Fの8 bytes/texelで算出し、native allocationのpaddingやin-flight資源は含めない。

返却はGPU submission完了、presentation解放、command buffer・argument table・prepared drawing等の従属資源の清掃がすべて完了した後に行う。
記録・wait・清掃が失敗したtargetは、再試行後に清掃できた場合も再利用しない。
device退役時は新しい貸出を停止してからpending frameを解除し、遅延して返る資源も解放する。
poolの貸出・返却・退役・破棄を同じlockで直列化し、非同期完了と破棄の競合による再登録を防ぐ。

成功済みsubmissionの最終texture stateを保持する。通常はSampled、intermediateをreadbackした場合はCopySourceから次のColorAttachmentへbarrierを記録する。
毎frameのGPU提出・提示確認は継続し、公開Surface.Recordの契約は変更しない。
このpoolは画面提示用の中間targetに限定する。

実Frameとfake texture/viewによる100回の完了・返却で、texture/viewの作成が各1回になること、wait失敗、清掃失敗、retirement競合、resize/format、容量上限とbarrierを検証する。
これは資源作成回数と寿命の検証であり、実GPU時間・画素・FPSの改善率は未測定である。
