# テスト

`tests/<カテゴリ>/<プロジェクト名>/`に検証コードを配置します。性能の測定方法は[ベンチマーク一覧](../benchmarks/README.md)を参照してください。

## 実行レーン

テスト本体の依存で分類します。.NET test host自体は「別プロセス」に数えません。

| 入口 | 契約 | CI |
| --- | --- | --- |
| Test --lane cpu / Test --lane cpu | managedな値・所有権・記録。外部tool、子process、native library、GPUを呼ばない | Linux x64のみ |
| Test --lane integration / Test --lane integration | FakeToolを起動するbuild検証、Slang、Parley、usvg、native Audio、native window、通信 | Linux / Windowsのx64 / arm64 |
| Test --lane gpu / Test --lane gpu | 実backendのshader実行・readback・pixel・native device契約 | Linux x64のsoftware Vulkan、Windowsの既存opt-in条件 |
| Test --suite browser | Node契約、Browser process、WebGPU、Audio | 独立Linux x64 job |

build/TestLanes.jsonがsolution内のxUnit projectの正本です。新projectは分類しないと失敗します。
混在projectのIntegrationClasses、個別のExecution=Integration trait、
GPU Fact/TheoryのExecution=Gpu traitを使い、GPU優先で排他的に選びます。
未指定のmanaged testはそのprojectの既定レーンへ入るため、新しいFactを追加しても実行漏れません。
新しいnative/tool/processテストは対応するclass一覧またはtraitも更新してください。
Toolsの純粋なcatalog・JSON検証、managed WAVE、fake window、Text/Editor benchmarkのmanaged契約はCPU側へ残し、同じproject内のFakeTool・native/font・compiler経路だけを統合側へ分けます。
CPUレーンは継承したLUMYTE_*TEST* / LUMYTE_*BENCHMARK* opt-inを子test processから除去します。

各レーンは通常のFallout入口で呼びます（例: dotnet run --project build -c Release -- --target Test --lane cpu --host-only）。
--projectで混在projectを指定しても同じfilterを適用します。
各レーンは必要なbuildを先に行い、TRXをartifacts/test-results/{cpu,integration,gpu}/へ保存します。
「CPU」はテスト本体の条件です。混在projectのコンパイル・埋め込みshader生成にはSDK/native build toolが必要な場合があります。
従来のTestは全体／projectをfilterせずに実行する互換入口です。Test --lane integrationはTest --lane integrationの互換名です。
Browser・AOT・sample・Rust等のprocess検証は下記の専用入口も維持します。
GPUを使わずに確認できるrectangle poolの所有権・8→10 allocation契約は既存CpuGraphicsDeviceへ移しました。
backend固有のvalidation、native retirement、pixel/readbackの試験はfakeへ置き換えていません。

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
dotnet run --project build -c Release -- --target Test --suite signals
```

`Test --suite signals`は本体の単体テスト、sample、ベンチマークの動作確認をまとめて実行します。`BenchmarkSignals`はその成功後に測定します。

## 統合・環境依存の検証

| 対象 | 配置・実行入口 |
| --- | --- |
| 実通信と診断UI | `Diagnostics/Lumyte.Diagnostics.IntegrationTests`、`Test --suite diagnostics-processes`、`Test --suite diagnostics-ui` |
| Audioの実行経路 | `Audio/Lumyte.Resources.Audio.IntegrationTests`、`Samples`、`Test --suite aot` |
| GPU・ネイティブ描画 | 各Graphics projectのGPU／native Fact。必要なbackend・driverを準備 |
| Parley／Rust | [Parleyの手順](Graphics/Lumyte.Graphics.Text.Parley.Tests/README.md)、`Test --suite text`、`Test --suite rust` |
| Native window | `Platform/Lumyte.Platform.Tests`、Linux X11は`Test --suite windowing` |
| Browser | `Test --suite browser-contracts`、`Test --suite wasm`、`Test --suite browser-audio` |
| NativeAOT | `*.AotSmoke`などを`Test --suite aot`でpublish・実行 |

Node.jsのBrowser契約テストは対象機能の`tests/`内に、診断galleryツールのNode.js／Pythonテストは対象スクリプトと同じ`tools/diagnostics/`にあります。実行条件と各targetの詳細は[共通ビルド手順](../build/README.md)を参照してください。

### CPUのメソッド単位並列実行

Test --lane cpu --test-concurrency 4は同じclassのmethodも並列実行します。
projectは直列、同時method数はassemblyごとに最大4（指定範囲1–64）です。
明示的collection・assemblyの直列境界と、theoryデータ行の順序は維持します。
GPU/integrationはこのschedulerを使わずcollectionも直列化します。
[選択肢・移行と検証](../build/TASK-MIGRATION.md)を参照してください。
