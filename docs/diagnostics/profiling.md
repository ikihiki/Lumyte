# 本体開発向けプロファイルの取得

本体の EventSource イベントを EventPipe 経由で `.nettrace` に収集します。独自の収集セッションや保存形式は使いません。概要は既存の Metrics、論理 trace は ActivitySource のままです。

## 取得

Release build を用意し、診断ポートへアクセスできる権限で実行します。必要な SDK・Slang・DXC はリポジトリの開発環境に従って準備してください。

```sh
dotnet-trace ps
dotnet-trace collect --process-id 12345 \
  --profile gc-verbose \
  --providers "Microsoft-DotNETCore-SampleProfiler,Lumyte-UI-Application-Profiling:0xF:4,Lumyte-UI-Profiling:0xF:4,Lumyte-Drawing2D-Profiling:0xF:4,Lumyte-RenderGraph-Profiling:0xF:4" \
  --output lumyte.nettrace
```

`12345` は対象の PID に置き換えます。起動時から必要なら、`--process-id` の代わりに `-- dotnet path/to/application.dll` を末尾へ指定します。Enter または `--duration` で収集を停止します。

次は既存の Headless サンプルを起動時から収集する例です。サンプルは通常のアプリケーションループ、入力、配置、保持描画、animation、停止を実行します。GPU 描画を行う例ではありません。

```sh
dotnet build samples/Lumyte.UI.Application.Headless.Sample -c Release
dotnet-trace collect --profile gc-verbose \
  --providers "Microsoft-DotNETCore-SampleProfiler,Lumyte-UI-Application-Profiling:0xF:4,Lumyte-UI-Profiling:0xF:4" \
  --output headless.nettrace \
  -- dotnet samples/Lumyte.UI.Application.Headless.Sample/bin/Release/net10.0/Lumyte.UI.Application.Headless.Sample.dll
```

## 選択と解釈

- `0x1`: フレーム・相関などの制御イベント。完全なフレーム解析にはホストのこの keyword も購読します。
- `0x2`: CPU 処理区間。
- `0x4`: 区間ごとの処理量カウンター。通常の EventCounter の定期サンプルではありません。
- `0x8`: 対応した経路の GPU 結果用の予約。CPU の待機を GPU 時間へ変換する機能ではありません。
- RenderGraph の `0x10`: pass ごとの詳細区間。通常の `0xF` には含まれず、必要なら `0x1F` にします。

具体的な場所、stage ID、counter ID、単位は [UI.Application](../adr/ui/UI-0026-application-profiling-points.md)、[UI](../adr/ui/UI-0027-ui-profiling-points.md)、[Drawing2D](../adr/graphics/GRAPHICS-0030-drawing2d-profiling-points.md)、[RenderGraph](../adr/graphics/GRAPHICS-0031-render-graph-profiling-points.md) を参照してください。共通契約は [DIAGNOSTICS-0007](../adr/diagnostics/DIAGNOSTICS-0007-development-profiling.md) にあります。

PerfView などのイベント表示、または TraceEvent の EventPipe 読み取り機能で解析します。スタック専用の変換だけで、これらの独自イベントもすべて可視化されるとは限りません。元の `.nettrace` を保存してください。

ProfileRunId と FrameId で同じフレームを結び、provider 内の SpanId と ParentSpanId で区間を結びます。GraphRunId は Compile 一回の識別子であり、実行全体の ProfileRunId とは別です。複数 graph を同じフレームで実行した場合も GraphRelation の root span と operation で対応付けます。

ホスト相関なしでも有効なモジュールの計測は残り、Guid.Empty／FrameId=0 は未帰属を表します。途中接続、途中停止、バッファ圧迫では開始・終了や相関が欠け得ます。不明な対応を時刻の近さだけで補完しないでください。

CPU 区間は経過時間で、待機やスケジューリングを含みます。親子と並列区間を単純加算せず、スタックサンプルの比率を CPU 専有時間の割合とみなさないでください。

## 検証の範囲

EventListener による契約テストと、既存の配置・描画・フレーム・資源寿命の回帰テストを使います。標準 `dotnet-trace` による実取得は、既存 Headless サンプルと、既存の CPU テスト用 device／command fixture を使う Drawing2D・RenderGraph の公開 API 呼び出しで確認します。CPU fixture の取得は GPU の実行・画素・timestamp 対応を証明しません。

収集・解析のログと生トレースはローカルの `artifacts/profiling-validation/` に保持します。検証用に生成した consumer／parser はその中の非配布ハーネスです。実アプリには不要です。

計測無効時の追加割当と有効時の挙動をテストしますが、一定割合以下の計測負荷や性能改善は保証しません。比較では同じ入力・build・backend・提示方式を使い、ウォームアップと詳細計測の有無を分けてください。

ブラウザーや診断無効ランタイムで同じ EventPipe 取得ができるとは限りません。GPU timestamp の共通 API、専用ビューア、リモート取得操作は追加していません。

### 実取得の確認

2026-11-11、固定 SDK 10.0.403、dotnet-trace 10.0.750501、TraceEvent 4.0.0 で Release build を取得・解析しました。

- CPU fixture の Drawing2D／RenderGraph：開始・終了 80 組、未対応の開始・終了 0、EventsLost=0、EventSource エラー 0。
- 既存 Headless アプリ：受理・終了フレーム各 5、開始・終了 540 組、RunMetadata 1 件、未対応の開始・終了 0、EventsLost=0、EventSource エラー 0。
- 両トレースで標準ランタイムのイベントも同じファイルに存在します。回数はこの実行の観測値であり、owner loop の回数などはスケジューリングで変わります。

この確認は取得・対応付けの動作確認で、GPU timestamp や物理表示、計測負荷の一定割合以下を保証する測定ではありません。

### Trimming／NativeAOT

各モジュールの低水準イベント書き込みは、宣言済みの Guid／Boolean／Int32／Int64 を固定幅でコピーします。RunMetadata も Guid と string だけを渡します。動的なオブジェクトやプロパティをシリアライズしないため、IL2026 はこれらの書き込みメソッドだけに根拠付きの局所抑制を置きます。イベントの型や書き込み方式を変える場合は、この根拠も再確認してください。全体のトリミング警告やCIのエラー扱いは維持します。

.NET 10 NativeAOT は既定で EventSource を無効にします。イベント機能を有効にするには、対象実行ファイルの publish に `-p:EventSourceSupport=true` を明示します。製品全体の既定値は変更しません。

既存の `tests/Graphics/Lumyte.Resources.Graphics.AotSmoke` だけはこの機能を有効にし、厳格な NativeAOT publish と通常／`--host` 実行で、GraphRelation のpayload型、非空の区間開始・終了の対応、EventSource エラー不在を検証します。既存の資源寿命検証も維持しています。このin-process検証を、すべてのNativeAOT環境でのEventPipe接続やBrowser上の実取得の保証とは扱いません。

## Browser PNGキャプチャの障害診断

UI Application／Navigation／Pages／TransitionsのBrowser検証は、既存の`LUMYTE_REQUIRE_SOFTWARE_WEBGPU=1`によるCI実行でキャプチャ診断を有効にします。ローカルでは`LUMYTE_BROWSER_CAPTURE_TRACE=1`を明示できます。通常のアプリ実行では無効です。起動前に標準EventSourceのobserverを接続するため、PNG要求より前から進行しているフレームも追跡できます。

キャプチャ要求IDと既存のrun／frame識別子を関連付け、dispatcher、要求queue、開始待ちの描画、prepare、submission完了、image解放、readback、画素copy／crop、PNG変換の到達段階を記録します。`UI Capture trace:`はmanaged側の構造化イベントで、検証側は`browser-capture-event`としてCIログへ転送します。既存のWebGPU API observerとは別の観測境界なので、時刻の一致だけからGPU内部の原因を断定しません。

失敗時はBrowserを閉じる前に、直近イベントと有界な状態snapshotを`browser-capture-failure`へ出力します。snapshot照会だけに最大2秒の独立したCDP期限を設け、managedのキャプチャ期限や既存のCDP期限を延長しません。Browser自体が応答しなければsnapshot取得不能を記録し、すでに受け取った最終イベントを残します。診断の取得失敗で元の例外を置き換えません。既知timeoutの限定許容条件、画素assertion、終了コードも変更しません。

snapshotの`terminalFrames`は、取消し／エラーを含む要求終了を観測した時点の関連frameとstageを有界に保持します。`observation:"atTerminal"`の内側の`openStages`は終了時点の記録で、取得時にも待機中という意味ではありません。現在の状態はtop-levelの`openStages`と区別します。

イベント／履歴には上限と欠落・打切り表示があります。正常完了したrequestが再度別frameへ関連付けられた場合も、そのIDから重複処理を調べられます。この診断の追加自体はキャプチャの取消し・重複除去・再試行を行いません。UI文字列、入力内容、画素やPNG bytesはログへ含めません。経過時間はCPU側の観測値であり、GPU timestampや性能比較結果ではありません。
