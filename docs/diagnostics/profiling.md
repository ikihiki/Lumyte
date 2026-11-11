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
