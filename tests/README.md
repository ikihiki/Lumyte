# テスト

`tests/<カテゴリ>/<プロジェクト名>/`に検証コードを配置します。性能の測定方法は[ベンチマーク一覧](../benchmarks/README.md)を参照してください。

## 単体テスト

| カテゴリ | 主な検証対象 |
| --- | --- |
| Core | Composition、Execution、Mathematics、Memory、Resources、Settings、Signals、StateMachines、Worlds |
| Animation | 補間、再生境界、イベント、出力とソースの寿命 |
| Input | 登録所有権、通知、Action、Context、設定連携 |
| Graphics | 描画モデル、RenderGraph、リソース、画像効果、文字レイアウト |
| Audio | デコード、リソース登録 |
| Platform | WindowSystemとplatform契約 |
| Diagnostics | 生成コード、操作、テレメトリー、各機能の診断 |
| Tools | ビルド工程、サンプルランチャー |

単体テストは値、通知、例外、所有権、解放などの契約を確認します。Graphics、Audio、Platformには環境依存のテストもあるため、対象projectのREADMEとFact属性の実行条件を確認してください。

SDKと依存ツールを[開発環境の手順](../docs/development-environment.md)で準備し、リポジトリルートから実行します。

```sh
# 全体。native依存も必要です。
dotnet run --project build -c Release -- --target Test --host-only

# 変更したprojectを選択。
dotnet run --project build -c Release -- --target Test --project tests/Core/Lumyte.Signals.Tests

# .NETのみで実行できるprojectを直接実行。
dotnet test tests/Core/Lumyte.Signals.Tests -c Release
```

直接実行する初回checkoutでは、NuGet.configが参照する`artifacts/nuget`ディレクトリを先に作成してください。共通runnerの結果は`artifacts/test-results/`に保存します。直接実行時の`TestResults/`もGit管理外です。

## ベンチマークの動作確認

`Core/Lumyte.Signals.Benchmarks.Tests`は測定コードを参照し、各パラメーターの結果値、通知、購読解除、非同期処理の失効をxUnitで検証します。測定時間や割当量は合否に使いません。Signals本体の単体テストはBenchmarkDotNetへ依存しません。

```sh
dotnet test tests/Core/Lumyte.Signals.Benchmarks.Tests -c Release
dotnet run --project build -c Release -- --target TestSignals
```

`TestSignals`は本体の単体テスト、sample、ベンチマークの動作確認をまとめて実行します。`BenchmarkSignals`はその成功後に測定します。

## 統合・環境依存の検証

| 対象 | 配置・実行入口 |
| --- | --- |
| 実通信と診断UI | `Diagnostics/Lumyte.Diagnostics.IntegrationTests`、`TestDiagnosticsProcesses`、`TestDiagnosticsUi` |
| Audioの実行経路 | `Audio/Lumyte.Resources.Audio.IntegrationTests`、`Samples`、`TestAot` |
| GPU・ネイティブ描画 | 各Graphics projectのGPU／native Fact。必要なbackend・driverを準備 |
| Parley／Rust | [Parleyの手順](Graphics/Lumyte.Graphics.Text.Parley.Tests/README.md)、`TestText`、`TestRust` |
| Native window | `Platform/Lumyte.Platform.Tests`、Linux X11は`TestWindowing` |
| Browser | `TestBrowserContracts`、`TestWasm`、`TestBrowserAudio` |
| NativeAOT | `*.AotSmoke`などを`TestAot`でpublish・実行 |

Node.jsのBrowser契約テストは対象機能の`tests/`内に、診断galleryツールのNode.js／Pythonテストは対象スクリプトと同じ`tools/diagnostics/`にあります。実行条件と各targetの詳細は[共通ビルド手順](../build/README.md)を参照してください。
