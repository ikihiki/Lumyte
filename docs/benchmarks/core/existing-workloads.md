# 既存のCore関連ベンチマークを統合する方針

基準コミットは `05d391955a607e7fb948812c9660dc2bed32704d`。[共通契約](../../adr/core/CORE-0005-benchmark-catalog-and-modes.md)に従い、既存の入力・処理・oracleを優先してケース群へ対応付ける。これは統合の設計表であり、この文書だけでコード移動や計時方式の置換を完了したとは扱わない。

## 統合の区分

- 継続・再利用: 現在の対象別projectの処理を共通カタログから参照する。同じメソッドをコピーして別ハーネスを増やさない。
- 契約を整えて統合: 処理とoracleを保持し、計時・fixture依存・アクセス・resetを共通契約へ合わせる。
- 履歴として保持: 過去の対照実装・計測結果。現在の製品版の測定値や現在のA/B比較に混ぜない。
- 他カテゴリへ帰属: Coreを呼び出していても、測定している主体がUI・GraphicsならCoreへ移動しない。

## Signalsの既存ケース

[現在のproject](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Signals.Benchmark/Lumyte.Signals.Benchmark.csproj)は既にbenchmarks/Core/Lumyte.Signals.Benchmarkへ配置されている。以下を継続し、[Signalsのケース設計](signals-resources-settings.md)へ意味ごとに対応付ける。

| 既存ファイル・処理 | 統合する意味 | 方針 |
| --- | --- | --- |
| ScalarBenchmarks: ReadValue・ReadPeek・WriteSame・WriteUnobserved | 読み出し、非追跡、同値対照、実更新 | 同値対照と更新を独立IDにする |
| ComputedBenchmarks: ReadObservedCached・ReadUnobservedCached・UpdateObserved・UpdateAndReadUnobserved | 購読状態、cache、再評価 | cache hitとdirty再評価を混ぜない |
| FanoutBenchmarks: Update | observer数に対する配送 | 通知回数と最新値を既存oracleで確認 |
| BatchBenchmarks: Unbatched16・Batched16 | batch有無と通知集約 | 1batchと1writeの換算単位を明示 |
| GraphBenchmarks: DiamondUpdate・SwitchBranch | diamond依存、動的依存切替 | 最新値と依存解除を維持 |
| GraphBenchmarks: CaptureEightReads・CheckReadSet | 読み取り集合の構築、鮮度検査 | 構築と検査の費用を独立ケースにする |
| SharedDagBenchmarks: ReadObservedCached・ReadUnobservedCached | 共有依存DAGのcache検証 | graph規模と実評価回数を確認 |
| ValidationBenchmarks: ReadAfterUnrelatedWrite・UpdateAndRead | 無関係な更新後の検証、関連更新 | 実際の再評価有無をoracleに含める |
| ReuseBenchmarks: ReevaluateStable・CreateAndDisposeEffect | 依存記録の再利用、購読生成・解除 | lifecycle込みと定常更新を分ける |
| SchedulerBenchmarks: Coalesced16 | 明示flushでのscheduler集約 | OS scheduling待ちとは呼ばない |
| AsyncBenchmarks: CreateCompleted・ReloadCompleted・ReloadCompletedObserved・SupersedeAndCancelPending | 同期完了、再読込、購読、失効とcancel | 各状態と完了結果の意味を保持 |
| RetentionProbe.Run | 生存する保持量と解放後checkpoint | latencyとB/opに合算せず、専用の保持量観測へ帰属 |

[既存benchmark動作テスト](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Benchmarks.Tests/WorkloadTests.cs)を正当性の根拠として再利用する。基準の全benchmarkメソッドと現行12ケースのスクリーニング集合は同じ範囲ではない。[既存選択](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/build/ComponentScreeningData.cs)が扱わない既存ケースもカタログへ残す。

[Program](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Signals.Benchmark/Program.cs)の通常jobや--screeningは実在する入口だが、共通4モードを実装済みとは扱わない。job設定を変えるときも入力・oracle・ケース版をそろえ、比較証拠を取り直す。

## Mathematicsの既存ケース

[現在のproject](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Mathematics.Benchmark/Lumyte.Mathematics.Benchmark.csproj)も既にCoreへ配置されている。幾何の入力と対照を再利用し、[数値のケース設計](numeric-memory-text.md)へ統合する。

| 既存の処理 | 残す意味 | 統合時に整えること |
| --- | --- | --- |
| Program: Geometry2D.GetTransformedBoundsBcl | 変換後bounds、出力消費、独立した四隅oracle | 対象版間の同じ製品入口をBenchmarkDotNetで測る |
| Program: former array/LINQ reference | かつての方式の対照と正しさの参照 | 過去の方式を現在の製品baselineと誤認しない。履歴対照は明示して分離 |
| BatchBenchmark: Single・Double | 座標spanの距離計算、scalar oracle、末尾とtiny input | 固定入力と出力消費、型・長さをcase profileへ分ける |
| SIMD無効・AVX無効の実行 | fallback経路の確認 | CPU機能とruntime設定を別profileとして記録し、製品既定とは混ぜない |

[Program](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Mathematics.Benchmark/Program.cs)と[BatchBenchmark](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Mathematics.Benchmark/BatchBenchmark.cs)の独自ループ・warmup・current-thread計測は、そのまま共通モードの実装として複製しない。既存の入力・oracle・計算境界を保持して、managed CPUの計時は既存BenchmarkDotNetに合わせる。

## Execution sampleの既存処理

[ExecutionBenchmarks](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/samples/Lumyte.Execution.Sample/ExecutionBenchmarks.cs)のdirect、cached_delegate、captured_delegate、typed_single、typed_batch_16はCore.Executionの比較素材である。対象名がAudio風でも、ここで測るのはsampleのDemo処理とcommand配送であり、音声backendの性能ではない。

[実行のケース設計](execution-composition-worlds.md)では、次のように統合する。

- DemoModule・DemoVoiceのsample依存を、最小の決定的command fixtureへ切り離す。公開APIと生成コードの実際の経路を維持する。
- directとdelegateを計時対照として残す場合は、製品のcommand実行とは別のcase IDにする。
- typed_singleはenqueue、協調drain、結果取得を含む1完了要求として明示し、enqueueだけのケースと混ぜない。
- typed_batch_16はbatch全体とcommand当たりの換算を明示する。Resetとrun.Disposeを含めるかを固定し、時刻予算による途中打切りが起きていないことを確認する。
- 既存の20000回のStopwatchループを新runnerとして保持せず、同じ仕事量をBenchmarkDotNetの操作へ移す。

これは統合先と必要な抽出作業を示す設計であり、新しいExecution.Benchmark projectやfriend accessは本変更で追加しない。

## 他カテゴリに残す既存ハーネス

| 既存の配置 | 測定主体 | Coreへの扱い |
| --- | --- | --- |
| tools/benchmarks/ui-functions・ui-widgets | UiSurfaceの部分更新とlayout | Signalsを使っていてもUI。Coreの配送ケースと重複して移さない |
| tools/benchmarks/ui-input | UI hit test・capture・dispatch | InputやCoreではなくUI入力境界 |
| tools/benchmarks/ui-optimization・ui-retained | UI host・layout・編集統合・window | Coreの依存を含む統合例。Core単体結果とは別にする |
| tools/benchmarks/ui-device・ui-device-trace | 実device経路とtrace解析 | UI・Graphicsと診断ツールへ帰属 |
| tools/benchmarks/drawing2d-incremental | Drawing2Dの構築・更新・保持と用途別描画 | Graphics。幾何を呼ぶだけでCoreへ移さない |
| tools/benchmarks/drawing2d-skia-bdn | 他描画ライブラリとの同条件比較 | Graphics統合。Core CPU退行検知とは分離 |
| samples/Lumyte.Graphics.Shared/PipelineBenchmark.cs | GPU pipeline・command費用 | Graphics backendへ帰属 |
| tools/colors/Lumyte.NamedColors.Generator | 色定数コードの生成・検証 | 生成ツールの責務。Coreの色演算と混ぜない |

## 履歴と移行の完了条件

過去の測定データやREADMEの説明は、そのSHA・runtime・設定・境界の証拠として保持する。統合後の新しいケースへ過去値を無条件に移植しない。旧パスを整理する際は既存ADRの本文を改変せず、必要なら文書の移動案内を残す。

実装時は既存の全ケースを対応付け、残す・同義統合・履歴対照・他カテゴリのいずれかを明示する。処理、仕事量、oracle、寿命が一致しない場合は別caseを保つ。sourceと入力をそろえた再検証を終えるまでは、移植済み・比較互換・性能同等とは報告しない。
