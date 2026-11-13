# ベンチマーク

Core全体のケース設計と既存ケースの統合方針は[Coreベンチマークのケース設計](../docs/benchmarks/core/README.md)を参照してください。設計段階であり、カタログ全体が実装済みという意味ではありません。

性能を比較するためのケースと再現手順を管理します。測定値や生成レポートはGitへ追加せず、`artifacts/`またはCIのartifactへ保存してください。変更時の正しさは[テスト](../tests/README.md)で確認します。

| 対象 | 配置・手順 | 測定するもの |
| --- | --- | --- |
| Mathematics | [Lumyte.Mathematics.Benchmark](Core/Lumyte.Mathematics.Benchmark/README.md) | 境界計算とbatchのCPU時間・割当量 |
| Signals | [Lumyte.Signals.Benchmark](Core/Lumyte.Signals.Benchmark/README.md) | 読出し、依存グラフ、配送、非同期変換の時間と割当量。保持メモリのprobeも含む |
| Execution | [Execution sample](../samples/Lumyte.Execution.Sample/README.md#ベンチマーク) | 直接実行、delegate、型付きcommand、batchの配送費用 |
| Vulkan | [Vulkanの比較手順](../src/Graphics/Lumyte.Graphics.Vulkan/README.md#pso生成費用の比較方法) | pipeline cache有無によるdraw記録・submit・破棄の費用 |

ExecutionとVulkanはsampleの起動・device構築を使用するため、測定コードもsampleに配置しています。

グラフィックスのシナリオ別測定と比較条件は[ADR-GRAPHICS-0027（提案）](../docs/adr/graphics/GRAPHICS-0027-scenario-based-graphics-benchmarks.md)にまとめています。

非同期のPR部品スクリーニングは[運用と測定範囲](../docs/performance/component-screening.md)を参照してください。
通常のCIやマージを待たせず、退行候補と未測定範囲をActionsに保存します。

対象別projectはテストと同じカテゴリ構成の`benchmarks/<カテゴリ>/<対象project>.Benchmark`へ配置します。

## 実行

SDKと依存ツールは[開発環境](../docs/development-environment.md)の手順で準備します。リポジトリルートから実行してください。

```sh
dotnet run --project build -c Release -- --target BenchmarkSignals
dotnet run --project build -c Release -- --target PipelineBenchmark
```

Signalsは`artifacts/signals-benchmarks/`、Vulkanは`artifacts/test-results/vulkan-pipeline-benchmark.jsonl`に出力します。Executionの標準出力を保存する場合も`artifacts/`を使用してください。BenchmarkDotNetを直接実行する場合は`--artifacts`で保存先を指定できます。既定の`BenchmarkDotNet.Artifacts/`もGit管理外です。

測定はReleaseで行い、同じマシン・runtime・ケース・パラメーターを使って比較します。測定を並列実行せず、環境情報とばらつきを結果と一緒に保存してください。時間の閾値を単体テストの合否には使いません。
