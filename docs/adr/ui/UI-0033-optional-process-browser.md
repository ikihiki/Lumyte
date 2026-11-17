# ADR-UI-0033: UIを優先し、任意のブラウザを別プロセスで合成する

- 状態: 採用
- 日付: 2026-11-16
- 基準コミット: `7f78d63ea2d1b11107ec6fee24b269079d5705eb`

## 背景

Lumyteの画面は通常のUI部品で構築し、Webページはそれだけでは代替しにくい用途のフォールバックとして表示したい。ブラウザの起動失敗、native abort、ページの待ちが、主となるUIの起動・入力・更新を止めてはならない。ブラウザの一般的な互換性を追い続けるより、任意部品として限定した機能と局所的な回避策を採用する。

基準コードの `src/UI/Lumyte.UI.Media/ImageElement.cs` は借用画像を記録できるが、HTML実行環境は持たない。`src/UI/Lumyte.UI.Application/IUiApplicationParticipant.cs` はowner上の起動・停止を調整するためのinternal境界であり、ここで同期的にCEFを初期化すると、例外でないnative abortをUIから隔離できない。try/catchだけではこの境界は成立しない。

[UI-0004の描画](UI-0004-drawing-and-clipping.md)、[UI-0005の入力](UI-0005-pointer-dispatch-and-capture.md)、[UI-0008の資源寿命](UI-0008-render-graph-and-resource-lifetime.md)、[UI-0025の借用画像](UI-0025-borrowed-media-and-progress.md)を維持し、追加するブラウザだけを別の実行・失敗境界へ置く。Web実行環境でLumyte自身を動かすApplication.Browserとは別の機能である。

## 決定

WebViewは任意の表示宣言とし、永続Sessionを借用する。CEFは明示的に選択した別プロセスhelperだけで実行し、ソフトウェアOSRの画素を有界な共有メモリと更新通知で受けて通常のDrawing2D画像として合成する。UIプロセスはCEFやその.NETラッパーをロードしない。helperの欠落・依存不足・sandbox起動失敗・異常終了・timeoutはSessionのFaultedとして扱い、アプリは通常UIを継続する。

起動は利用時だけ行う。Build・参加者のStartAsyncではnative初期化を行わず、通常のsolution・packにCEFの取得・起動を要求しない。再試行は暗黙に行わない。Linuxのsession/portal対策が必要な場合はhelper専用の環境へ閉じ込め、全ユーザーのDISPLAYやDBus、既存portalサービスを変更しない。sandboxを利用できないことを理由に、自動でsandboxなしへ切り替えない。

### 利用者の変更前・変更後

基準コードではPreparedImageを表示できるが、ページを開くAPIはない。独自Elementから画像を描いても、ブラウザの起動・転送・終了は利用者が実装する必要がある。

追加後も通常UIを既定にする。ブラウザが必要な場合だけUseCefWebViewsを登録し、絶対パスでhelperを明示する。以下はimportsを含む構成例であり、実行には別途helperと対応runtimeが必要である。

```csharp
using Lumyte.UI.Application;
using Lumyte.UI.Application.Desktop;
using Lumyte.UI.Application.WebView.Cef;
using Lumyte.UI.WebView;
using static Lumyte.UI.Compose;
using static Lumyte.UI.WebView.Compose;

var builder = UiApplication.CreateBuilder(args).UseDesktop();
builder.UseCefWebViews(new()
{
    HelperExecutablePath = Environment.GetEnvironmentVariable("LUMYTE_CEF_HELPER")
});
builder.UseRoot(window =>
{
    var page = window.GetWebViewSession("help", new Uri("https://example.com/"));
    page.StateChanged += (_, _) =>
    {
        if (page.State == UiWebViewState.Faulted && window.View.CurrentSurface is { } surface)
            surface.Rebuild(Button(width: 800, height: 80, automationName: "通常UIへ戻る"));
    };
    return WebView(session: page, width: 800, height: 600, automationName: "ヘルプ");
});
await using var app = builder.Build();
await app.RunAsync();
```

helper未指定もローカルな利用不可として扱う。SessionはWindowの一意なインスタンスIDとkeyで保持し、同じ初期URLでの再取得・宣言の再構築では再起動も再遷移もしない。異なる初期URL、空key、別Windowへの接続や二重attachmentは公開前に拒否する。宣言の構成エラーと、任意backendの実行不能を混同しない。

### 関数コンポーネントからの取得

関数コンポーネントはUiWebViewHooks.UseWebView(key, initialUri)でWindow所有のSessionを借用する。WindowやSessionを親から子へ引数で転送しない。CEF登録がWindowごとの安定したsession providerを既存のUI contextへ自動提供し、生成された関数の評価時に最も近いproviderを参照する。独立したsurfaceにはProvideWebViewSessionsで明示的な借用providerを設定できる。関数外・provider未設定・閉じたWindowへのアクセスは明示的に拒否する。

同じWindow・key・初期URIは再構築・signal更新・component再mountでも同じSessionを使う。初期URIを後から変更することや、同じSessionを二つのWebViewへ同時接続することは拒否する。参照を取得することと表示をattachすることを分離し、componentのunmountはWindow所有Sessionのdisposeを意味しない。backendの同時1session制限も緩和しない。hookは関数評価内で呼び、イベントは取得した参照をcaptureする。通常のStateChanged購読を毎回の関数評価で追加しない。

Window contextは実root Widgetを変更・置換せずsurface環境として保持する。初期構築とstaged viewで共通の境界を使い、full rebuild、root関数自身のsignal再評価、子部分木の更新でも継承を維持する。局所providerの優先順位を保持し、候補viewの破棄が借用Sessionを退役させない。初期構築補助のthread-local Windowをhook解決に流用しない。

関数型sampleのBrowserSampleUi.csは通常のheader・counter Buttonと、clip・paddingを持つPanel内のWebViewを同じUI階層へ置く。helper内部のCPU画素検証と、実UIのtexture upload・合成・captureの確認は区別する。

### 宣言、命令、状態と対応能力

宣言は接続先と配置を保持する。読込・現在URL・エラーはSessionの読み取り専用snapshotで観測し、StateChangedをowner上で通知する。UIからの明示Cancelはhelperを停止し、アプリ全体の停止を要求しない。購読の寿命は利用者のWindow・モデル寿命に合わせる。

初期対応はHTTP/Sの初期文書の受動表示に限定する。InputAvailable、PopupAvailable、ImeAvailable、NavigationCommandsAvailableは対応能力を示す。このhelperではすべてfalseで、戻る・進む・reload・postcreation Navigate等を成功したnoopとして受理しない。明示操作はNotSupportedException、入力未接続はUnsupportedCapabilityとして表す。roleやfocus指定で未対応のブラウザ入力を有効化しない。

一般のSession命令APIは拡張用に維持するが、能力falseのbackendでは利用できない。URLの遷移要求と、ブラウザから観測したURLを同じ双方向の値として扱わない。load-endはHTTP成功・画素描画・提示完了を意味しない。

### helperの起動と失敗境界

Application接続はengine-freeの共通部だけを参照する。Rust helperは固定したcef-rsとCEFを使い、子プロセスのbootstrapをstdin readerやIPC writerの作成より先に処理する。UIとhelperの接続はこのbackend専用の小さい固定形式とし、汎用RPCや任意の.NETオブジェクト公開を追加しない。

helperパスは明示した絶対パスに限定し、引数はProcessStartInfo.ArgumentListで渡す。監督はCEFにもPythonにも依存しない独立したLinux native executableとし、pidfdとsubreaperで所有子孫を回収する。managed設定のSupervisorExecutablePathを明示でき、未指定ならhelperと同じディレクトリのlumyte-cef-supervisorを使う。未対応OSや監督実行物の不足ではBrowserだけを失敗させ、起動時のコンパイルやPython fallbackを行わない。通常UIに監督やCEFを要求しない。ユーザーの通常ブラウザprofileを取り込まない。親はhelper専用HOME・cache・tempを所有し、終了後に回収する。Chromiumが追加するsingleton socket名を含めLinuxのUnix-domain socket長制限に収まるよう、private rootはUTF-8で59byte以内にする。継承したtemp baseが長い場合は短い/tmp配下へ退避し、0700で原子的に作成する。継承元のディレクトリやホスト環境は変更・削除しない。Linuxの任意ランチャーも同じhelper寿命の範囲に置く。監督は標準入出力を子へ継承して自分の参照を閉じ、画素や命令を読み書き・中継しない。.NETとCEF helperが命令・状態・小さい更新通知を直接pipeで通信し、画素は専用の固定3slot共有メモリで渡す。

Browser backendはprivateなLumyte.Execution runtimeとinstance WorkflowPortでsession単位の非同期I/O寿命を所有する。packetごとのcommandや独自Task.Run loopを増やさず、UI側は既存のlatest mailboxとcoalesced Wakeで更新する。UI共有runtimeのmodule登録制約は変更しない。CloseWindow・BeginStopで先にsessionを取消し、pipe・writer・reader・実process終了と回収をjoinした後にbackend runtimeを停止する。module.Stopを最初の取消通知に使うと静止待ちとの循環になるため行わない。timeoutだけでClosed扱いにはしない。監督の停止・回収が期限内に終わらない場合は、UI ownerへ停止失敗を観測させ、StopFailedとして有限時間で返す。未回収のprocess・root・workflowを保持し、同じ監督が実際に終了した後の再試行で退役を確認する。runtime停止の例外も元の例外としてownerが観測し、成功・失敗の両方でWakeする。停止再試行のために別processや別workflowを起動しない。

起動期限は既定10秒、初期文書の期限は既定15秒とし、正の有限な上限付き値だけを受理する。UI ownerはpipeのread/write、native初期化、プロセス終了を同期的に待たない。startupの無応答、ロードの無応答、EOF、異常packet、queue上限、native processの終了は理由付きFaultedにし、未処理命令を再送しない。中止・失敗時は所有child treeの終了とreader/writerの回収を行う。

helperの終了とGPU資源の退役を分ける。Window停止の第一段階はhelperの受入停止・プロセス終了までとし、ここでsceneの破棄を待たない。既存ApplicationのGPU drain・Surface detach後に、内部RetireWindowResourcesAsyncで画素・upload・画像のleaseを退役する。新しい終了判定が、既存のdetach到達条件と循環しないようにする。

### IPC、レイアウトと資源寿命

packetの長さ・session・kind・generation・sequence・寸法を検証し、制御bodyは4096bytes以下、画素更新通知のbodyは空とする。旧形式の大きな画素bodyは確保前に拒否し、画素pipeへの暗黙fallbackは行わない。command queueとnative input/output queueを有界にし、共有slotとmanaged mailboxは最新画素を優先する。

共有メモリは親が0700 root内に0600で排他的作成し、version・session nonce・capacity・slot数を検証して起動する。3slotはFree／Writing／Ready／Readingをaligned atomicで所有し、producerは未読Readyを再利用できるがReadingを上書きしない。consumerはclaim後のmetadataを検証し、既存の所有画素mailboxへコピーしてからslotを解放する。frame sequenceと通知packet sequenceを分離し、古い通知を最新slotと同一視しない。mappingはsession中に拡張・truncateせず、実際のCPU budgetへmetadata・page padding込みで予約し、readerとnativeの実退役後に解放する。

更新通知だけがqueue満杯なら通知を捨てても共有Readyを維持する。consumerは初期Readyを含む全packet受信後に共有slotを調べ、control queueに阻まれた最後の静止画素を取りこぼさない。すべてのslotが読取中ならnative ownerを待機させずそのframeを捨てる。stdoutはprotocol専用、CEFやportalの診断はstderrへ分離し、UI側で無制限に保持しない。

WebViewは文書の固有寸法を親へ返さず、既定800×600論理単位へ親制約を適用する。viewport・DPIの変更はgenerationを進める。ゼロ面積・非表示・presence完了・Window休止ではUI側のuploadを休止し、helperの面を有界な1×1へ縮小する。ブラウザの通信・CPU描画やIPC画素の受信まで停止したとは保証しない。layout overrideが呼ばれない非表示subtreeも扱い、休止中に追加したElementも公開時に休止させる。

CEF callbackが借用するBGRA画素はcallback内で共有slotへ直接コピーし、managed側はclaimしたslotから所有mailboxへコピーする。共有メモリ化はこの2回のCPUコピーやGPU uploadをゼロにはしない。GPU転送はownerに直列化した既存graphics境界で行い、upload用bufferとcommandは送信完了まで保持する。画素revisionだけが変わる場合は描画を無効化し、layoutを再実行しない。

本体・popup用の既存uploaderは最大3画像とCPU/GPU予算を維持する。ただしpopup能力falseのhelperからはpopup面を公開しない。scene、snapshot、read lease、準備済みframeの画像参照を独立して保持し、unmountや提示終了だけをGPU解放条件にしない。曖昧なsubmit失敗はdevice退役まで保持し、失敗後の退役を再試行できる。

通常のclip・重ね順に従い、ブラウザを含む祖先のlive captureは不可とする。旧Imageの到達性を保守的に確認するためGCまでslot再利用を待つ場合がある。CEF paintにはgeneration tokenがなく、同寸法へ戻るresizeで画面内容の古さを完全には判定できない。これらを隠して滑らかな動画や完全な提示同期を保証しない。

### 安全性と未対応範囲

helperのsandboxは既定で有効にする。起動不能な環境はUIを継続してbackendだけを失敗させる。アプリがAllowUnsandboxed=trueを明示した場合に限り、当面の局所的な選択としてsandboxなしで実行できる。起動失敗からの自動切替は行わない。sandboxなしは信頼できるコンテンツ向けであり、別process・private HOME・read-only表示は悪意あるページからの防御境界ではない。HTTP/SのURL検査もネットワーク隔離やlocalhost制限ではない。native bootstrapが成立しないWindows、IME要求、永続profileは現在のhelperでは未対応として明示する。明示unsafeモードは通常の製品helperで実装・検証し、診断用一時コピーの結果を流用しない。unsafeモードの成功からsandbox付き配布の成立を推測しない。

popup、download、ファイル・JavaScriptのdialog、media・permission要求を既定拒否し、audioをmuteする。HTTP/S以外への遷移を拒否するが、これは通信先ネットワークの隔離や完全なブラウザhardeningではない。DOM automation、OSアクセシビリティ橋渡し、JavaScript/host RPC、対話入力、GPU共有、任意の信頼できないページへの製品保証は含めない。

CEF・binding・helperは互換性を固定して明示的に取得し、ライセンス・第三者通知を含めて配布を検証する。helperが欠ける通常UI利用者へruntime取得や新しいデスクトップサービスを要求しない。

### 公開API差分

基準コミットに対する主要な追加を示す。UiElementUpdateContextは既存型へのメンバー追加であり、そのほかは新規型・宣言である。

```diff
+namespace Lumyte.UI
+{
+    // 既存型へのプロパティ追加。初mountも公開前に所属を検証する。
+    public sealed class UiElementUpdateContext
+    {
+        public UiSurface Surface { get; }
+    }
+    public sealed record UiCommittedTextInput(string Text) : UiInput;
+}
+namespace Lumyte.UI.WebView
+{
+    public enum UiWebViewState { Creating, Ready, Closing, Closed, Faulted }
+    public sealed class UiWebViewSession
+    {
+        // 生成と解放はWindowが所有し、利用者は借用する。
+        public UiWebViewState State { get; }
+        public Uri? CurrentUri { get; }
+        public bool IsLoading { get; }
+        public bool CanGoBack { get; }
+        public bool CanGoForward { get; }
+        public string? FailureCode { get; }
+        public bool InputAvailable { get; }
+        public bool PopupAvailable { get; }
+        public bool ImeAvailable { get; }
+        public bool NavigationCommandsAvailable { get; }
+        public string? UnsupportedCapability { get; }
+        public event EventHandler? StateChanged;
+        public void Cancel();
+        // helperの能力falseでは、受理せずNotSupportedExceptionとする。
+        public ValueTask NavigateAsync(Uri uri, CancellationToken cancellationToken = default);
+        public ValueTask GoBackAsync(CancellationToken cancellationToken = default);
+        public ValueTask GoForwardAsync(CancellationToken cancellationToken = default);
+        public ValueTask ReloadAsync(CancellationToken cancellationToken = default);
+        public ValueTask StopLoadingAsync(CancellationToken cancellationToken = default);
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            [Composable]
+            public partial class WebView : Lumyte.UI.Compose.Definitions.Widget
+            {
+                [ComposeParameter] public UiWebViewSession? Session { get; set; }
+            }
+        }
+    }
+}
+namespace Lumyte.UI.Application.WebView.Cef
+{
+    public sealed record CefWebViewOptions
+    {
+        public string? HelperExecutablePath { get; init; }
+        public string? SupervisorExecutablePath { get; init; }
+        public bool AllowUnsandboxed { get; init; } = false; // 信頼コンテンツ向けの明示選択のみ
+        public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(10);
+        public TimeSpan NavigationTimeout { get; init; } = TimeSpan.FromSeconds(15);
+        public int MaximumSessions { get; init; } = 1;
+        public long CpuPixelBudgetBytes { get; init; } = 268435456;
+        public long GpuImageBudgetBytes { get; init; } = 268435456;
+        public string? ProfileDirectory { get; init; } // 現在は未対応
+        public bool RequireIme { get; init; } // trueならbackendだけ失敗
+    }
+    public static class CefWebViewApplicationExtensions
+    {
+        public static UiApplicationBuilder UseCefWebViews(
+            this UiApplicationBuilder builder, CefWebViewOptions options);
+        public static UiWebViewSession GetWebViewSession(
+            this UiApplicationWindowContext window, string key, Uri initialUri);
+    }
+    public static class CefWebViewBootstrap
+    {
+        // 互換shim。UI内bootstrapは行わずfalseを返す。
+        public static bool TryRunSubprocess(string[] args, out int exitCode);
+    }
+}
```

## 検討した代替案

- UI内でCEFを初期化する: 接続が簡単でもnative abortをUIから隔離できないため採用しない。例外の捕捉だけを代替にはしない。
- OSの子WebViewを重ねる: OS標準runtimeへ委譲しやすいが、通常のGPU描画列と同じclip・重ね順を保証できないため今回の合成経路にはしない。
- ホスト全体のsession設定を修復する: アプリ外のサービスへ影響するため、任意部品の自動処理にはしない。必要な回避策は明示選択したhelperの環境へ限定する。
- 起動期限や安全設定を無制限に緩める: Browserのために主UIの起動を待たせることになるため採用しない。
- GPU共有を最初から必須にする: OS・device間の同期とhandle寿命が増えるため、CPU画素経路の任意部品から開始する。

## 結果と影響

通常UIはブラウザの可用性に依存せず、ブラウザを使う場合だけhelper・runtime・必要な局所launcherを明示する。代わりに、初期対応は受動表示へ限定し、未対応の操作は能力として公開する。フォールバックであることは安全設定の自動無効化や、失敗を成功として扱う許可ではない。

受け入れ確認では、fake helperの異常終了・不正packet・無応答・取消を使い、実際のUI ownerが更新を継続することを確認する。native helperのsandbox異常も主UIへ波及しないことを別に検査する。実ページの描画・resize・close、sandbox付き配布、他RID、局所session launcherは、それぞれ実行条件を分けて検証し、engine-free testの成功から推測しない。
