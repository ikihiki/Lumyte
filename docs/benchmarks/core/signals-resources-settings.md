# Signals・Resources・Settings のベンチマーク設計

- 状態: 設計案。このカタログ全体のケース・モード選択は未実装。既存SignalsケースとFalloutによる部分的な実行は基準コードに存在する。
- 基準コミット: `05d391955a607e7fb948812c9660dc2bed32704d`。以下のソース・テストリンクはすべてこの版へ固定する。
- 共通契約: [カタログと実行モードの共通 ADR](../../adr/core/CORE-0005-benchmark-catalog-and-modes.md)。公開 API の変更はない。

## 対象と読み方

利用者は、単純な値の読み出しだけでなく、依存検証・世代管理・編集確定のどこが変化したかを比較できる必要がある。まず意味の異なる経路を列挙し、その後で実行モードを選ぶ。別名 API と全入力の直積はケースにしない。

| プロジェクト | 配置上の役割 | カタログの扱い |
| --- | --- | --- |
| `Lumyte.Signals` | 独立測定 | 値・グラフ・購読・非同期の各境界を分離 |
| `Lumyte.Resources` | 独立測定 | 公開 API と制御可能な Source・Stage・Runtime による CPU 経路 |
| `Lumyte.Resources.DependencyInjection` | 消費側との統合測定 | 登録・初回解決・所有権を測る。定常ロードの二重登録はしない |
| `Lumyte.Resources.Embedded` | 独立測定と Pipeline 統合 | 読み込み済み DLL の読取。実ファイル入力とは別系列 |
| `Lumyte.Resources.Execution` | 消費側との統合測定 | Resources から実際の Execution workflow を通す |
| `Lumyte.Settings` | 独立測定とストア統合 | メモリ内経路と原子的ディスク置換を分離 |

この 6 プロジェクトにコンパイル時専用の対象はない。Settings の JSON 型情報は標準生成器の出力を固定した入力とし、生成時間を Settings の定常時間へ混ぜない。
将来のコード配置は `benchmarks/Core/<target>.Benchmark`、起動・集約は Fallout とする。内部実装名は根拠として示すだけで、reflection・既存 IVT の流用・新しい公開入口を測定手段として約束しない。

## 入力・観測・モードの約束

- 各項目の「代表／境界／負荷」は独立した有限プロファイル。記載値の組合せを全展開せず、他の軸を代表値へ固定する。
- `verify` は正しさの確認のみ。各項目の許容欄は性能モードの適用可能性であり、頻度・環境・cold/warm の指定ではない。`compare` は独立起動、`diagnose` の追加観測は時間比較から分離する。
- 環境「管理」は同一ホストの .NET 管理実行。「実行」は明記した Execution 構成。「埋込」はロード済み同一 DLL。「ディスク」は専用一時ディレクトリと記録した FS・媒体。環境間の数値は混ぜない。
- 観測「割当」は操作中の managed 割当、「保持」は操作後も到達可能な managed オブジェクト、「計上」は Memory の予約・commit・返却、「物理」は native/pool/OS の実保持。計上と物理、割当と保持は同義ではない。
- 非同期経路は全スレッドの割当を捕捉できる方式を選ぶ。単一スレッドの差分だけを全体割当と呼ばない。GC・弱参照・native 追跡による保持確認は `verify` / `diagnose` で行い、時間区間へ強制 GC を入れない。
- 正しさの oracle は既存テストの値・回数・所有権・状態遷移。テスト中の容量確認や型名に Performance があっても性能の測定結果とは扱わない。

## Signals のワークロード群

### CORE-SIG-01 — 生成と値アクセス

- 経路・境界: [Signal](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/Signal.cs) の `Create<T>` → [Signal.Value](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/Signal.Value.cs) の `Value` / `Peek`。1 操作は生成 1 個、または既存値の読取／代入 1 回を別ケースにする。
- 入力: 代表／境界／負荷 = int／null を含む参照値・同値代入／64 要素の純粋比較器を持つ値。 復元・正しさ: 変更代入は 2 値を交互に使い、同値代入は明示した抑止ケースに限定する。[GraphTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/GraphTests.cs) の通知回数を基準に、値と比較器呼出回数を確認。
- 許容: 全件 `verify`、`screen`・`compare`・`diagnose`。環境: 管理。観測: 割当。生成物の保持は別確認。

### CORE-SIG-02 — computed の初回・キャッシュ・受動検証

- 経路・境界: [Computed](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/Computed.cs) の `Value` / `Peek`。1 操作は初回評価 1 回、温まった読取 1 回、無関係な書込後の検証 1 回、依存変更後の再評価 1 回を分ける。
- 入力: 代表／境界／負荷 = 鎖 8 段／依存 0・1・32 段／共有菱形 64 節点。 復元・正しさ: 初回は新規グラフ。無関係な source と実依存の変更を混同しない。[PassiveCacheTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/PassiveCacheTests.cs) の `UnrelatedWritesPreserveCachedSharedValues` と `PassiveChainRemainsFreshInsideBatchesAndAfterDeactivation` に合わせ値・評価回数を照合。
- 許容: 全件 `verify`、`screen`・`compare`・`diagnose`。環境: 管理。観測: 割当と受動キャッシュの保持。

### CORE-SIG-03 — 動的依存と結果同値の無効化

- 経路・境界: [Signal](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/Signal.cs) の `Computed` と [Computed](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/Computed.cs) の依存置換。1 操作は分岐切替の書込から観測完了まで 1 周期。
- 入力: 代表／境界／負荷 = 左右各 4 依存／旧枝だけの変更・結果が同じ偶奇値／各枝 64 依存。 復元・正しさ: 分岐を交互に切り替え、捨てた枝の購読が残らないことを計時外で検証。[InvalidationTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/InvalidationTests.cs) の動的分岐と [PassiveCacheTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/PassiveCacheTests.cs) の `EqualComputedResultsDoNotHideUpstreamRevisionChanges` を oracle にする。
- 許容: 全件 `verify`、`screen`・`compare`・`diagnose`。環境: 管理。観測: 割当、旧依存の保持。

### CORE-SIG-04 — ファンアウト・共有経路・バッチ

- 経路・境界: [Signal](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/Signal.cs) の `Batch` / `Effect`。1 操作は書込 1 回の全通知完了、または複数代入を包むバッチ 1 回。
- 入力: 代表／境界／負荷 = 16 observer・8 代入／observer 0・共有菱形・入れ子 2／256 observer・64 代入。 復元・正しさ: observer は固定し、各バッチの値を変える。コールバックは集計だけにする。[GraphTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/GraphTests.cs) のバッチ整合性と [InvalidationTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/InvalidationTests.cs) の `SharedPathsDeliverOncePerWrite` に従い値・配送数を確認。
- 許容: 全件 `verify`、`screen`・`compare`・`diagnose`。環境: 管理。観測: 割当、配送キューの上限。

### CORE-SIG-05 — 購読の生成・解除・scope 寿命

- 経路・境界: [Signal](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/Signal.cs) の `Effect` → [SignalEffect](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/SignalEffect.cs) の `Dispose`、[SignalScope](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/SignalScope.cs) の `Run` / `Dispose`。1 操作は購読 1 個の生成解除、または scope 1 個の構築解放。
- 入力: 代表／境界／負荷 = 16 購読／空 scope・入れ子 2・setup 失敗／256 購読。 復元・正しさ: 毎回新しい scope。共有 source は存続させ、解放後の書込で配送しないことを確認。[DependencyLifetimeTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/DependencyLifetimeTests.cs) の `DisposedEffectsReleaseSourceReferences` と [GraphTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/GraphTests.cs) の失敗 setup 所有権を利用。
- 許容: 全件 `verify`、`screen`・`compare`・`diagnose`（失敗 setup は `verify`・`diagnose`）。環境: 管理。観測: 割当と callback・source の保持。

### CORE-SIG-06 — scheduler 配送と一時停止

- 経路・境界: [SignalEffect](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/SignalEffect.cs) の `Pause` / `Resume` と `ISignalScheduler.Schedule`。1 操作はpause → 有効な複数書込 → resume → 明示キュー排出の 1 周期。
- 入力: 代表／境界／負荷 = 8 書込／配送待ちで Dispose・キュー拒否／64 observer・32 書込。 復元・正しさ: キューは周期ごとに空へ戻す。enqueue のみと完了込みを別ケースにする。[GraphTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/GraphTests.cs) の `ScheduledAndPausedObserversTrackLatestRevision` と [DirectEvaluationTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/DirectEvaluationTests.cs) の拒否時 Failure を照合。
- 許容: 全件 `verify`、`compare`・`diagnose`（拒否は `verify`・`diagnose`）。環境: 管理、決定的キュー。実 UI scheduler は消費側統合として分離。観測: 割当、保留配送数。

### CORE-SIG-07 — 受動 read set と追跡の抑止

- 経路・境界: [Signal](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/Signal.cs) の `CaptureReads` / `Untracked` → [SignalReadSet](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/SignalReadSet.cs) の `IsCurrent` / `Read`。1 操作はcapture 1 回、鮮度検査 1 回、Effect 内の replay 1 回を別測定。
- 入力: 代表／境界／負荷 = 8 依存／空・重複読取・同値結果の上流変更／128 依存。 復元・正しさ: 古い集合を増やさず各周期の集合を置換。Untracked は同じ読取との対照にする。[GraphTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/GraphTests.cs) の `PassiveReadSetsSeparateConstructionAndObservation` と [DependencyLifetimeTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/DependencyLifetimeTests.cs) の churn 後の集合を照合。
- 許容: 全件 `verify`、`screen`・`compare`・`diagnose`。環境: 管理。観測: 割当、集合が保持する依存。

### CORE-SIG-08 — 失敗・再試行・純粋性違反

- 経路・境界: [Computed](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/Computed.cs) の読取、[SignalEffect](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/SignalEffect.cs) の `Failure`。1 操作は例外 1 回と回復読取 1 回、または他 observer が動く配送 1 回。
- 入力: 代表／境界／負荷 = 計算例外／比較器例外・循環・評価中書込／旧新各 32 依存を持つ失敗枝。 復元・正しさ: 回復可能な比較器を戻す。失敗 observer を使い回して停止済み no-op を測らない。[PassiveCacheTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/PassiveCacheTests.cs) の同じ revision での再試行と [DependencyLifetimeTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/DependencyLifetimeTests.cs) の旧新依存保持を確認。
- 許容: 全件 `verify`、`diagnose`。環境: 管理。観測: 例外割当、失敗依存の保持。

### CORE-SIG-09 — 非同期の開始・完了・再ロード

- 経路・境界: [Signal.Async](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/Signal.Async.cs) の `FromAsync` → [AsyncSignal](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/AsyncSignal.cs) の `Completion` / `ReloadAsync`。1 操作は開始から終端 snapshot 観測まで 1 回。
- 入力: 代表／境界／負荷 = 同期完了 Task／制御ゲートで未完了・null 成功・失敗／64 回の逐次 reload。 復元・正しさ: 既知のゲートを開け、全 loader の実完了まで待つ。人工遅延を処理能力としない。[AsyncTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/AsyncTests.cs) の `CompletionPublishesOneConsistentSnapshotAsync` と失敗状態を確認。
- 許容: 全件 `verify`、`compare`・`diagnose`。環境: 管理。同期完了と非同期継続を分離。観測: 全スレッド割当、Task と状態の保持。

### CORE-SIG-10 — 非同期取消・陳腐化・解放

- 経路・境界: [AsyncSignal](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/AsyncSignal.cs) の `ReloadAsync` / `Cancel` / `Dispose`。1 操作は旧操作を pending にして取消または置換し、新旧の完了を回収する 1 周期。
- 入力: 代表／境界／負荷 = 新旧 2 操作／取消非協調・旧操作失敗・observer 配送失敗／上限 32 操作。 復元・正しさ: 最大未完了数を固定し、旧結果も必ず解放。scope 解放と単体解放は別ケース。[AsyncTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/AsyncTests.cs) の `ReloadRevokesOldCompletionAsync`、`CancelRevokesUncooperativeWorkAndAllowsReloadAsync`、`DisposalRevokesPublicationWithoutNotifyingAsync` を照合。
- 許容: 全件 `verify`、`compare`・`diagnose`。環境: 管理。観測: Task・CTS 割当と旧 factory の保持。

### CORE-SIG-11 — 共有グラフの並行アクセス

- 経路・境界: [Signal.Value](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/Signal.Value.cs) の `Value` と [SignalEffect](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Signals/SignalEffect.cs) の `Revision`。1 操作は固定個数の producer が各 64 回の変更代入を完了する 1 波。
- 入力: 代表／境界／負荷 = 2 producer／順次スレッド移動・observer なし／4 producer・16 observer。 復元・正しさ: 開始ゲートを固定し、同一値連続代入を避ける。グラフを波ごとに解放。[InvalidationTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Signals.Tests/InvalidationTests.cs) の `DifferentThreadsKeepDistinctStampsAsync` の stamp 契約を利用し、競合時の特定配送順は要求しない。
- 許容: 全件 `verify`、`compare`・`diagnose`。環境: 管理、実 worker 数を記録。観測: 全スレッド割当、競合。callback 自体は同期される。

## Resources と関連プロジェクトのワークロード群

### CORE-RES-01 — キー構築と型付き登録

- 経路・境界: [ResourceId](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceId.cs) / [ResourceKey](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceKey.cs) と [ResourceManager](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceManager.cs) constructor。1 操作はキー 1 個の構築、または manager 1 個の構築を別測定。
- 入力: 代表／境界／負荷 = 3 segment・4 Stage／不正論理名・重複出力・不適合 runtime／32 segment・32 Stage。 復元・正しさ: 事前生成文字列を使い、manager は解放。識別子生成と構成検証を混ぜない。[ResourceManagerTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceManagerTests.cs) の ordinal identity と [ResourceTypeIdentityTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceTypeIdentityTests.cs) の型識別を確認。
- 許容: 全件 `verify`、`compare`・`diagnose`（不正構成は `verify`）。環境: 管理。観測: 割当、登録表の保持。

### CORE-RES-02 — 初回構築とキャッシュ命中

- 経路・境界: [ResourceManager](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceManager.cs) の `RetainAsync<T>` → Source / Stage の `BuildAsync`。1 操作は未ロードキーの取得完了 1 回、または公開済みキーの Reference 取得返却 1 回。
- 入力: 代表／境界／負荷 = 4 KiB 入力・1 Stage／空内容・値型出力／1 MiB 入力・3 Stage。 復元・正しさ: miss は有限キー集合を使い各波で Trim。hit は事前 Reference で常駐させ、build 回数を確認。[ResourceManagerTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceManagerTests.cs) の多段構築と [ResourceValueTypeTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceValueTypeTests.cs) の型付き値を照合。
- 許容: 全件 `verify`、`screen`・`compare`・`diagnose`。環境: 管理、メモリ Source と決定的 Stage。観測: 割当、Build/Resident の計上。I/O は別系列。

### CORE-RES-03 — 依存グラフの構築と共有

- 経路・境界: [ResourceBuildContext](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceBuildContext.cs) の `RequireAsync` / `OpenSourceAsync`。1 操作はroot 1 個の構築と依存静止まで。
- 入力: 代表／境界／負荷 = 鎖 8・菱形 4 節点／Build→Resident 昇格・循環／鎖 64・共有葉 64 親。 復元・正しさ: 有限グラフを再生成。親出力に借用値を別所有出力として返さない。[ResourceManagerTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceManagerTests.cs) の `ResidentDiamondSharesLeafAndTrimsParentsFirstAsync`、昇格と循環拒否を照合。
- 許容: 全件 `verify`、`screen`・`compare`・`diagnose`（循環は `verify`・`diagnose`）。環境: 管理。観測: 割当、依存所有数と計上、回収後も残る再構築用 metadata。

### CORE-RES-04 — 同時取得の集約と取消

- 経路・境界: [ResourceManager](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceManager.cs) の `RetainAsync`。1 操作は同一キーまたは独立キーへの固定数要求を完了・回収する 1 波。
- 入力: 代表／境界／負荷 = 8 caller／1 caller の取消・build 失敗後再取得／64 caller・16 キー。 復元・正しさ: ゲートで重なりを保証し、取消 caller の背後の共有 build も実完了まで待つ。[ResourceManagerTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceManagerTests.cs) の `ConcurrentAcquireSharesBuildAndIsolatesCallerCancellationAsync` と [ResourceConcurrencyTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceConcurrencyTests.cs) の Trim 中再取得を照合。
- 許容: 全件 `verify`、`compare`・`diagnose`。環境: 管理、固定 executor 構成。観測: 全スレッド割当、共有 load と失敗 metadata の保持。

### CORE-RES-05 — Reference・Lease・Snapshot の定常経路

- 経路・境界: [ResourceManager](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceManager.cs) の `TryRetain` / `CaptureSnapshot` → [ResourceReference](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceReference.cs) の `Pin` → [ResourceLease](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceLease.cs) の `Value` / `Dispose`。1 操作は所有権増減 1 対、Value 読取 1 回、または snapshot 取得返却 1 対。
- 入力: 代表／境界／負荷 = 32 handle／期限切れ・別 manager・snapshot にない handle／1024 handle。 復元・正しさ: hit は実 Reference を維持。失効系は毎回新規 slot 周期を作る。Lease 読取中は返却しない。[ResourceManagerTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceManagerTests.cs) の ownership・foreign owner 拒否と [ResourceConcurrencyTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceConcurrencyTests.cs) の `LeaseReadsPreservePublishedValueAsync` を照合。
- 許容: 全件 `verify`、`screen`・`compare`・`diagnose`。環境: 管理。観測: 割当、世代保持。Pin は GC pinning ではない。

### CORE-RES-06 — 変更通知から再構築・一括公開

- 経路・境界: [IResourceChangeSource](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/IResourceChangeSource.cs) → [ResourceReloadService](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceReloadService.cs) → `Reloaded` / `ReloadFailed`。1 操作は制御 Source の revision 変更と通知から対応する公開イベントまで 1 バッチ。
- 入力: 代表／境界／負荷 = 32 節点中 1 葉変更／無変更・重複・未追跡 ID・Rescan／256 節点中 16 葉変更。 復元・正しさ: 通常変更は revision を進める。重複抑止は独立ケース。旧 Lease は上限 2 世代だけ保持し波ごとに返す。[ResourceHotReloadTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceHotReloadTests.cs) と [ResourceManagerTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceManagerTests.cs) の選択的再構築・一括公開を照合。内部 `ReloadAsync` や test helper は使わない。
- 許容: 全件 `verify`、`compare`・`diagnose`。環境: 管理、集約待ち込み。Stage の純粋時間と混ぜない。観測: 全スレッド割当、旧世代保持、通知上限。

### CORE-RES-07 — 再構築の conflict・失敗・監視集合

- 経路・境界: [ResourceReloadService](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceReloadService.cs) と [ResourceManager.Reload](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceManager.Reload.cs)（公開通知で到達）。1 操作は構築途中 revision 変更または失敗を与え、結果イベントと候補 cleanup まで 1 回。
- 入力: 代表／境界／負荷 = 1 競合／回収済み Build 依存・通知上限超過・有界再試行／128 追跡 ID・32 通知。 復元・正しさ: ゲートで conflict を再現し、再試行数を固定。候補と旧 map を毎回回収。[ResourceWatchTargetsTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceWatchTargetsTests.cs)、[ResourceWatchSharingTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceWatchSharingTests.cs)、[ResourceManagerTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceManagerTests.cs) の旧公開保持を oracle にする。
- 許容: 全件 `verify`、`diagnose`。環境: 管理、実 watcher の OS 遅延は別統合。観測: 割当、metadata・監視集合の保持。

### CORE-RES-08 — runtime 世代の検証・失効・復旧

- 経路・境界: [ResourceBuildContext](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceBuildContext.cs) の `TrackRuntimeDependency` → [ResourceManager.Rebuild](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceManager.Rebuild.cs) の `RebuildRuntimeAsync`。1 操作はprovider 世代変更から利用不能確認と再構築完了まで 1 周期。
- 入力: 代表／境界／負荷 = 1 provider・8 親／Build 寿命と Resident 寿命・2 provider・世代だけ前進／64 親。 復元・正しさ: 制御 provider の identity/version を更新し、元世代 Lease の返却も待つ。実 device loss と同一視しない。[ResourceEnvironmentTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceEnvironmentTests.cs) と [ResourceInvalidationLifetimeTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceInvalidationLifetimeTests.cs) の失効範囲、旧 lease の可用性を照合。
- 許容: 全件 `verify`、`compare`・`diagnose`。環境: 管理、制御 provider。実 GPU/Audio は消費側統合。観測: 割当、世代 Lease・依存保持、計上。

### CORE-RES-09 — 解放・Trim・cleanup 再試行

- 経路・境界: [ResourceManager.Lifetime](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceManager.Lifetime.cs) の `TrimAsync` / `ReclaimAsync` / `DisposeAsync` と [ResourceBuildContext](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceBuildContext.cs) の `RegisterCleanup` / `GetMemoryScope`。1 操作は有限未使用グラフの Trim 1 回、または失敗 cleanup の再試行 1 回。
- 入力: 代表／境界／負荷 = 32 出力・各 4 KiB／空 Trim・保持 snapshot・解放失敗・取消／1024 出力・各 4 KiB。 復元・正しさ: 毎回実対象を積む。空 Trim は明示対照。成功 cleanup は再実行しない。外部所有を返してから manager 終了。[ResourcePerformanceLifetimeTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourcePerformanceLifetimeTests.cs) の cleanup 順序・保持 payload・取消後再開と [ResourceAcquisitionLifetimeTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceAcquisitionLifetimeTests.cs) を確認。
- 許容: 全件 `verify`、`screen`・`compare`・`diagnose`（失敗再試行は `compare`・`diagnose`）。環境: 管理、固定 Memory pool。観測: 割当、保持、計上と物理を別記。Reclaim の返却値は slice サイズ総和ではない。

### CORE-RES-10 — stream の生成・先読み・定常消費

- 経路・境界: [ResourceManager](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceManager.cs) の `OpenStreamAsync` → [ResourceStreamSession](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceStreamSession.cs) の `ReadAvailable` / `ReadAsync`。1 操作はsession 開閉 1 回、準備済み block 消費 1 回、または固定長全読取 1 回を分ける。
- 入力: 代表／境界／負荷 = 4096 int・block 256×2／空・短読取・underflow・block 1×1／65536 int・block 1024×3。 復元・正しさ: 有限入力・固定受信 buffer。読取系列を使い切り EOF no-op を繰り返さない。checksum と件数のみ蓄積。[ResourceStreamTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceStreamTests.cs) の順序・独立 cursor・有界先読み・固定 budget を oracle にする。
- 許容: 全件 `verify`、`screen`・`compare`・`diagnose`。環境: 管理、制御 reader。実行器の種類は別ラベル。観測: 割当、buffer 計上、物理保持。ReadAvailable 内で入力を読まない。

### CORE-RES-11 — stream の seek・取消・故障・世代寿命

- 経路・境界: [ResourceStreamSession](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceStreamSession.cs) の `SeekAsync` / `DisposeAsync` と `ReadAsync` の取消。1 操作はseek と次の有効読取、または待機取消から解放まで 1 周期。
- 入力: 代表／境界／負荷 = 4096 要素で前後 seek／非対応・seek 失敗・満杯解放・解放再試行／8 session・上限 2 factory 世代。 復元・正しさ: consumer は session ごとに一つ。read/seek/dispose を重ねない。reload 後は旧新 session を個別に閉じる。[ResourceStreamTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceStreamTests.cs) の `ReadCancellationOnlyCancelsItsWaitAsync`、`FailedSeekFaultsSessionAsync`、旧 factory 保持を確認。
- 許容: 全件 `verify`、`compare`・`diagnose`（故障経路は `verify`・`diagnose`）。環境: 管理、制御 reader。観測: 全スレッド割当、session buffer・factory 世代保持。

### CORE-RES-12 — DI 登録・初回解決・所有権

- 経路・境界: [ResourceServiceCollectionExtensions](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources.DependencyInjection/ResourceServiceCollectionExtensions.cs) の `AddResources` と [ResourceRegistrationBuilder](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources.DependencyInjection/ResourceRegistrationBuilder.cs) の `UseSource` / `AddSourcePipeline` / `AddTransformPipeline` / `UseStageRuntime`。1 操作は登録一式、provider の初回 manager 解決、または子 scope の解決 1 回を分離。
- 入力: 代表／境界／負荷 = 4 Pipeline／重複・late 登録・scoped Source・runtime 不一致／32 Pipeline。 復元・正しさ: provider はケースごとに作る。起動時間は含めず、manager 解放成功後に root を閉じる。[ResourceManagerTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceManagerTests.cs) の DI root 内共有と [ResourceRuntimeTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceRuntimeTests.cs) の独立 host 分離を照合。AOT 可否は [AotSmoke](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.AotSmoke/Program.cs) の確認系列とする。
- 許容: 全件 `verify`、`compare`・`diagnose`（不正登録・AOT は `verify`）。環境: 管理。JIT と AOT を分離。観測: 割当、singleton 所有と root 解放後保持。

### CORE-RES-13 — Embedded の名前解決・snapshot・範囲読取

- 経路・境界: [EmbeddedResourceSource](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources.Embedded/EmbeddedResourceSource.cs) の constructor / `GetRevisionAsync` / `OpenSnapshotAsync` → [EmbeddedResourceSnapshot](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources.Embedded/EmbeddedResourceSnapshot.cs) の `OpenReadAsync`。1 操作はalias 辞書構築 1 回、revision 問合せ 1 回、または範囲 stream 開始から固定量読取・解放 1 回。
- 入力: 代表／境界／負荷 = 64 alias・64 KiB データ・4 KiB 範囲／空・欠落・末尾・取消／1024 alias・4 MiB データ・64 KiB 範囲。 復元・正しさ: assembly は事前ロード。各 reader を閉じ cursor を復元。全データ ToArray や一時ファイルを導入しない。[EmbeddedResourceSourceTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/EmbeddedResourceSourceTests.cs) の独立 cursor・alias コピー・snapshot 解放後 reader 生存・collectible assembly 保持を照合。
- 許容: 全件 `verify`、`screen`・`compare`・`diagnose`。環境: 埋込。DLL ロード自体と OS ファイルキャッシュはこの時間から除く。観測: managed 割当と assembly 保持。DLL 常駐を Source の native 確保と重複計上しない。

### CORE-RES-14 — Embedded の Pipeline 統合と入力媒体の分離

- 経路・境界: [ResourceBuildContext](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources/ResourceBuildContext.cs) の `OpenSourceAsync` → `EmbeddedResourceSource` → typed Stage。1 操作は埋込入力を取得して checksum 出力を構築し返す 1 回。
- 入力: 代表／境界／負荷 = 64 KiB／0 byte・独立 reader 2 個／4 MiB。 復元・正しさ: miss を保証する有限キーと Trim。Stage の decode buffer は memory scope へ計上。[EmbeddedResourceSourceTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/EmbeddedResourceSourceTests.cs) の `PipelineUsesEmbeddedSourceThroughDependencyInjectionAsync` を照合。実ファイルは FileSystem 担当系列で同じ Stage と内容を用いる。
- 許容: 全件 `verify`、`compare`・`diagnose`。環境: 埋込＋管理。実ファイルの metadata・watch・I/O は別環境・別結果。観測: Source は全入力 buffer を持たないことを保持観測で確認し、Stage の割当・計上と分離。

### CORE-RES-15 — Execution への構築・解放・stream 配送

- 経路・境界: [ResourceRuntime](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources.Execution/ResourceRuntime.cs) の `BuildAsync` / `ReleaseAsync` / `StreamAsync` と manager の取得・Trim。1 操作は実 workflow を経由する root 取得解放 1 周期、または session 開閉 1 回。
- 入力: 代表／境界／負荷 = 同一 runtime 鎖 4／runtime A→B→A・CPU 枠 1 で未完了 I/O／鎖 64・8 独立要求。 復元・正しさ: Prepare/Start は定常区間外。ThreadPool worker 2 と Host/Cooperative を分け、後者は待機中も Pump。全 session・manager 完了後 Stop。[ResourceRuntimeTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceRuntimeTests.cs) の Pump・I/O 中の CPU 枠返却と [ResourcePipelineJobTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourcePipelineJobTests.cs) / [ResourceStreamJobTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceStreamJobTests.cs) の実行経路を照合。
- 許容: 全件 `verify`、`compare`・`diagnose`。環境: 実行。配置・CPU mode・MaxInFlightRuns・Pump 予算を固定。観測: 全スレッド割当、workflow 受付枠と buffer 計上。

### CORE-RES-16 — Execution 容量不足と停止境界

- 経路・境界: [ResourceRuntime](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Resources.Execution/ResourceRuntime.cs) を使う `RetainAsync` / `TrimAsync`。1 操作は受付枠を既知の pending 処理で占有し、拒否と回復・回収まで 1 周期。
- 入力: 代表／境界／負荷 = 容量 2／起動前・停止後・queue full 解放／容量 16・同数 session。 復元・正しさ: ゲートを必ず解除し実完了を join。拒否された処理を再投入し no-op 回復にしない。[ResourceRuntimeTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceRuntimeTests.cs) の `QueueFullReleaseRetainsTheOutputUntilCapacityIsAvailable` と [ResourceStreamJobTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Resources.Tests/ResourceStreamJobTests.cs) を参照し、所有権が失われないことを確認。
- 許容: 全件 `verify`、`diagnose`。環境: 実行、実 device なし。観測: 例外割当、未回収結果・解放待ち所有。

## Settings のワークロード群

### CORE-SET-01 — 登録・定義構築・初期化

- 経路・境界: [PersistedOptionsExtensions](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/PersistedOptionsExtensions.cs) の `AddSettings` / `AddPersistedOptions` / `UseJsonTypeInfo` → `ISettingsDocument.ValidateRegisteredSettings`。1 操作は登録一式と初回解決、または全登録検証 1 回を分離。
- 入力: 代表／境界／負荷 = 4 module／0 module・未参照 module・対応外モデル／32 module。 復元・正しさ: provider と source を毎回再生成。型情報準備・標準生成器実行は計時外。[SettingsTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Settings.Tests/SettingsTests.cs) の hostless 起動・Host 自動検証と [RootSettingsTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Settings.Tests/RootSettingsTests.cs) の collection root 拒否を照合。
- 許容: 全件 `verify`、`compare`・`diagnose`（不正登録は `verify`）。環境: 管理、明示 JsonTypeInfo。既存 reflection fallback を測る場合は JIT 専用の別系列。観測: 割当、metadata キャッシュ保持。

### CORE-SET-02 — 深いコピー・snapshot・編集開始

- 経路・境界: [JsonSettingsDefinition](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/JsonSettingsDefinition.cs) の `DeepClone`（登録済み `ISettingsDefinition<T>` の公開契約を通す）と [IEditableOptions](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/IEditableOptions.cs) の `Current` / `BeginEdit` / `Revision`。1 操作はclone 1 回、snapshot 1 回、編集開始 1 回、revision 読取 1 回を分離。
- 入力: 代表／境界／負荷 = scalar 16・list 32／null・NaN・共有参照・辞書 comparer／1024 要素・深さ 16。 復元・正しさ: 編集と snapshot を保存せず破棄。内部値とは独立、同じコピー内の共有参照は保持する。[SettingsTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Settings.Tests/SettingsTests.cs) の `MetadataCopyPreservesInvalidValuesAndSeparatesCollectionsAsync` と comparer 保持を確認。
- 許容: 全件 `verify`、`screen`・`compare`・`diagnose`。環境: 管理。観測: 割当、コピーの保持。JSON roundtrip を clone の代用品にしない。

### CORE-SET-03 — 既定値への overlay と読み込み

- 経路・境界: [PersistedSettingsSource](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/PersistedSettingsSource.cs) の `LoadAsync` → [SettingsState](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/SettingsState.cs) 初期化 → [SettingsModelOverlay](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/SettingsModelOverlay.cs) の `Read`。1 操作はメモリ store の文書を読み込み module を初期化する 1 回。
- 入力: 代表／境界／負荷 = 8 KiB・半数キー保存／欠落・明示 0/false/null・空辞書・未知プロパティ／256 KiB・深さ 16。 復元・正しさ: 同じ初期 JSON から provider を作る。初期化済み Current のコピーと混同しない。[SettingsTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Settings.Tests/SettingsTests.cs) の deleted dictionary・未知項目・辞書 comparer、独自定義の正しさを確認。
- 許容: 全件 `verify`、`screen`・`compare`・`diagnose`。環境: 管理、メモリ store。観測: 割当、JsonNode と確定モデル保持。callback を持つ JSON 型情報の merge 分岐は独自定義で到達可能な場合だけ独立プロファイル。

### CORE-SET-04 — 形式移行と破損の保護

- 経路・境界: [ISettingsDefinition](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/ISettingsDefinition.cs) の `Upgrade` と `IEditableOptions.LoadResult`。1 操作は旧版文書の初期化、または保護判定 1 回。
- 入力: 代表／境界／負荷 = 旧版 1→2・8 KiB／現在版・将来版・移行なし・重複 JSON key・文書破損／256 KiB。 復元・正しさ: 初期 byte 列を毎回復元。移行は保存しない限り store を変えない。[SettingsTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Settings.Tests/SettingsTests.cs) の `MetadataMigrationRunsOnlyForOlderVersionsAsync`、`MissingMetadataMigrationProtectsSavedValuesAsync`、文書保護を照合。
- 許容: 全件 `verify`、`compare`・`diagnose`（破損は `verify`・`diagnose`）。環境: 管理、メモリ store。観測: 割当、移行 JsonObject と保護中の原文保持。

### CORE-SET-05 — 正常保存・往復検証・文書置換

- 経路・境界: [IEditableOptions](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/IEditableOptions.cs) の `SaveAsync` → [SettingsState](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/SettingsState.cs) の候補コピー・検証・再シリアライズ → `ISettingsStore.WriteAtomicallyAsync`。1 操作は有効 edit 1 個の保存完了まで。
- 入力: 代表／境界／負荷 = 4 module・8 KiB／明示既定値・未知 section 保持・空 collection／32 module・256 KiB。 復元・正しさ: 毎回 BeginEdit して値を変える。store は最新 byte 列 1 本だけ保持し履歴を貯めない。[SettingsTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Settings.Tests/SettingsTests.cs) の `ConcurrentModulesPreserveOtherSectionsAndDetectStaleEditsAsync` と `NormalizedDefaultsCanBeSavedAndReloadedAsync` に従い再ロード値を照合。
- 許容: 全件 `verify`、`screen`・`compare`・`diagnose`。環境: 管理、原子的契約を満たすメモリ store。観測: 割当、候補・文書・保存結果の保持。ディスク時間は含めない。

### CORE-SET-06 — 検証失敗と保存不可の値

- 経路・境界: [IEditableOptions](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/IEditableOptions.cs) の `SaveAsync` と Options `PostConfigure` / `IValidateOptions<T>`。1 操作は不正候補を保存要求し結果受領まで 1 回。
- 入力: 代表／境界／負荷 = validator 4 個／NaN・null・往復不一致・外側構造込み深度 64 付近／validator 32 個・深さ超過 65 の文書。 復元・正しさ: 各回最新 edit を使う。深度はモデル深度だけでなく envelope を含め実 JSON で確認。[SettingsTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Settings.Tests/SettingsTests.cs) の `InvalidCandidatesAreDiagnosedBeforeSerializationAsync`、`UnreadableSerializerOutputIsRejectedBeforeWritingAsync`、深度拒否で store 未変更を照合。
- 許容: 全件 `verify`、`compare`・`diagnose`。環境: 管理、メモリ store。観測: エラー割当、書込回数 0・revision 不変。

### CORE-SET-07 — 編集競合・複数 module・書込直列化

- 経路・境界: [IEditableOptions](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/IEditableOptions.cs) の `BeginEdit` / `SaveAsync` → [SettingsDocument](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/SettingsDocument.cs) の書込直列化。1 操作は2 edit の保存競争、または固定数 module の並行保存が完了する 1 波。
- 入力: 代表／境界／負荷 = 2 同一 module／異なる 2 module・待機取消・別 service の edit／8 module。 復元・正しさ: 開始ゲートと store 完了ゲートを制御し全要求を回収。古い edit を作る操作と拒否のみを別区間にする。[SettingsTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Settings.Tests/SettingsTests.cs) の stale edit・別 module 非競合と `CandidateIsCapturedBeforeAwaitAndCommitIgnoresLateCancellationAsync` を照合。
- 許容: 全件 `verify`、`compare`・`diagnose`（別 service edit 拒否は `verify`）。環境: 管理、遅延を制御できるメモリ store。観測: 全スレッド割当、待機候補の保持。人工待機と処理時間を分ける。

### CORE-SET-08 — module・文書リセットと障害回復

- 経路・境界: [IEditableOptions](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/IEditableOptions.cs) の `ResetAsync(revision)` と [ISettingsDocument](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/ISettingsDocument.cs) の `ResetAsync`。1 操作は変更済みまたは保護中の設定をリセットし store 確定まで 1 回。
- 入力: 代表／境界／負荷 = 1 module／文書破損・不正既定値・storage failure・commit 前取消／32 module 文書。 復元・正しさ: 毎回変更済み状態または同じ破損文書へ戻す。全文リセットは専用 fixture のみで未登録 section の削除も確認。[SettingsTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Settings.Tests/SettingsTests.cs) の `DocumentCorruptionProtectsWritesUntilExplicitRecoveryAsync` と reset 検証失敗・revision 維持を照合。
- 許容: 全件 `verify`、`compare`・`diagnose`（障害注入は `verify`・`diagnose`）。環境: 管理、メモリ store。観測: 割当、確定前候補と失敗後の旧値保持。

### CORE-SET-09 — 原子的ディスク読み書きの統合

- 経路・境界: [JsonFileSettingsStore](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/JsonFileSettingsStore.cs) の `ReadAsync` / `WriteAtomicallyAsync`、[PersistedJsonFileSource](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/PersistedJsonFileSource.cs)。1 操作は同一 directory の一時ファイル書込・flush・rename 完了 1 回、または読込 1 回。SaveAsync 全体は別ケース。
- 入力: 代表／境界／負荷 = 8 KiB／不存在・初回 directory 作成・commit 前取消／1 MiB。 復元・正しさ: 専用 directory と同一 target を使い、各回内容を交替。後処理で tmp 残存と再読込を確認し履歴を増やさない。[SettingsTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Settings.Tests/SettingsTests.cs) の `FileSourceLoadsDuringRegistrationAndSavesForRestartAsync` を再起動 oracle とし、元ファイル維持と commit 後成功を確認。
- 許容: 全件 `verify`、`compare`・`diagnose`。環境: ディスク。OS cache が温かい読込と新規プロセス読込を別ラベルにし、後者を物理 cold と断定しない。観測: managed 割当、byte 数、OS I/O。flush を耐障害性の実証としない。

### CORE-SET-10 — 観測購読ありの保存・スコープ

- 経路・境界: [SettingsTelemetry](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Settings/SettingsTelemetry.cs) の `BeginScope` と通常の保存経路。1 操作はscope 開閉と有効保存 1 回。
- 入力: 代表／境界／負荷 = 購読なし／Meter・Activity・ILogger の各単独購読／最大 3 購読の同時使用。 復元・正しさ: sink は有界集計のみとし JSON・値・パスを記録しない。購読はサービス初回解決前に開始。[SettingsTelemetryTests](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Settings.Tests/SettingsTelemetryTests.cs) の操作数・scope 復元と値参照が計測を増やさない契約を照合。
- 許容: 全件 `verify`、`compare`・`diagnose`。環境: 管理、メモリ store。trace 採取の diagnose は時間比較と別実行。観測: 割当、sink 保持。ログ転送・ネットワークは対象外。

## 既存ワークロードの再利用対応

既存の `benchmarks/Core/Lumyte.Signals.Benchmark` は移動・複製せず、次の操作本体、有限入力、期待値を family へ対応付けて再利用する。未対応の経路だけを追加候補にする。

| 固定ソース | 再利用する操作 | 対応群 |
| --- | --- | --- |
| [ScalarBenchmarks](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Signals.Benchmark/ScalarBenchmarks.cs) | `ReadValue` / `ReadPeek` / `WriteSame` / `WriteUnobserved` | CORE-SIG-01 |
| [ComputedBenchmarks](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Signals.Benchmark/ComputedBenchmarks.cs) | `ReadObservedCached` / `ReadUnobservedCached` / `UpdateObserved` / `UpdateAndReadUnobserved` | CORE-SIG-02 |
| [SharedDagBenchmarks](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Signals.Benchmark/SharedDagBenchmarks.cs) | `ReadObservedCached` / `ReadUnobservedCached` | CORE-SIG-02 |
| [ValidationBenchmarks](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Signals.Benchmark/ValidationBenchmarks.cs) | `ReadAfterUnrelatedWrite` / `UpdateAndRead` | CORE-SIG-02 |
| [GraphBenchmarks](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Signals.Benchmark/GraphBenchmarks.cs) | `DiamondUpdate` / `SwitchBranch` / `CaptureEightReads` / `CheckReadSet` | CORE-SIG-04 / 03 / 07 |
| [BatchBenchmarks](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Signals.Benchmark/BatchBenchmarks.cs) | `Unbatched16` / `Batched16` | CORE-SIG-04 |
| [FanoutBenchmarks](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Signals.Benchmark/FanoutBenchmarks.cs) | `Update` | CORE-SIG-04 |
| [ReuseBenchmarks](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Signals.Benchmark/ReuseBenchmarks.cs) | `ReevaluateStable` / `CreateAndDisposeEffect` | CORE-SIG-02 / 05 |
| [SchedulerBenchmarks](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Signals.Benchmark/SchedulerBenchmarks.cs) | `Coalesced16` | CORE-SIG-06 |
| [AsyncBenchmarks](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Signals.Benchmark/AsyncBenchmarks.cs) | `CreateCompleted` / `ReloadCompleted` / `ReloadCompletedObserved` / `SupersedeAndCancelPending` | CORE-SIG-09 / 10 |
| [RetentionProbe](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Signals.Benchmark/RetentionProbe.cs) | `Run` / `Install` / `Release` | CORE-SIG-05（verify・diagnose 専用） |

[既存 Program](https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/benchmarks/Core/Lumyte.Signals.Benchmark/Program.cs) の normal/screening 起動設定、smoke の期待値、保持 probe の出力は別々の責務として扱う。warmup・反復・launch 数をそのまま新モードへ固定せず、保持 probe の手動 GC は verify・diagnose にのみ残す。旧 stopwatch、手動 GC、JIT 上書きがある再利用元では操作本体から起動・測定制御を切り離す。履歴の測定値や Markdown 結果は入力データでも性能基準でもない。
Resources・Settings はこの基準の Core benchmark 群に既存専用プロジェクトを持たず、上記テストの fixture と oracle を公開経路へ抽出する候補である。Resources.Execution は実 runtime を使う fixture が必要で、Demo 依存の sample をそのまま benchmark と呼ばない。UI・Graphics の tools が Core を参照していても、その workload を Core へ自動移管しない。

## モードと頻度を選ぶ際の暫定候補

全群を見た後の候補として、短く決定的な `CORE-SIG-01/02/04/07`、`CORE-RES-02/05/09/10/13`、`CORE-SET-02/03/05` の代表プロファイルを `screen` で検討する。これは採用リストでも PR 必須ジョブでもない。
`compare` は同じ family・入力・環境・境界を揃えて独立起動する。並行性、通知待ち、実行器、実ディスクの系列は短時間判定へ無理に圧縮しない。失敗・取消・保持観測はまず `verify` で契約を満たし、必要時に `diagnose` で原因を追う。
PR・定期・手動のどこへ置くかは、全 Core カタログ、実行時間の上限、環境の再現性を確認してから決める。既存 Signals の normal/screening と本設計のモード名を同一視しない。
