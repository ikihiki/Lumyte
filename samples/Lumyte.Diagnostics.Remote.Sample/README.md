# 診断サーバーへ接続するゲームサンプル

[診断サーバー](../../src/Diagnostics/Lumyte.Diagnostics.Server/README.md) を別端末で起動し、同じ LUMYTE_DIAGNOSTICS_GAME_TOKEN を設定して実行する。

```sh
dotnet run --project samples/Lumyte.Diagnostics.Remote.Sample -c Release -- magiconion http://127.0.0.1:5001 60
# HTTP も同じサブシステム・Agent・ゲームループで動く。
dotnet run --project samples/Lumyte.Diagnostics.Remote.Sample -c Release -- http http://127.0.0.1:5000 60
```

引数は通信方式、接続先、実行秒数。接続後は SessionId を表示し、最大 60 秒のゲームループで BeforeInputProcessing の Pump を呼ぶ。Game は待ち受けポートを開かない。

InputDiagnostics と InputOverrideService は既存ローカルサンプルをそのまま共有する。Operation の opt-in 属性一つからスキーマと private メソッドへの配送が生成される。通信方式によってサブシステムを変更しない。

```csharp
services.AddDiagnosticExecutionPoint<BeforeInputProcessing>("input.before-processing");
services.AddDiagnosticSubsystem<InputDiagnostics, BeforeInputProcessing>(new("input", "Input", 1));
services.AddDiagnosticAgent<BeforeInputProcessing>();
```

所有スレッドで Activate → ConnectAsync → RunAsync → Pump の順に実行する。ネットワーク callback は bounded queue への投入だけを行い、所有スレッドで Input を更新する。操作時には `Input state: Jump=True`、終了時には `Disconnected: Jump=False` と表示する。

Counter / ActivitySource / ILogger を使って Metrics / Trace / Log を生成し、共通 Agent がサーバーへ送る。最初の Counter には 2^53 より大きい整数を記録し、実通信で精度を保持することを確認できる。

終了時は Agent を停止してから所有スレッドで ReleaseSession と Deactivate を呼び、その後に DI スコープを DisposeAsync で解放する。切断後の自動再接続は行わない。Input は小さなドメインモデルであり、既存デバイス入力の InputSystem への統合はまだ含まない。

[サーバー側の操作方法](../../src/Diagnostics/Lumyte.Diagnostics.Server/README.md)、[自動で別プロセス起動する再現スクリプト](../../docs/diagnostics/communication-verification.md) を参照する。

## 設定診断

絶対パスの `LUMYTE_DIAGNOSTICS_SETTINGS_PATH` を指定すると、音声設定を同じ実行ポイントで公開します。

```sh
export LUMYTE_DIAGNOSTICS_SETTINGS_PATH=/tmp/lumyte-audio-settings.json
dotnet run --project samples/Lumyte.Diagnostics.Remote.Sample -c Release -- http http://127.0.0.1:5000 60
```

サーバーのSettingsページで`settings.audio`の`read`、`save`、`save-result`を操作します。
volumeとmutedを編集でき、deviceは読み取り専用です。保存にはreadで取得したRevisionが必要です。
saveのjob-idでsave-resultを実行し、Savedを確認してください。保存ファイルはサンプル終了後も保持します。
設定診断APIの詳細は[Lumyte.Diagnostics.Settings](../../src/Diagnostics/Lumyte.Diagnostics.Settings/README.md)を参照してください。

## アニメーション診断

`LUMYTE_DIAGNOSTICS_ANIMATION=1`を指定すると、`animation`を同じ実行ポイントに登録します。

```sh
export LUMYTE_DIAGNOSTICS_ANIMATION=1
dotnet run --project samples/Lumyte.Diagnostics.Remote.Sample -c Release -- http http://127.0.0.1:5000 120
```

`sample.playback`と`sample.motion`を公開し、所有スレッドのゲームループで更新します。AnimationページではPlaybackの再生、Pause、Stop、Seek、速度変更と、状態機械の`action` Triggerを確認できます。環境変数を省略した通常実行にはAnimation対象を追加しません。

診断APIと公開範囲の詳細は[Lumyte.Diagnostics.Animation](../../src/Diagnostics/Lumyte.Diagnostics.Animation/README.md)を参照してください。

## レンダーグラフ診断

`LUMYTE_DIAGNOSTICS_RENDER_GRAPH=1` を指定すると、`graphics.render-graph` を同じ実行ポイントに登録します。

```sh
export LUMYTE_DIAGNOSTICS_RENDER_GRAPH=1
dotnet run --project samples/Lumyte.Diagnostics.Remote.Sample -c Release -- http http://127.0.0.1:5000 120
```

`sample-frame` グラフを所有スレッドで約 16 ms ごとに Compile し、同じスレッドの Pump で診断要求を処理します。
描画を模した宣言から本物の render graph compiler が依存関係・culling・barrier・resource lifetime を計算します。
GPU コマンドの Record と実行は行わず、GPU や native graphics library は不要です。
capture の状態は `compiled`、Record 件数は 0 になります。FrameId は Compile ごとに増加します。

19 個の宣言 pass のうち 17 個が保持され、13 個の temporary resource を含みます。
`obsolete-scene-upload` は次の `upload-scene` が全面上書きするため除去され、`unused-debug` は未使用のため除去されます。
depth、shadow、lighting、bloom、UI、最終 copy の依存関係に加え、`frame-marker` は明示的な副作用として保持されます。
サーバーの Render graph ページで graph 名に `sample-frame` を指定して capture を要求すると、後続 Compile の構造を同じ capture ID で参照できます。
要求しない期間は軽量集計だけを行い、詳細 snapshot を生成しません。

HTTP と MagicOnion の両方で利用できます。環境変数を省略した通常実行にはグラフを追加しません。
診断 API と保持予算の詳細は [Lumyte.Diagnostics.RenderGraph](../../src/Diagnostics/Lumyte.Diagnostics.RenderGraph/README.md) を参照してください。
