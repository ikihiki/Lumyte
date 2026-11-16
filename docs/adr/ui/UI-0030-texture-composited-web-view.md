# ADR-UI-0030: ブラウザを通常のUI描画へ合成する

- 状態: 採用
- 日付: 2026-11-16
- 基準コミット: `7f78d63ea2d1b11107ec6fee24b269079d5705eb`

## 背景

Lumyteで画面を作る利用者は、ヘルプやWebアプリをUI内に表示し、通常の部品と同じ重ね順・クリップで操作したい。基準コードには画像表示はあるが、Webページの実行、継続的な画面更新、ページへの入力配送を担う部品はない。

基準コミットの `src/UI/Lumyte.UI.Media/ImageElement.cs` と `PreparedImage.cs` は借用画像を記録し、GPU資源の保持を呼び出し側へ要求する。`src/UI/Lumyte.UI.Application.Desktop/DesktopDriver.cs` の標準デスクトップ実行環境はWindowsとLinuxを選択する。`Lumyte.UI.Application.Browser` はLumyte自身をWeb上で動かす実行環境であり、埋め込むブラウザエンジンとは別である。

[UI-0004の描画](UI-0004-drawing-and-clipping.md)、[UI-0005の入力](UI-0005-pointer-dispatch-and-capture.md)、[UI-0008の資源寿命](UI-0008-render-graph-and-resource-lifetime.md)、[UI-0025の借用画像](UI-0025-borrowed-media-and-progress.md)を維持する独立した機能追加とする。OSの子ウィンドウを置く場合は、その領域だけ通常のGPU合成から外れるため、同じ描画保証を与えられない。

## 決定

任意の追加パッケージである `Lumyte.UI.WebView` に宣言型の `WebView` を設け、ブラウザが画面外へ描画した結果を通常のDrawing2D画像として合成する。Windows・Linux向けの第一候補はCEFとし、まずCPU画素の取り込みを基準経路にする。エンジンの直接参照は専用アダプターへ隔離し、UI本体、Media、通常のアプリ起動にブラウザの依存や初期化を追加しない。

CEF版の正式な対応RID、.NETバインディング、NativeAOT互換性、sandboxを有効にした配布方法は受け入れ条件による選定事項である。ここでCEF版を実装済み・全環境対応とはしない。ブラウザのGPU共有資源の直接取り込み、macOS、モバイル、Web実行環境へのiframe配置は今回の対応対象外とする。

### 利用者の変更前・変更後

変更前は `Lumyte.UI.Media.Compose.Image` で準備済み画像を表示できるが、URLを渡してページを開く手段はない。独自の `UiElement.OnRecord` から画像を描いても、HTML実行環境と入力・資源寿命の接続は利用者が用意する必要がある。

変更後の提案例は次のとおり。標準アプリがエンジン起動、セッション、画素転送、終了を所有する。フォントや通常のUI構成は省略するが、CEF固有のプロセス起動処理は省略しない。

```csharp
using System;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Desktop;
using Lumyte.UI.Application.WebView.Cef;
using Lumyte.UI.WebView;
using static Lumyte.UI.WebView.Compose;

// 提案API。CEFの子プロセスならここで処理して終了する。
if (CefWebViewBootstrap.TryRunSubprocess(args, out int exitCode))
    return exitCode;

var builder = UiApplication.CreateBuilder(args).UseDesktop();
builder.UseCefWebViews(new CefWebViewOptions());
builder.UseRoot(window =>
{
    // 同じWindowインスタンスとkeyの組は再構築しても同じセッション。
    var page = window.GetWebViewSession("help", new Uri("https://example.com/"));
    return WebView(session: page, width: 800, height: 600,
        automationName: "ヘルプ");
});

await using var app = builder.Build();
await app.RunAsync();
return 0;
```

`GetWebViewSession` は宣言作成時に同期的にハンドルを返すが、ブラウザ作成と最初の遷移は非同期に進む。起動中の部品は空の領域を確保する。URL変更はセッションの `NavigateAsync` で行い、再描画や宣言の再生成によって再遷移しない。利用者は `State` と `StateChanged` を通常の状態表示へ接続できる。

### 宣言、命令、状態と要求の分離

WebView宣言は接続先Sessionと配置だけを保持する。戻る・進む・再読込・読込停止はSessionの明示的な一回命令とし、CanGoBack・CanGoForward・CurrentUri・IsLoadingを読み取り専用の状態として通知する。遷移要求とブラウザが報告する現在URLを同じ双方向の値として扱わない。

入力・viewport・focusの内部要求は型付きのhost接続へ渡す。ページからの権限要求、新規ページ、download、ファイル選択を利用者が非同期で承認する公開APIは今回追加せず、既定拒否を維持する。SessionがCEFの実体、DOM、native handleを返すことはない。

### 責務と依存方向

追加する配置と責務は次のとおり。パッケージ名と同じディレクトリ名を `src/UI/` 配下へ置く。

- `Lumyte.UI.WebView`: 宣言、永続Element、セッションの公開ハンドル、エンジンと独立した内部接続契約。UIとDrawing2Dへ依存し、Platform、CEF、Applicationへ依存しない。
- `Lumyte.UI.WebView.Cef`: CEFの画面外描画、入力変換、プロセス・プロファイル管理。WebView共通部に依存する。既存の保守されたバインディングを優先し、必要条件を満たさない場合だけ限定的なネイティブ境界を検討する。
- `Lumyte.UI.Application.WebView.Cef`: 標準Application、Desktop、WebView、CEFアダプターの接続。GPUアップロード、ownerへの通知、Windowとの対応、IME、終了を調整する。

利用者へCEFのブラウザ、native pointer、画素バッファ、GPU view、ポンプを返さない。独自host向けの実行APIやエンジン差し替えの公開プラグインAPIは今回設けず、標準Desktopへの接続だけを公開する。

基準コードの `src/UI/Lumyte.UI.Application/IUiApplicationParticipant.cs` と `UiApplicationBuilder.Participants` はinternalで、別アセンブリからそのまま使えるAPIではない。Applicationから新しい接続アセンブリへの `InternalsVisibleTo` を追加し、既存participantへの登録と後述の内部フレーム通知を使う。WebView共通部とCEFアダプターから接続アセンブリへのfriend指定も必要な範囲で明示する。ApplicationがCEFを直接参照する依存逆転は行わない。

### セッションとスレッド

一つのアプリが一つのブラウザ実行環境を所有し、その下にWindowの一意なインスタンスIDとkeyでセッションを登録する。プロセス内のCEF初期化は一回とし、複数のアプリによる同時初期化とshutdown後の再初期化要求は起動エラーにする。再起動にはアプリのプロセスを作り直す。同じkeyの再取得では最初の初期URLを維持し、異なる初期URLを渡した場合は引数エラーとする。keyは空文字を許可しない。

セッションの状態は `Creating → Ready → Closing → Closed` とし、作成失敗・ブラウザ異常終了では `Faulted` へ移る。Faultedも終了時にはClosingを経てClosedへ進み、Creating中のclose要求でも遅れて作成されたブラウザを必ず閉じる。読込中かどうかと現在のURLはこの状態とは別に保持する。主文書の通信エラーはブラウザ自体の故障にしない。Faultedからの暗黙の再起動やPOST再送はせず、Windowを閉じて新しいインスタンスを作る。

セッションはWindowが所有し、部品は借用する。同一セッションを同時に二つのElementや別Windowへ接続することは拒否する。unmount時はfocusとcaptureを解除して非表示にし、セッションはWindow終了まで保持する。Window終了は登録済みセッションをすべて閉じるため、短命な部品を大量に作る用途にはkeyを使い回す責務がある。

CEFのブラウザUIスレッドとLumyteのUI ownerを同一と仮定しない。コマンドはCEF側の所定スレッドへ投入し、状態・画素・要求は有界のキューを通してownerへ戻す。コールバックでUI宣言・GPU・Surfaceを直接操作せず、相互の同期待ちもしない。ownerへの一回のwakeをまとめ、CEFが要求する次の処理時刻をhostの待機期限へ反映し、無条件の高頻度ポーリングはしない。

遷移には単調な番号、接続には世代を付ける。キャンセルは未投入なら遷移しない。投入後なら呼び出しの待機を取消すだけで、ページ側の要求を巻き戻したとは保証しない。NavigateAsyncの成功は遷移要求の受理を示し、読込完了はStateChangedで観測する。Creating中の遷移は上限16件まで順に予約し、超過は受付失敗にする。Readyで順に処理し、Closing・ClosedのコマンドはObjectDisposedException、Faultedでは理由コード付き例外とする。読込通知は遷移番号と主frameを照合し、旧遷移の完了で新しいIsLoadingを解除しない。

### レイアウト、更新と描画

WebViewは固有の文書サイズをUIへ返さず、与えた幅・高さをページのviewportとする。既定要求寸法は800×600論理単位で親制約を優先する。最終寸法が正の有限値でなければ、ゼロ面積は休止し、無限大はレイアウトエラーにする。文書のスクロール範囲で親のレイアウトを拡張しない。

viewportの画素寸法は論理寸法とWindowのscaleから切り上げて求める。CEFへ論理viewport、画素密度、表示状態を別々に伝え、ブラウザのページズームとは区別する。resizeとDPI変更ごとに世代を進め、古い世代の画素は公開しない。新しい画面が届くまで直前の画面を現在の矩形に配置するが、入力座標は新しいviewport基準とする。

CPU画素の取り込みはブラウザから通知されたバッファをコールバック内で所有バッファへコピーして終える。コールバックを抜けた後に元ポインターを参照しない。初期実装は完全な画面の最新一枚を保持し、未取り込みの古い画面を置き換える。差分矩形の累積不備を避けるため部分更新の最適化は行わない。画素形式、行の向き、stride、premultiplied alpha、sRGBの扱いをアダプターで正規化し、二重のalpha乗算や上下反転を生じさせない。

CEFの選択リスト等の描画用popupは別画素面として保持し、ブラウザの本体座標で配置する。これは新しいページを要求するpopupとは異なる。UI側の共通clipと重ね順を適用し、ブラウザ領域外へはみ出す部分は初期仕様では切り取る。任意のネイティブウィンドウを暗黙に生成しない。

ownerはlayout前に受信状態を取り込み、viewport確定後に新しい画素をGPUへ転送する。最新画面をキューから取り出したownerは読取leaseを取得し、コピー完了までCEF callbackの最新画面置換から分離する。転送には基準の `src/Graphics/Lumyte.Graphics.Abstractions/IGraphicsCommandBuffer.cs` の `CopyBufferToTexture` を使う。行alignmentを含むupload bufferをLumyte側で所有し、CPUからそのbufferへのコピー完了でCPU読取leaseを戻し、upload bufferは転送を含むGPU送信の完了まで保持する。upload bufferと変換用一時画素もCPU画素予算へ算入し、空きがなければ最新画面の取り込みを保留する。公開した画像のrevision変更で `InvalidateDrawing` を呼び、画素更新だけではlayoutを無効化しない。描画のgetterやOnRecordからCEFの処理・通信・GPU転送を起動しない。非表示は描画・入力を止めCEFへ通知するが、ページの通信や音声まで停止したとは保証しない。音声は既定でmuteとする。

通常のUIと同じclip・重ね順に従う。基準入力系が保証しない任意回転・射影・3D変換の座標逆変換は追加保証しない。WebView自身とそれを含む祖先のlive captureは不可とし、共有要素遷移では通常のcapture対象から除外する。子のcapture不可を祖先へ伝播するUI側の判定追加が必要であり、既存の `SupportsTransitionCapture` をfalseにするだけでは完了としない。

### CPU・GPU資源と終了

UI-0025のPreparedImageへ新しい画像を代入するだけでは寿命管理は解決しない。retained scene、準備済みframe、未送信command、送信済みGPUが参照する各画像に、内部の資源leaseを対応付ける。retained sceneの置換はsceneの参照だけを解放し、frameの参照をまとめて解放しない。

Applicationの `UiApplication.Frames.cs` と実際のGPU送信経路へ、資源の予約・未送信破棄・送信完了の内部通知を追加する。Drawing2Dが保持する画像からWebView所有資源を識別するregistryを使い、実際に準備されたsnapshotの参照集合をframeへ固定する。現在のElement列挙だけから推測しない。録画と予約の間に画像を再利用しないようownerで直列化する。既存のinternal participant.AdvanceだけでGPU完了が分かるとは扱わない。

未送信の失敗ではcommandとsnapshotを破棄した後に予約を戻す。送信済みならGPU完了の証拠が得られるまで保持する。提示完了、Waitの取消、Window close、Element unmountはそれぞれ単独では解放条件にしない。device lossはそのdeviceの送信済み処理が退役したことをバックエンドが保証してから破棄し、古いGPU画像を再利用しない。

セッションごとの本体・popupそれぞれにGPU画像を最大3枚まで持つ。現在sceneまたはframeが借用している画像は書き換えない。空きがなければGPU完了待ちで表示を据え置き、CPU側の最新画面だけ更新する。既定のアプリ全体予算はCPU画素・upload256MiB、GPU画像256MiB、セッション4個とする。サイズ計算はcheckedで行い、現在・退役待ち・resize先・popupを含む予算に入らない要求を明示的に失敗させる。この予算はLumyte側で所有する画素とGPU画像のpayloadを対象とし、CEFのrenderer・JavaScript・cacheやGPU allocatorの管理領域まで含むプロセス全体の上限ではない。CPU側もコピー中・取り込み中・最新待ちをすべて算入する。無制限に画像を作ってフレーム落ちを隠さない。

終了は二段階とする。第一段階は新規コマンド拒否、入力・IME停止、CEFへのclose要求とclose完了の確認までで、ここでparticipantの `IsWindowStopped` をtrueにする。sceneやGPU leaseの退役をこの判定で待たない。基準の `src/UI/Lumyte.UI.Application/UiApplication.Stopping.cs` の `CloseEntryAsync` はこの停止判定の後にGPU drainとSurface detachを行うためである。

第二段階では、既存のGPU drainとSurface detachによるscene解放の後、native Windowを破棄する前に新設する内部 `RetireWindowResourcesAsync` 通知で残る画像・upload leaseを退役させる。この非同期通知はownerをブロックせず、GPU転送だけの送信も含めて完了を待つ。全Windowの第二段階が済んでからセッションの最終解放とCEF shutdownを実行し、Platformとdeviceの終了へ進む。全体の `IsStopped` は第二段階までを含む。終了待ち中もCEF・owner・GPUポンプを継続し、期限超過は既存のStopFailedとして保持・再試行する。close済みのCEF処理を再発行せず、生きたコールバック先を先に解放しない。ブラウザ子プロセス異常も同じ退役規則を使う。

### 入力、focusと日本語入力

WebViewElementは通常のUiElementとしてhit testを受け、body-local座標とDPIをアダプターへ渡す。Down時のfocusとcapture、領域外へのdrag、Up、Cancelを通常UIと同じ順で扱う。keyの物理入力とテキスト入力を分け、OS focus喪失、unmount、menu/modalによる停止でcaptureとIMEを必ず終了する。ブラウザの戻る等のショートカットをアプリ全体から横取りしない。

Tabはページ内移動をCEFへ渡し、文書の端からfocusを外へ返す通知を受けた場合だけ、同じSurfaceの次または前のfocus候補へ移す。非同期の返却は接続世代とfocus要求番号を確認し、古い通知で別の部品のfocusを奪わない。既存の `src/UI/Lumyte.UI/UiSurface.Focus.cs` の公開 `MoveFocus(UiFocusNavigation.Next/Previous)` を使い、focus移動のための新しいfriend境界は作らない。

基準の `src/UI/Lumyte.UI.TextEditing/IUiTextInputHandler.cs` はTextBoxElementを受け、`src/UI/Lumyte.UI.Windowing.TextInput/UiTextInputConnection.cs` もTextDocumentを所有する。この接続をWebViewへそのまま適用したり、不可視のTextBoxでDOM編集を模倣したりしない。

さらに基準の `src/Platform/Lumyte.Platform.TextInput.Abstractions/ITextInputService.cs` のBeginはTextDocumentとnavigatorを要求し、Windowsの `src/Platform/Lumyte.Platform.Windows.TextInput/TsfTextStore.cs` は同期的な文書操作を行う。非同期のDOM状態をこの文書として返す設計にはせず、既存Platformの公開text service契約を変更しない。

WindowsのWebViewにIMEを提供する場合は専用のIMM32互換経路を必要とする。既存WindowsPlatformのnative message配送へ、接続アセンブリだけが登録できるinternal IME受信境界とfriend指定を追加する。WebViewがfocusを持つ間だけ対象HWNDのIME contextを所有し、WM_IME系メッセージからpreedit・属性・caret・確定文字列を読み、UTF-16範囲でCEFのcomposition設定・確定・取消へ順序付きで渡す。composition結果と通常文字メッセージの二重配送を防ぐ。Lumyteの既存TSF sessionはその間停止し、focus返却前にcomposition終了とIME contextの復元を完了する。

この経路はTSFの同期文書要求をCEFへ代理転送しない。ブラウザ文書全体の同期取得や再変換は初期仕様に含めず、IMEのIMM32互換動作で通常の変換・確定・取消を提供する。CEFのcaret通知から候補位置をviewport→Surface→実Window→screenへ変換し、DPI・resize・focusの世代が古ければ位置を公開せずcompositionを止める。キーから推測して文字を生成しない。

Windowsの既存text serviceがあることはWebView対応の証拠にはならない。Linuxの標準DesktopDriverは基準コードでtext serviceを返さないため、Linuxの日本語入力は別途実装と実Windowでの受け入れ確認が必要である。初期のLinux版ではIMEは未対応とし、Windowsも専用経路の接続と利用可能なIMEを確認するまでtrueにしない。対応能力に `ImeAvailable` を公開し、未対応を隠さない。日本語入力を必須とする配布では、この能力がfalseなら起動を拒否できる設定を用意する。

### 権限、ページ境界と配布

初期仕様で受け付けるURLは絶対http/httpsだけとする。ナビゲーションのredirectもブラウザ側のポリシーで検査する。TLS証明書エラーは拒否し、file、javascript、外部プロトコルへの遷移を暗黙に許可しない。これはアクセス先ネットワークを隔離する仕組みではなく、埋め込みページは通常のネットワークアクセスを持つ。信頼できないページから社内・ローカルサービスを保護する必要がある場合は、別の通信制御が必要になる。

永続プロファイルは既定で使わず、Cookie・認証・ストレージの共有は同じアプリ実行環境内に限定する。任意のディスクプロファイルを指定する場合は排他所有し、別プロセスとの同時利用を拒否する。ログへCookie、入力内容、URLのquery・fragmentを保存しない。ログイン済みの通常ブラウザのプロファイルを自動取得しない。

新規ページpopup、download、ファイル選択、clipboard読み取り、camera/microphone、位置情報、通知、印刷、外部アプリ起動は既定拒否とする。今回のAPIで許可経路を用意しない機能は、対応済みとは表示しない。JavaScript実行、DOM取得、任意の.NETオブジェクト公開、ページからhostへのRPCは公開しない。DevToolsとremote debuggingも通常配布では無効にする。

CEFのsandboxを既定で有効とし、起動できない環境を `--no-sandbox` で自動回避しない。Windowsでは採用CEF版の公式native bootstrapとclient DLLの起動形態を配布境界とし、sandbox初期化をCLRとCEFの起動より前に行う。上記のmanaged利用例はそのclient側であり、TryRunSubprocessだけでnative sandbox初期化を代替したとはしない。Linuxも必要なhelper・実行権限を配布検証する。配布物には子プロセス、必要なruntimeデータとlocale、ライセンス・第三者通知を含める。エンジンとバインディングのABIを固定して一緒に検証し、セキュリティ更新時はアプリの配布単位で更新する。DRM、独自codec、拡張機能、一般ブラウザと同じログイン・決済互換性は保証しない。

### 実験的な対応境界

CEFアダプターは通常のsolutionとpackへ含めず、利用者が別プロジェクトを明示的にbuildしてUseCefWebViewsを登録する場合だけ使用する。CEFの起動を伴うsampleも実験用backendを明示して選ぶ。CEFの世代が古い配布物はproduction対応として採用しない。

Windowsはnative bootstrapが成立するまで起動を拒否し、Linuxもsandboxを利用できない環境では失敗させる。専用IME経路が成立するまでは両環境ともImeAvailable=falseとし、RequireImeを指定した構成は拒否する。通常文字入力のOS取得、wheel・副ボタン入力、同寸法へ戻るresizeでのpaint世代識別も、対応を保証するための受け入れ対象とする。

画像再利用はscene・snapshot・read・frameのleaseに加え旧Imageの到達性を保守的に検査する。安全な退役が確認できない場合は再利用せず、GCまで表示更新を保留し得る。この制約を隠して滑らかな動画表示を保証しない。

### 公開API差分

基準コミットへの新規型と既存型のメンバー追加を示す。宣言は今回の主要部であり、生成factoryは既存のComposable規則に従う。名前空間 `Lumyte.UI.WebView` と部品factoryのWebViewは、Web実行環境のBrowserと区別する。

```diff
+namespace Lumyte.UI
+{
+    public sealed class UiElementUpdateContext
+    {
+        // 既存型への追加。初mountも含め、公開前に所属を検証する対象Surface。
+        public UiSurface Surface { get; }
+    }
+    // 物理キーやIME preeditとは別の確定文字。OSでの取得経路はhostが担う。
+    public sealed record UiCommittedTextInput(string Text) : UiInput;
+}
+namespace Lumyte.UI.WebView
+{
+    public enum UiWebViewState { Creating, Ready, Closing, Closed, Faulted }
+    public sealed class UiWebViewSession
+    {
+        // コンストラクターと解放は公開しない。Windowが所有する。
+        public UiWebViewState State { get; }
+        public Uri? CurrentUri { get; }
+        public bool IsLoading { get; }
+        public bool CanGoBack { get; }
+        public bool CanGoForward { get; }
+        // Faultedの理由。入力内容や認証情報を含めない。
+        public string? FailureCode { get; }
+        public bool ImeAvailable { get; }
+        public event EventHandler? StateChanged;
+        // UI ownerから呼ぶ。絶対http/httpsのみ。成功は読込完了ではなく受理。
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
+                // 必須の借用。nullは宣言検証時に拒否し、二重接続も拒否する。
+                [ComposeParameter] public UiWebViewSession? Session { get; set; }
+            }
+        }
+    }
+}
+namespace Lumyte.UI.Application.WebView.Cef
+{
+    public sealed record CefWebViewOptions
+    {
+        // nullは一時プロファイル。指定パスはこの実行環境が排他所有する。
+        public string? ProfileDirectory { get; init; }
+        public bool RequireIme { get; init; } = false;
+        public int MaximumSessions { get; init; } = 4;
+        public long CpuPixelBudgetBytes { get; init; } = 268435456;
+        public long GpuImageBudgetBytes { get; init; } = 268435456;
+    }
+    public static class CefWebViewBootstrap
+    {
+        // UI作成前に呼ぶ。trueなら返されたexitCodeで直ちに終了する。
+        public static bool TryRunSubprocess(string[] args, out int exitCode);
+    }
+    public static class CefWebViewApplicationExtensions
+    {
+        // 二重登録、非Desktop driver、非正の予算はBuild時までに拒否する。
+        public static UiApplicationBuilder UseCefWebViews(
+            this UiApplicationBuilder builder, CefWebViewOptions options);
+        // UI owner限定。閉じたWindowはObjectDisposedException。
+        public static UiWebViewSession GetWebViewSession(
+            this UiApplicationWindowContext window, string key, Uri initialUri);
+    }
+}
```

`StateChanged` はownerで状態snapshotを更新した後に発火する。購読解除は利用者が行い、subscriber例外は既存アプリのcallback失敗規則に従う。profile以外の既定拒否権限を緩めるオプションは追加しない。WebViewの既定Roleは既存のGroup、focus適格性はtrue、ClipToBoundsはtrueとする。Sessionは生成factory上の省略可能引数であっても宣言の検証で必須とし、省略・nullを公開前に拒否する。

## 検討した代替案

- OSの子ウィンドウを埋め込む: WindowsのWebView2やLinuxのWebKitGTKを使いやすいが、通常UIと任意の重ね順・clipを共有できない。ネイティブ領域だけ別の描画規則にする案は今回選ばない。
- OSごとのWebViewを画面外描画へ統一する: runtime配布の負担を抑え得るが、単なる共通インターフェースでは全OSで同じ画素取得・IME・合成が成立する保証にならない。今回のWindows・Linux共通の基準経路にはしない。
- CEFの共有GPU画像を直接取り込む: CPUコピーを減らせる可能性があるが、backendごとの共有handle、同期、所有権が必要になる。現在のクロスbackend保証を曖昧にせずCPU画素経路から開始する。
- iframe: Web実行環境での既存エンジン利用には適するが、frame禁止ヘッダー、同一origin、DOM上の重ね順、canvasとの合成に制約がある。CEF版と同じ能力を装わず今回の範囲から外す。
- HTML/CSS/JavaScriptエンジンを自作する: ブラウザ互換性とセキュリティ保守の負担が目的に対して過大なため採用しない。

## 結果と影響

利用者はURLを開くためにGPU資源や入力の橋渡しを実装せず、通常の部品としてページを配置できる。一方、CEF配布容量、子プロセス、更新責務、CPU転送コスト、Window終了時のdrainが加わる。既存のImage・TextBox利用者の呼び出し方は変えない。UI共通部には公開前の所属検証に使うUiElementUpdateContext.Surfaceと、確定文字を配送するUiCommittedTextInputを追加する。アプリ内部のframe寿命・focus連携・IME調停とcapture判定にも追加が必要になる。

初期のautomationはWebViewを一つの名前付きfocus可能領域として扱い、ページ内DOMをLumyteのautomation treeへ偽装しない。CEFのアクセシビリティ情報をOS支援技術へ橋渡しする実装は今回含めず、ページ内容のスクリーンリーダー対応は未対応と明示する。これを必須とする製品では、その受け入れ確認が済むまで採用しない。

受け入れ条件は次のとおり。設計文書の検査と、実装後に必要な実機検証を区別する。

- バインディング選定: CefGlue等の保守された候補を対象に、sandbox付きWindows・Linux配布で子プロセス起動、非同期close、必要なOSR・IME callback、trim・NativeAOTを実際に確認する。満たせなければ対応環境を狭め、AOT対応を推測で宣言しない。
- 描画: 不透明・半透明・細い文字・popupの基準画像、DPI変更、resizeの遅延応答、ゼロ寸法、通常UIの前後重ね、祖先clip、capture除外を確認する。GPUでの色変換とalphaを検査し、CPU側だけで合格にしない。
- 入力: drag中の領域外Up、focus喪失、Tab往復、modal停止、日本語の通常変換・確定・取消・候補位置、TextBoxとの排他切替、Window間移動を確認する。WindowsのIMM32互換経路が成立しないIMEでは未対応と明示する。LinuxはIME未対応の能力表示とRequireImeによる起動拒否を確認し、日本語入力対応済みとは扱わない。
- 資源: callbackバッファを直後に破壊する偽エンジン、描画を遅延する偽GPU、旧scene保持、未送信失敗、送信後の取消、resize連打、予算超過で不正参照と上限超過がないことを確認する。
- 終了・権限: navigation中のunmount、close callback遅延、renderer異常、device loss、sandbox起動失敗、証明書エラー、redirect、popup・download等の既定拒否、永続profile排他を確認する。公開Webサイトの可用性をCIの前提にせず、制御したローカルHTTPページを使う。

滑らかさや転送の低コストは未測定のため保証しない。まず単一WebViewの正しい表示・入力・終了を成立させ、複数ページや動画の実用性は別途計測して判断する。
