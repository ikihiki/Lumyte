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
