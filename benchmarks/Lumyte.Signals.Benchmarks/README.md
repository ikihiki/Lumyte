# Lumyte.Signals benchmarks

Signalの読出し、依存グラフ、更新配送、非同期変換の時間と割当量をBenchmarkDotNet 0.15.8で測定します。対象は.NET 10 / Releaseです。SDKはリポジトリの`global.json`に従います。

## 実行

リポジトリルートから共通runnerを実行します。`TestSignals`は単体テスト、sample、ベンチマークの動作確認を行い、`BenchmarkSignals`はその後に全ケースを計測します。

```sh
dotnet run --project build -c Release -- --target TestSignals
dotnet run --project build -c Release -- --target BenchmarkSignals
```

測定結果は`artifacts/signals-benchmarks`へ保存します。特定のケースや保持メモリの検証はprojectを直接実行できます。ローカルNuGet feedがまだないcheckoutでは、先に`artifacts/nuget`ディレクトリを作成してください。

```sh
dotnet run --project benchmarks/Lumyte.Signals.Benchmarks -c Release -- --smoke
dotnet run --project benchmarks/Lumyte.Signals.Benchmarks -c Release -- --retention
dotnet run --project benchmarks/Lumyte.Signals.Benchmarks -c Release -- --filter '*ReuseBenchmarks*' --artifacts artifacts/signals-reuse
```

`--smoke`は結果値、通知回数、購読解除、非同期処理の失効、共有依存グラフの鮮度を確認します。時間の良否を判定するテストではありません。

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

## 2026-10-31の測定記録

採用した実装で全48ケースを測定しました。環境はDebian 13、Intel Xeon Platinum 8573CのKVM、SDK 10.0.401 / runtime 10.0.12、Concurrent Workstation GCです。5 vCPUが見え、cgroupのCPU割当ては4CPU相当です。

比較元はPR #99の`0791f5d1798656e2f6d4489e42e0496bc73bda85`に、比較処理と内部読出しの不要な辞書・クロージャ生成だけを削減した前段階の試作です。未変更のPRとの直接比較ではありません。比較元も同じセッションで代表15ケースを再測定しています。

採用版は変更stampによる未購読Computedの検証省略、無効化の重複除去、依存辞書のObjectPool、直接のrevision集約とAction評価、配送用クロージャの分離、既存lock内の不要な再lock削減を合わせています。表の改善全体をObjectPool単独の効果として解釈しないでください。

| 操作 | 比較元の平均 | 採用版の平均 | 比較元の割当て | 採用版の割当て |
| --- | ---: | ---: | ---: | ---: |
| 未購読DAG・8層のキャッシュ読出し | 14.310 µs | 22.32 ns | 0 B | 0 B |
| 未購読chain・32段の更新＋読出し | 25.192 µs | 3.272 µs | 11,584 B | 0 B |
| 128 Effectへの更新 | 48.616 µs | 18.383 µs | 58,600 B | 0 B |
| 未購読Signalの変更 | 64.26 ns | 50.97 ns | 64 B | 0 B |
| 動的分岐の切替え | 690.41 ns | 406.38 ns | 912 B | 0 B |
| 128依存のEffect作成・破棄 | 9.304 µs | 6.622 µs | 10,512 B | 120 B |
| 無関係な更新後のDAG・8層の読出し | 14.095 µs | 902.34 ns | 64 B | 0 B |
| DAG・8層の更新＋読出し | 37.337 µs | 2.465 µs | 4,864 B | 0 B |

外部へ渡すCaptureReadsの記録辞書は再利用しません。このケースの割当ては1,016 Bのままであり、内部辞書のプール化がすべてのAPIの割当てを消すわけではありません。

## プールと保持メモリ

`Microsoft.Extensions.ObjectPool` 10.0.0を使い、返却時に辞書をClearします。未使用辞書の保持上限は64個、保持する辞書の割当て済み容量は1,024以下です。容量はCountではありません。上限は保持数を制限し、使用中の辞書数や生成数を制限しません。外部へ渡すread setをプールへ返さず、評価・交換・失敗処理で所有権を維持します。詳細は[Signals ADR](../../docs/adr/signals/SIGNALS-0001-independent-signal-runtime.md)を参照してください。

`--retention`は小規模なウォームアップ後に、16個の参照型入力を共有する1,000 Effectを保持し、2回更新してからDisposeし、グラフへの参照を解放します。各段階でFull GC後のheapを測り、入力payloadの弱参照が解放されることを確認します。

測定したheap増分は稼働中1,377,264 B、解放後36,768 Bで、payloadは解放されました。これはグラフ全体の概算で、1操作の割当量ではありません。ウォームアップ済みのbaseline、runtimeのcache、意図的に保持する空のプール辞書もあるため、解放後の差分だけをリーク量と解釈しないでください。

## 検証

変更後は単体テストと`--smoke`を実行し、例外後の追跡復元、評価中の書込み拒否、動的依存、キャンセル、外へ保持したread set、購読解除後の参照解放を確認してください。測定版ではlinux-x64のNativeAOTをILLink/ILC warnings-as-errorsでpublishし、生成したsampleの実行も成功しています。AOT性能の測定は行っていません。

## 参考資料

- [Microsoft ObjectPool](https://learn.microsoft.com/en-us/aspnet/core/performance/objectpool?view=aspnetcore-10.0)
- [DefaultObjectPoolの.NET 10実装](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/ObjectPool/src/DefaultObjectPool.cs)
