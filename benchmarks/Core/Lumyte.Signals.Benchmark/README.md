# Lumyte.Signals benchmarks

Signalの読出し、依存グラフ、更新配送、非同期変換の時間と割当量をBenchmarkDotNet 0.15.8で測定します。対象は.NET 10 / Releaseです。SDKはリポジトリの`global.json`に従います。

## 実行

リポジトリルートから共通runnerを実行します。`TestSignals`は単体テスト、sample、ベンチマークの動作確認を行い、`BenchmarkSignals`はその後に全ケースを計測します。

```sh
dotnet run --project build -c Release -- --target TestSignals
dotnet run --project build -c Release -- --target BenchmarkSignals
```

測定結果は`artifacts/signals-benchmarks`へ保存し、Gitへ追加しません。READMEには実行手順・ケース・測定条件を記載します。特定のケースや保持メモリの検証はprojectを直接実行できます。ローカルNuGet feedがまだないcheckoutでは、先に`artifacts/nuget`ディレクトリを作成してください。

```sh
dotnet test tests/Core/Lumyte.Signals.Benchmarks.Tests -c Release
dotnet run --project benchmarks/Core/Lumyte.Signals.Benchmark -c Release -- --retention
dotnet run --project benchmarks/Core/Lumyte.Signals.Benchmark -c Release -- --filter '*ReuseBenchmarks*' --artifacts artifacts/signals-reuse
```

ベンチマークの動作確認は`tests/Core/Lumyte.Signals.Benchmarks.Tests`へ集約しています。結果値、通知回数、購読解除、非同期処理の失効、共有依存グラフの鮮度をxUnitで確認し、時間の良否は判定しません。旧`--smoke`の代わりに上記の`dotnet test`を使用してください。

## ケースと1操作の意味

パラメーター展開後は48ケースです。入力、callback、購読、完了済みTaskは原則として測定の外で用意します。

| クラス | ケース | 1操作 |
| --- | --- | --- |
| Scalar | Value / Peek / 同値代入 / 未購読の変更 | 読出しまたは代入1回 |
| Fanout | Effect 1・16・128件 | 代入1回と全Effectの配送 |
| Computed | 深さ1・8・32、購読あり／なし | キャッシュ読出し、または代入と最新値の取得 |
| Graph | diamond / 動的分岐 | 更新1回とEffectの配送 |
| Graph | CaptureReads / IsCurrent | 8入力の記録、または記録済み集合の検証 |
| Batch | unbatched / batched | 16代入全体と最終配送 |
| Scheduler | 手動schedulerで集約 | 16代入全体と予約された配送の実行 |
| Async | completed create / reload | 同期完了するloaderの作成・破棄、または再取得 |
| Async | observed reload | 再取得とPending・Succeededの2通知 |
| Async | supersede / cancel | 未完了処理2件の生成、置換、キャンセル、旧処理の完了・清掃 |
| SharedDag | 2・4・8層、購読あり／なし | 値が変わっていない共有依存DAGの読出し1回 |
| Reuse | 1・16・128依存 | 安定した依存集合での更新、またはEffectの作成・破棄 |
| Validation | 共有DAG 2・4・8層 | 無関係なSignalまたは入力の更新とDAGの読出し |

共有DAGは各層に2個のComputedを作り、両方が直前の層の2ノードを読みます。SharedDagは最初の計算後のキャッシュ検証を測り、Validationは変更stampが進むケースを測ります。Reuseの作成・破棄は同じ入力とcallbackを使うため、それらの生成費用は含みません。Asyncのsupersede/cancelはTaskCompletionSource 2個の生成も含み、旧Taskを各操作内で完了させます。

## 測定条件と制限

ジョブは1 launch、3 warmup iterations、8 measurement iterations、目標150 ms/iterationです。BenchmarkDotNetが呼出し回数を調整し、呼出しオーバーヘッドを補正します。MemoryDiagnoserによる割当量と、CSV・Markdown・完全なJSONを出力します。比較する実装は同じ環境・同じケースを使用し、計測を並列実行しないでください。

絶対時間はCPU、VMの負荷、JITに依存します。平均だけでなくError・StdDevも確認し、小さな時間差は複数launchで再測定してください。これは単一スレッドのJITマイクロベンチマークであり、UI描画、ネットワーク、ThreadPoolの配送遅延、複数スレッドの競合、NativeAOT性能は含みません。ウォームアップ後の値なので、コールドスタート時のプール初期化・辞書の最初の確保とは異なります。

## プールと保持メモリ

`Microsoft.Extensions.ObjectPool` 10.0.0を使い、返却時に辞書をClearします。未使用辞書の保持上限は64個、保持する辞書の割当て済み容量は1,024以下です。容量はCountではありません。上限は保持数を制限し、使用中の辞書数や生成数を制限しません。外部へ渡すread setをプールへ返さず、評価・交換・失敗処理で所有権を維持します。詳細は[Signals ADR](../../../docs/adr/signals/SIGNALS-0001-independent-signal-runtime.md)を参照してください。

`--retention`は小規模なウォームアップ後に、16個の参照型入力を共有する1,000 Effectを保持し、2回更新してからDisposeし、グラフへの参照を解放します。各段階でFull GC後のheapを測り、入力payloadの弱参照が解放されることを確認します。

heap増分はグラフ全体の概算で、1操作の割当量ではありません。ウォームアップ済みのbaseline、runtimeのcache、意図的に保持する空のプール辞書もあるため、解放後の差分だけをリーク量と解釈しないでください。

## 検証

変更後は`TestSignals`を実行してください。[単体テスト](../../../tests/Core/Lumyte.Signals.Tests/README.md)が例外後の追跡復元、評価中の書込み拒否、動的依存、キャンセル、外へ保持したread set、購読解除後の参照解放を確認し、ベンチマークの動作確認テストが各測定ケースの前提を確認します。

## 参考資料

- [Microsoft ObjectPool](https://learn.microsoft.com/en-us/aspnet/core/performance/objectpool?view=aspnetcore-10.0)
- [DefaultObjectPoolの.NET 10実装](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/ObjectPool/src/DefaultObjectPool.cs)

## 非同期スクリーニング

通常の測定設定と保持メモリprobeは変更しません。明示的な短縮ジョブは次で実行します。

```sh
dotnet run --project benchmarks/Core/Lumyte.Signals.Benchmark -c Release -- --screening-smoke
dotnet run --project benchmarks/Core/Lumyte.Signals.Benchmark -c Release -- --screening --filter '*ScalarBenchmarks*' --artifacts artifacts/signals-screening
```

同じprojectのケースを使用し、独立したコピーは保持しません。比較エンジンはこのproject全体を両製品SHAへ同一に重ねます。
測定対象の選択・実行・解析・通知はFalloutへ統合しています。詳細は[運用と実行手順](../../../docs/performance/component-screening.md)を参照してください。
