# 実行・構築・状態・ワールドのベンチマーク設計

- 状態: 提案（設計のみ。本カタログ全体の実装や測定結果は追加しない。既存sampleとFalloutは基準コードに存在する）
- 基準コミット: `05d391955a607e7fb948812c9660dc2bed32704d`
- 共通契約: [分類・モードの共通 ADR](../../adr/core/CORE-0005-benchmark-catalog-and-modes.md)

## 読者と対象範囲

Core の変更を評価する担当者が、短時間実行に適する処理だけでなく、構築、通常実行、拒否、再構築、寿命、並行実行、コンパイル時処理の意味ある経路から測定対象を選ぶためのカタログである。
以前の運用を置き換える実装ではなく、既存 API の呼び出しを保ったまま測定境界と正しさの判定を追加する設計であり、公開 API の追加・変更は要求しない。
全 API の別名や入力値の直積ではなく、意味や資源の保持が異なる処理群を列挙してからモードを割り当てる。

| プロジェクト | 位置付け | 対象 |
| --- | --- | --- |
| Lumyte.Execution | 独立測定 | 受付、配送、batch、graph、workflow、更新、取消、寿命 |
| Lumyte.Execution.Generators | コンパイル時のみ | Roslyn driver による生成と診断。生成 sender の実行は Execution の利用側統合 |
| Lumyte.Composition | 独立測定と利用側統合 | slot と adapter は独立、生成 factory・内容初期化は小さな独立 host と統合 |
| Lumyte.Composition.Generators | コンパイル時のみ | Roslyn driver による class・function・adapter・slot 生成と診断 |
| Lumyte.StateMachines | 独立測定と利用側統合 | 定義と実行者、ComposeStateMachines の構築統合 |
| Lumyte.Worlds | 独立測定 | CPU 上のフレーム境界と計数 Scene。GPU は不要 |

## 入力、境界、観測の読み方

以下は全分類を先に定義したカタログであり、CI の採用一覧ではない。頻度（PR・定期・手動）、環境、冷間・温間、入力プロファイルはモードから独立させる。
各項目の「代表／境界／負荷」は選択する有限プロファイルであり、組合せを全展開しない。負荷上限は初期設計値で、実装時に実行予算内で確認する。
許可モードの記号は V=verify、S=screen、C=compare、D=diagnose。V は正しさの確認のみ、S は候補検出、C は独立起動の正式比較、D は時間比較と分離した診断である。
環境の「管理実行」は .NET 管理実行環境、「専用機」は物理 CPU・スレッド条件を固定した環境、「生成」は SDK・Roslyn・参照 assembly を固定した環境を表す。
管理実行の単一スレッドケースでも環境差は記録する。Browser は実行方式の別プロファイルとし、Cooperative/Host の互換経路だけを候補にする。
将来のコード配置は `benchmarks/Core/<target>.Benchmark`、統括は Fallout。Python、reflection、実在しない内部可視化経路、新しい公開観測 API を前提にしない。
各「1操作」は計測境界であり、検証ログの生成や照合は外側で行う。単なる空 callback ではなく結果の checksum・呼出回数を消費する。
例外系の期待例外は外側で照合し、通常系の集計に混ぜない。反復の履歴は固定長カウンターか検証用の有限バッファに制限する。
割当は管理ヒープの新規割当、保持は結果・配列・driver など生存参照の量として区別する。ネイティブ資源や thread/OS 資源は管理割当に含めず D で別観測する。
正しさの根拠は固定 SHA のテストであり、テストの待機時間やタイムアウトを性能保証・判定閾値として流用しない。

## 実行基盤の処理群

この節の通常系は V/S/C/D、例外・競合の順序検証だけの枝は V/D を許可する。専用 thread と ThreadPool の正式比較は専用機に限定する。
ExecutionRuntime を作り直す際は fresh な endpoint と module を使う。静的 endpoint の停止後再接続はできないため、生成 sender の冷間ケースは独立プロセス等で分離する。

- **CORE-EXEC-01 モジュール収集と実行計画** — 経路: [ExecutionRuntime の constructor・PrepareOnCurrentHost][exec-runtime]、[ModuleBuilder][exec-module] と [ExecutionPlanBuilder][exec-plan]。1操作は新規 module 群の収集と Prepare。Start は含めない。
  - 入力: 代表 4 module・各 4 command、境界 1 module・依存なし、負荷 32 module・各 8 command と鎖状依存。重複名、依存不備、endpoint 衝突は個別の V/D 枝。
  - リセット・判定: 毎回新規 runtime/module/endpoint、起動・副作用なしと依存順を [ModuleCollectionTests][test-modules] と [RuntimeTests][test-runtime] に照合。計画の管理割当と保持を区別。環境: 管理実行。
- **CORE-EXEC-02 単発 command の受付と完了** — 経路: [CommandPort.Enqueue・ExecuteAsync][exec-port] → PumpJobs または RunHostPhase。受付のみの1操作と、投入から1回の結果観測までの1操作を別ケースにする。
  - 入力: 代表 int payload/result・CPU/Host を別選択、境界同一 thread への遅延配送、負荷 256件の有界窓。生成 sender も同じ仕事の利用側統合枝として別記録する。
  - リセット・判定: 窓を完全排出し ValueTask を1回だけ観測、checksum・FIFO・未 Pump 時の副作用なしを [RuntimeTests][test-runtime] に照合。受付後に保持する completion と新規割当を分離。環境: 管理実行。
- **CORE-EXEC-03 受付拒否と結果による容量保持** — 経路: [CommandPort.Enqueue][exec-port] と [ExecutionRuntime][exec-runtime] の容量管理。1操作は満杯・停止中・事前取消での受付1回、または完了結果の観測による次の受付再開1回。
  - 入力: 代表 MaxCommands=8、境界 1枠、負荷 256枠。MaxInFlightRuns・MaxPayloadBytes の制約は独立プロファイルとし同時に飽和させない。
  - リセット・判定: 完了未観測結果を正確に枠数だけ保持し最後に全回収。status・null out・副作用抑止を [AdmissionApiTests][test-admission] に照合。V/S/C/D、管理実行。保持量は割当速度と別評価。
- **CORE-EXEC-04 gate 待機と解除・失敗** — 経路: [GateControl][exec-gate] の Open/Fail と command 配送。1操作は固定個数の待機仕事の解除から完了まで。閉鎖 gate への投入のみとは分離する。
  - 入力: 代表 8件、境界 1件、負荷 256件。独立した実行可能 group と gate 待機 group の共存を1プロファイルとする。
  - リセット・判定: fresh な gate 状態を用意、結果全回収。待機中の副作用なし、開放後の順序、失敗伝播を [RuntimeTests][test-runtime] に照合。失敗枝 V/D、通常解除 V/S/C/D、管理実行または専用機。通知登録の保持も見る。
- **CORE-EXEC-05 batch の staging・受理・再利用** — 経路: [CommandBatch.Add・Reset][exec-batch] → [ExecutionRuntime.Submit][exec-submit] → CommandRun.GetResult/Dispose。1操作は staging から結果回収・Reset までの1 batch。受理のみも別境界にする。
  - 入力: 代表異型 8件を同じ owner/group、境界 1件、負荷 256件。CPU batch は別プロファイル、容量不足の全件拒否も分離する。
  - リセット・判定: run.Dispose → batch.Reset の順を守り値を変えて再投入。[BatchTests][test-batch] の全件原子受理、FIFO、型付き結果、staging 維持、旧 key 無効を照合。通常 V/S/C/D、誤世代・失敗停止 V/D。管理実行、staging 保持と run 割当を分離。
- **CORE-EXEC-06 型付き graph の構築・検証** — 経路: [JobGraphBuilder.Add・DependsOn・Build][exec-graph-builder]。1操作は新規 builder から不変 `JobGraph<TContext>` を作るまで。Schedule は外側。
  - 入力: 代表 16 node の鎖と扇状を別選択、境界 1 node、負荷 256 node・最大 1024 edge。循環・重複など実装が拒否する定義は個別の V/D 枝。
  - リセット・判定: 毎回新規 builder、完成 graph の実行結果を別途照合し [GraphTests][test-graph] の定義拒否を確認。通常 V/S/C/D、管理実行。node/edge の管理割当と不変 graph 保持量を観測。
- **CORE-EXEC-07 graph の配送と依存伝播** — 経路: [ExecutionRuntime.Schedule][exec-schedule] → PumpJobs/RunHostPhase → JobRun の完了・Dispose。1操作は準備済み graph の1実行。構築を含めない。
  - 入力: 代表 16 node の CPU→Host、境界独立1 node、負荷 256 node・1024 edge の有限 DAG。owner gate 待機と独立 CPU node の共存も分離。
  - リセット・判定: context buffer を上書きし run を必ず Dispose。依存順・失敗後の従属抑止・owner 待機が CPU 枠を占有しないことを [GraphTests][test-graph] に照合。通常 V/S/C/D、失敗 V/D、管理実行／専用機。完了 run の容量保持は独立枝。
- **CORE-EXEC-08 range の chunk 取得と再投入** — 経路: [JobGraphBuilder.AddRange][exec-graph-builder] と [GraphRangeWorker][exec-range] を公開 Schedule から通す。1操作は有限 range 全体の完了まで。
  - 入力: 代表 4096要素・chunk64、境界長さ0/1/65を別選択、負荷 65536要素・chunk256。同時数1/4を別プロファイルとする。
  - リセット・判定: 固定長 buffer と要素別 visit count を外側で初期化し、各要素がちょうど1回、長さ評価が受付前1回を [GraphTests][test-graph] に照合。V/S/C/D、Cooperative は管理実行、ThreadPool は専用機。worker/予約管理量と payload 参照先 buffer は別観測。
- **CORE-EXEC-09 workflow の同期入口と再開** — 経路: [WorkflowPort.Enqueue・ExecuteAsync][exec-workflow-port] → [ExecutionRuntime.Workflows][exec-workflows]。1操作は結果観測までの1 workflow、同期完了・制御可能な非同期1回待機を別ケースにする。
  - 入力: 代表1回待機、境界同期完了、負荷16回の有限再開。外部ネットワークでなく、計測者が解放する有限 completion を使う。
  - リセット・判定: 各回 fresh な完了源、通常 await で再開し結果を1回だけ読む。入口・継続の Pump 必須、待機中の CPU 枠解放を [WorkflowTests][test-workflow] に照合。V/S/C/D、管理実行／専用機。callback 管理量と handler 独自 state を分ける。
- **CORE-EXEC-10 workflow の子投入と協調譲渡** — 経路: [WorkflowPort][exec-workflow-port] 内から同 runtime の CommandPort/WorkflowPort と [ShouldYield][exec-workflows]。1操作は有限な子 command・子 workflow を await する根の結果回収まで。
  - 入力: 代表深さ2・子4件、境界 CPU 同時数1、負荷深さ4・合計64子。ShouldYield の短い同期区間と再開後区間は別プロファイルとし、時間値を性能保証しない。
  - リセット・判定: 全子を await し固定 checksum、履歴は64件まで。[WorkflowTests][test-workflow] の非 deadlock・context 復帰・待機時間除外を照合。別 runtime 投入拒否は V/D、通常 V/S/C/D。管理実行／専用機、継続保持を別観測。
- **CORE-EXEC-11 host の更新と有界 drain** — 経路: [RunHostPhase・PumpJobs・PumpHostLifecycle][exec-runtime] と [DriveOnHost][exec-plan]。1操作は既知件数の command と更新を含む1 host turn。ライフサイクル Pump だけの枝は別記録。
  - 入力: 代表 command16件・update4件、境界仕事なし／1件、負荷 command256件・update16件。maxWorkItems の上限1/64を別選択、時間予算で中断する枝は D/V を中心とする。
  - リセット・判定: 毎 turn payload と計数値を進め、有限 backlog を排出。[GraphLifetimeTests][test-graph-life]・[RuntimeTests][test-runtime] の進捗・順序を照合。通常 V/S/C/D、管理実行。未完了保持と turn 自身の割当を区別。
- **CORE-EXEC-12 専用 thread と複数 producer** — 経路: [DedicatedThread・BindDedicated・DrivePeriodic][exec-plan] と [ExecutionRuntime.Scheduling][exec-scheduling]。1操作は固定窓の受付から全完了、または idle owner の通知起床から完了まで。
  - 入力: 代表 producer2・owner1・64 command、境界 producer1・1件、負荷 producer4・owner2・256件。periodic update と制御 backlog の共存は別選択。
  - リセット・判定: barrier で測定開始を揃え、有限窓ごと全回収。[DedicatedLoopTests][test-dedicated]・[SchedulingTests][test-scheduling] の起床・更新進捗、[RuntimeTests][test-runtime] の欠落なしを照合。V/C/D、専用機。S は OS 雑音評価後の候補。thread/stack 等の native 保持は D。
- **CORE-EXEC-13 取消と静止** — 経路: cancellationToken、CommandRun/JobRun.RequestCancel、[StopAsync][exec-runtime]。1操作は事前取消の受付、または gate 待機／実行開始後の取消通知から実完了まで。三者を混ぜない。
  - 入力: 代表待機8件、境界1件、負荷256件。command・range・workflow はそれぞれ別プロファイルにして開始後の動作差を残す。
  - リセット・判定: fresh な token source、開始位置を barrier で固定、全結果回収。[RuntimeTests][test-runtime]・[GraphLifetimeTests][test-graph-life]・[WorkflowTests][test-workflow] の開始前副作用なし／開始後の実結果保持・静止待機を照合。V/C/D、管理実行／専用機。実時間の競合順序検証は V/D。
- **CORE-EXEC-14 起動・停止・cleanup 再試行** — 経路: [StartAsync・StopAsync][exec-runtime]、IExecutionModule の初期化・逆依存 cleanup。1操作は新規 runtime の起動または1回の停止。両方を一つの通常 command 測定に混ぜない。
  - 入力: 代表4 module、境界1 module・仕事なし、負荷32 module・未完了64件。Drain、CancelPending、起動中停止、cleanup 失敗後の再試行は別選択。
  - リセット・判定: 新規 endpoint、停止成功後のみ fixture を解放。未観測結果で停止が止まらないこと、失敗時の owner 保持と再試行を [RuntimeTests][test-runtime]・[ModuleCollectionTests][test-modules]・[ShutdownTests][test-shutdown] に照合。通常 V/C/D、失敗競合 V/D。専用機の thread 終了と管理結果保持を分離。

## 構築契約と生成コードの実行処理群

- **CORE-COMP-01 slot の snapshot 捕捉** — 経路: [`CompositionSlot<TTarget,TChild>` の indexer][comp-slot]。1操作は既存 slot から子配列を捕捉した assignment を1個生成するまで。
  - 入力: 代表子8、境界0/1、負荷256。毎回子の値を変えるが子オブジェクト構築は外側。Apply は含めない。
  - リセット・判定: 捕捉後の元配列変更が後の Apply に影響しないことを [CompositionTests][test-comp] に照合、捕捉を消費して除去を防ぐ。V/S/C/D、管理実行。snapshot 配列と closure の割当・保持を区別。
- **CORE-COMP-02 assignment の再適用と独立配列** — 経路: [CompositionSlotAssignment.Apply][comp-assignment] → slot の snapshot.Clone。1操作は既存 assignment を新しい軽量 target へ1回適用するまで。
  - 入力: 代表子8、境界空配列による clear、負荷256子。target は配列参照と checksum のみ保持し、UI は導入しない。
  - リセット・判定: target 準備は外側、前回配列を変更して次回と独立であることを [CompositionTests][test-comp] の EachSlotApplicationReceivesItsOwnChildArray に照合。V/S/C/D、管理実行。null/default assignment 拒否は V/D。毎回の clone と元 snapshot 保持は別観測。
- **CORE-COMP-03 省略値と adapter の状態保持** — 経路: [`Optional<T>`][comp-optional] と [`ComposeArgument<T>`.IsSpecified・GetInitialValue・AttachTo][comp-argument]。1操作は指定値の解決と target への metadata 接続1回、省略枝は判定1回。
  - 入力: 代表 int と参照値、境界省略／明示 default／null、負荷64 adapter を固定列で処理。明示 interface 実装・可変 struct は別プロファイル。
  - リセット・判定: ref の factory-local adapter を毎回復元し、呼出回数と変異維持を [GeneratorTests.AdapterReview][test-comp-adapter] に照合。V/S/C/D、管理実行。利用側 storage の保持と dispatch 割当を分離、boxing がないとの未測定断定はしない。
- **CORE-COMP-04 生成 factory と内容初期化** — 経路: [CompositionGenerator の Render][gen-comp] が生成する factory・indexer を独立 consumer で実行。1操作は1 node 構築、引数→with→content 適用まで。slot は上記と重複集計しない。
  - 入力: 代表 parameter4・action2・子8、境界省略値／callback明示null／空 content、負荷 parameter16・action8・子256。配列代入と ICompositionContent.Initialize を別選択。
  - リセット・判定: 毎回新規 node、factory delegate の初回取得と温間取得は別ケース。引数省略・初期値・callback 非実行・適用順・同じ instance を [CompositionTests][test-comp]・[CallbackTests][test-comp-callback] に照合。V/S/C/D、管理実行。node/storage と cached delegate の保持を別観測。
- **CORE-COMP-05 function wrapper と再構築** — 経路: [CompositionGenerator.Functions][gen-comp-functions] が生成する factory と host の [ICompositionRebuildable][comp-rebuild]。1操作は wrapper 構築、または保持 recipe の再構築1回を別測定。
  - 入力: 代表引数4・閉じた型1、境界既定値／keyなし、負荷引数16・再構築64回の有限窓。host は軽量 node と計数だけを持つ。
  - リセット・判定: 捕捉済み入力から毎回 fresh な node を再現して結果を消費、最後の node だけ保持。[FunctionTests][test-comp-function] の host による実行・既定値・recipe 再現を照合。V/S/C/D、管理実行。関数実体の費用と wrapper の割当・recipe 保持を分離。

## ソース生成器のコンパイル時処理群

以下はランタイムの throughput とは別系列で、各生成器を独立 consumer compilation と CSharpGeneratorDriver から呼ぶ。
1操作の出力は生成 tree・diagnostic を消費し、コンパイル成功の照合は計時外で行う。driver 実行だけと最終 consumer compile は別ケースにする。
全項目 V/S/C/D、環境は「生成」。冷間 driver は冷間 process を意味しない。process 冷間は独立起動し SDK 初期化・JIT も含む別プロファイルにする。D の build 診断は生成・コンパイラーの trace と管理メモリ／process 保持を観測し、ランタイム診断と混同しない。

- **CORE-GEN-01 Execution の冷間 driver** — 経路: [ExecutionGenerator.Initialize][gen-exec] → 新規 driver.RunGeneratorsAndUpdateCompilation。1操作は driver 構築と初回生成まで、構文解析・参照読込は外側の固定済み compilation を使う。
  - 入力: 代表4 container・各 command/update/range を計8宣言、境界対象なし／1宣言、負荷32 container・計256宣言。既存入力と生成 payload の枝を別選択。
  - リセット・判定: driver は毎回新規で過去の結果を保持しない。[Execution GeneratorTests][test-gen-exec] の expanded sender、wrapper 参照、生成コンパイル、決定性を照合。生成文字数・tree数と driver 管理割当を記録。
- **CORE-GEN-02 Execution の温間増分再実行** — 経路: 同じ [ExecutionGenerator.Initialize][gen-exec] の driver を初回実行後に更新して再実行。1操作は無変更、無関係 tree の変更、対象宣言1個変更のいずれか1回。
  - 入力: 代表32宣言、境界1宣言、負荷256宣言。基底 compilation を2版で交互に使い、無関係変更が出力再生成を省くとは仮定しない（CompilationProvider との結合がある）。
  - リセット・判定: driver の返り値を次へ渡すが履歴は直近だけ保持。fresh driver の同一入力出力と tree/hint/diagnostic を照合し [Execution GeneratorTests][test-gen-exec] の決定性を補助根拠にする。増分ヒット数は D の観測であり保証しない。
- **CORE-GEN-03 Execution の不正宣言診断** — 経路: [ExecutionGenerator.Emit][gen-exec] の検証を公開 driver で通す。1操作は不正 container を含む compilation の生成1回。
  - 入力: 代表正しい8宣言と不正1宣言、境界不正1個だけ、負荷正しい128宣言と不正16個。wrapper形状・名前衝突・runtime不整合は別プロファイル。
  - リセット・判定: fresh driver を基準とし、[Execution GeneratorTests][test-gen-exec] の LMEXEC 診断と container 全体の生成拒否を照合。診断ID・位置・生成有無を消費し、エラー件数を性能の良否とはしない。
- **CORE-GEN-04 Composition の冷間 driver** — 経路: [CompositionGenerator.Initialize][gen-comp]・[Functions][gen-comp-functions]・[Arguments][gen-comp-arguments] → 新規 driver の初回生成。1操作は driver 構築と生成、consumer compile は分離。
  - 入力: 代表 class8・function8・各引数4、境界対象なし／1型、負荷 class64・function64・各引数8。generic・継承・slot・callback・owned content・adapter は意味別の有限 fixture とする。
  - リセット・判定: 新規 driver、参照は固定。[Composition GeneratorTests][test-gen-comp]・[Functions][test-gen-comp-functions]・[ArgumentAdapters][test-gen-comp-arguments] の独立 host コンパイルと型制約を照合。生成出力量、管理割当、参照 metadata 保持を分離。
- **CORE-GEN-05 Composition の温間増分再実行** — 経路: [CompositionGenerator.Initialize][gen-comp] の実行済み driver に無変更、非対象 tree 変更、parameter/slot/function1個変更を渡す。1操作は更新後の生成1回。
  - 入力: 代表16定義、境界1定義、負荷128定義。変更前後2版だけを交互適用し、各回の driver と compilation は直近だけ保持する。
  - リセット・判定: fresh driver と生成 tree・hint・診断を比較、[Composition GeneratorTests][test-gen-comp] の型付き consumer 契約を確認。CompilationProvider 結合を踏まえ再利用の有無は D で観測し、温間=全出力キャッシュヒットと決めない。
- **CORE-GEN-06 Composition の診断と名前・型検証** — 経路: [CompositionGenerator.Validate][gen-comp]、[Arguments][gen-comp-arguments]、[Functions][gen-comp-functions] の検証を driver 経由で通す。1操作は不正入力1 compilation の生成。
  - 入力: 代表正しい16定義＋不正1個、境界不正1個、負荷正しい128定義＋不正16個。名前衝突・owned content 制約・adapter mapping・非対応 function を別 fixture にする。
  - リセット・判定: fresh driver で LYC001 と compiler diagnostic を分けて消費。[Composition GeneratorTests][test-gen-comp] と [Functions][test-gen-comp-functions] の拒否と有効定義の生成結果を照合。例外的入力の割当は通常生成と別系列。

## 状態機械の処理群

- **CORE-STATE-01 定義構築と凍結** — 経路: [StateMachineBuilder.BuildDefinition・Build][state-builder] と [StateMachine の候補配列構築][state-definition]。1操作は登録済み builder の定義確定、または新規登録から確定までを別測定。
  - 入力: 代表8状態・32遷移、境界1状態・遷移0、負荷128状態・512遷移。優先順位が混在する場合と同順位の場合を別選択。
  - リセット・判定: 再凍結だけを測らないよう状態と遷移を毎回新規作成。[StateMachineTests][test-state] の不変 snapshot・初期入場なし・設定凍結を照合。V/S/C/D、単一 thread 管理実行。候補配列の割当と定義保持を分ける。
- **CORE-STATE-02 実行者の生成と構築統合** — 経路: [StateMachineInstance の constructor][state-instance]、[ComposeStateMachines.Machine.BuildDefinition・Build][state-compose]。1操作は共有定義から実行者生成、または Compose 経由の定義構築までを別ケースにする。
  - 入力: 代表初期入場2 callback、境界callback0、負荷共有定義から64実行者。Compose 構築は8状態・32遷移を代表とし runtime 生成とは混ぜない。
  - リセット・判定: fresh context、生成後の入場回数と独立した CurrentState を [StateMachineTests][test-state] に照合。V/S/C/D、単一 thread 管理実行。共有定義と実行者ごとの保持を区別、失敗入場は V/D。
- **CORE-STATE-03 guard と単一入力の候補選択** — 経路: [StateMachineInstance.CanFire][state-instance] → [StateMachine.Find][state-definition]。1操作は変更しない状態に対する候補評価1回。
  - 入力: 代表出辺8・guard2、境界候補なし／最初成立／最後成立／全不成立、負荷出辺128・guard4。enum と string trigger は別選択、異なる context を交互に渡す。
  - リセット・判定: 固定カウンターで guard の AND・短絡、優先順位・登録順、状態非変更を [StateMachineTests][test-state] と [StateCandidateTests][test-state-candidates] に照合。V/S/C/D、単一 thread 管理実行。候補が既に索引化された温間経路と定義構築は別。
- **CORE-STATE-04 複数 trigger の捕捉と選択** — 経路: [CanFireAny・FireAny][state-instance] の CaptureTriggers と FindAny。1操作は入力集合の捕捉から評価完了まで、遷移ありと評価だけを分ける。
  - 入力: 代表trigger8・出辺16、境界空／重複のみ／未知値、負荷trigger64・出辺128。集合順と遷移優先順位が逆の fixture を用意する。
  - リセット・判定: 配列長を固定して内容を交互更新、最大集合長に達した初回と再利用後を分離。[StateMachineTests][test-state] の重複除去・最大1遷移を照合。V/S/C/D、単一 thread 管理実行。内部 List の保持容量は新規割当と別観測。
- **CORE-STATE-05 遷移、自己遷移、通知** — 経路: [StateMachineInstance.Fire][state-instance] の Take。1操作は1成功遷移、2状態の往復と自己遷移を別ケースにする。
  - 入力: 代表exit/effect/enter各1・subscriber1、境界callback0、負荷各8・subscriber8。状態別出辺8を保ち、遷移後も有効候補がある fixture を使う。
  - リセット・判定: 戻れない終端状態の反復を避け、有限 checksum で退出→effect→状態変更→入場→通知を [StateMachineTests][test-state] に照合。V/S/C/D、単一 thread 管理実行。無効 trigger は別ケース、listener なしを通常時間比較の条件とする。
- **CORE-STATE-06 失敗回復と診断** — 経路: [Fire・CanFireAny][state-instance]、[StateMachineDiagnostics.Activities][state-diagnostics]。1操作はguard/exit/effect/enter/通知のいずれかで失敗する1回、または listener 付き成功1回。
  - 入力: 代表1失敗点、境界再入拒否と入力 snapshot 例外、負荷8 subscriber の有限記録。各失敗点は別 fixture、成功測定へ混ぜない。
  - リセット・判定: fresh 実行者または記録済み部分状態から明示復旧。[StateMachineTests][test-state]・[StateCandidateTests][test-state-candidates] の部分状態・候補整合・評価ロック解除・診断タグを照合。V/D、単一 thread 管理実行。Activity・診断記録の割当と保持を観測し速度判定はしない。

## ワールドの処理群

以下は単一 thread の管理実行、計数だけの Scene と軽量 DrawContext を使用する。描画は CPU の呼び出し配送だけであり、GPU 送信・Present・実デバイス入力を測定しない。

- **CORE-WORLD-01 所有と非実行時の Scene 編集** — 経路: [WorldManager.CreateWorld][world-manager]、[World.AddScene・RemoveScene][world]。1操作は新規 world と scene 所有の構築、または inactive world の追加・削除対を別測定。
  - 入力: 代表world4・各scene8、境界world1・scene0/1、負荷world64・各scene32。削除成功と未所属の検索失敗は別選択。
  - リセット・判定: 編集対象は非実行、同じ scene を重複追加しない。[WorldStackTests][test-world] の所有・編集条件を照合。V/S/C/D、誤所有・破棄後アクセスは V/D。world/scene の管理割当と所有リスト保持を分離。
- **CORE-WORLD-02 layer 予約とフレーム境界 snapshot** — 経路: [WorldStack.Push・Pop・Replace][world-stack] → [WorldManager.BeginFrame][world-manager]。1操作は予約のみ、または予約済み batch の境界適用を別ケースにする。
  - 入力: 代表4 layer・予約4件、境界空stack／予約なし、負荷64 layer・予約128件。Push/Pop の相殺でも最終状態だけ通知する枝を分離。
  - リセット・判定: fresh batch または2つの合法 stack 状態を交互に使用。予約なしも snapshot・状態計算を行う実装経路として独立記録。[WorldStackTests][test-world] の境界前非反映・最終差分だけの通知を照合。V/S/C/D。予約 closure、snapshot 割当、確定リスト保持を区別。
- **CORE-WORLD-03 layer の遮断方針と入力配送** — 経路: [WorldStack.HandleInput][world-stack] と BeginFrame の ApplyStates。1操作は確定済み stack への入力配送1回、方針の再計算は別ケース。
  - 入力: 代表4 layer・scene8、境界上位で消費／最後で消費／消費なし、負荷64 layer・scene32。更新のみ遮断・描画のみ遮断・入力のみ遮断を個別 fixture にする。
  - リセット・判定: 消費位置と有限カウンターを復元し、上から下の配送と遮断独立性を [WorldStackTests][test-world] に照合。V/S/C/D。実入力収集は外側、policy と scene 所有保持は割当と区別。
- **CORE-WORLD-04 固定更新と通常・後段更新** — 経路: [WorldStack.FixedUpdate・Update][world-stack]。1操作は固定1 step、または全 Update と全 LateUpdate の1対で、両者を混ぜない。
  - 入力: 代表4 world・scene8、境界空／全更新遮断／elapsed0、負荷64 world・scene32。fixed step は正の固定値、停止復帰は別プロファイル。
  - リセット・判定: 整数 checksum を進めて呼出を消費、時間蓄積や catch-up は持ち込まない。[WorldPhaseTests][test-world-phase] の全 Update 後に全 LateUpdate、停止中の時間非蓄積、境界まで要求保留を照合。V/S/C/D。Scene 自身の仕事を一定にし配送費用と区別。
- **CORE-WORLD-05 描画登録の配送** — 経路: [WorldStack.Draw][world-stack] と [DrawContext][world-draw]。1操作は確定済み stack の計数 Scene 全体への Draw 配送1回。
  - 入力: 代表4 layer・scene8、境界空／下層描画遮断、負荷64 layer・scene32。context は外側で作り同一参照を全 scene へ渡す。
  - リセット・判定: 配送先 ID の固定 checksum を検証し [WorldPhaseTests][test-world-phase] の描画遮断と simulation の独立性を照合。V/S/C/D。GPU/native 費用なしという fixture 条件を記録し、描画一般の性能と呼ばない。
- **CORE-WORLD-06 破棄・通知保留・失敗の境界** — 経路: [RequestDestroy・BeginFrame][world-manager] と [WorldStack.Commit][world-stack]。1操作は有限な破棄 batch の適用、通知中の追加要求は次の境界として別操作。
  - 入力: 代表4 world 中1破棄、境界inactive world／重複要求、負荷64 world 中32破棄。重複 Push の不正 batch、通知例外、再入拒否は別 fixture。
  - リセット・判定: 毎回 fresh な破棄対象、BeginFrame 後の stack/manager/Scene 所有参照を確認。[WorldStackTests][test-world] の不正 batch 原子拒否、破棄要求維持、状態確定後の通知例外集約、[WorldPhaseTests][test-world-phase] の再入後復旧を照合。通常 V/C/D、失敗 V/D。破棄後の到達可能参照と transient 割当を分離。

## 既存 workload の再利用対応

固定基準の [ExecutionBenchmarks.Run][sample-bench] は既存 workload の出典であり、過去の測定値を新しい比較結果にはしない。新しい Execution.Benchmark プロジェクトは基準時点にはない。

| 出典のケース・補助経路 | 対応する処理群 | 再利用する意味と調整 |
| --- | --- | --- |
| Run の typed_single（DemoVoice.PlayAsync） | CORE-EXEC-02 | 型付き送信→RunHostPhase→結果1回観測。Demo 型への依存を軽量 fixture へ抽出し、固定 owner 検査を残す |
| Run の typed_batch_16（AddPlayTo、Submit） | CORE-EXEC-05 | 16件を代表プロファイルの追加候補にし、全件受理→完了→Dispose→Reset を保持。1操作は batch 全体、1 command 換算は副表示 |
| Run の direct・cached_delegate・captured_delegate、MeasureDirect、CreateCaptured | CORE-EXEC-02 の対照枝 | 同じ副作用を持つ直接呼出・delegate の対照であり、Execution の受付処理に数えない。escape の必要性を検証し JIT 制御属性を無条件コピーしない |
| [DemoModule.Configure][sample-module]・[DemoBackend.UpdateControl][sample-backend] | CORE-EXEC-11・CORE-EXEC-12 | Host/専用 thread と周期更新の fixture 候補。所有 thread と更新回数の oracle を抽出し、Audio 風の名前を実デバイス測定と混同しない |

Run の Measure/MeasureDirect の Stopwatch・手書き反復・thread-local 割当計測と Print は統括側の測定へ置き換える。元ソースにない手動 GC を導入せず、歴史的な手動 GC・JIT override が別出典にあっても共通測定へ持ち込まない。
DemoBackend.Play の Playing=true 反復だけに依存せず、状態を交互に変えるか計数する fixture へ移して実仕事を確認する。Demo 由来の入力・結果・所有権条件は保持し、internal メソッドへ reflection で接続しない。
Composition の slot・factory・function と StateMachines/Worlds は上記テストから fixture と oracle を抽出する候補であり、UI/Graphics の既存 tool を Core 依存だけで移管しない。既存 Mathematics/Signals の測定は本6プロジェクトと別分類である。

## モードと頻度の選択案

全処理群の実装可否・予算を確認してから頻度を選ぶ。候補として単発受付、batch、range、slot snapshot、guard 選択、world 更新の代表入力は S、生成器の冷間・温間は別の S 群にできる。
専用 thread の起床、競合、起動停止、失敗回復を短時間 PR 判定へ直ちに固定しない。まず V で意味と完了条件を確認し、C の専用環境と D の観測から安定性を評価する。
PR・定期・手動のどれで実行するかはこの一覧の採否とは別の提案事項である。将来の共通モードを、既存 Signals の normal/screening や Mathematics の独自ループに実装済みとは扱わない。

## 固定ソースと正しさの根拠

以下の参照はすべて基準 SHA に固定する。各 family のメンバー名が測定入口、内部名はその入口で通る経路を説明するための根拠である。

[exec-runtime]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Execution/ExecutionRuntime.cs
[exec-module]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Execution/ModuleBuilder.cs
[exec-plan]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Execution/ExecutionPlanBuilder.cs
[exec-port]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Execution/CommandPort.cs
[exec-gate]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Execution/GateControl.cs
[exec-batch]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Execution/CommandBatch.cs
[exec-submit]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Execution/ExecutionRuntime.Batches.cs
[exec-graph-builder]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Execution/JobGraphBuilder.cs
[exec-schedule]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Execution/ExecutionRuntime.Graphs.cs
[exec-range]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Execution/GraphRangeWorker.cs
[exec-workflow-port]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Execution/WorkflowPort.cs
[exec-workflows]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Execution/ExecutionRuntime.Workflows.cs
[exec-scheduling]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Execution/ExecutionRuntime.Scheduling.cs
[comp-slot]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Composition/CompositionSlot.cs
[comp-assignment]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Composition/CompositionSlotAssignment.cs
[comp-optional]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Composition/Optional.cs
[comp-argument]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Composition/ComposeArgument.cs
[comp-rebuild]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Composition/ICompositionRebuildable.cs
[gen-exec]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Execution.Generators/ExecutionGenerator.cs
[gen-comp]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Composition.Generators/CompositionGenerator.cs
[gen-comp-functions]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Composition.Generators/CompositionGenerator.Functions.cs
[gen-comp-arguments]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Composition.Generators/CompositionGenerator.Arguments.cs
[state-builder]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.StateMachines/StateMachineBuilder.cs
[state-definition]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.StateMachines/StateMachine.cs
[state-instance]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.StateMachines/StateMachineInstance.cs
[state-compose]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.StateMachines/ComposeStateMachines.cs
[state-diagnostics]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.StateMachines/StateMachineDiagnostics.cs
[world-manager]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Worlds/WorldManager.cs
[world-stack]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Worlds/WorldStack.cs
[world]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Worlds/World.cs
[world-draw]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/src/Core/Lumyte.Worlds/DrawContext.cs
[test-modules]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Execution.Tests/ModuleCollectionTests.cs
[test-runtime]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Execution.Tests/RuntimeTests.cs
[test-admission]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Execution.Tests/AdmissionApiTests.cs
[test-batch]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Execution.Tests/BatchTests.cs
[test-graph]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Execution.Tests/GraphTests.cs
[test-graph-life]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Execution.Tests/GraphLifetimeTests.cs
[test-workflow]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Execution.Tests/WorkflowTests.cs
[test-dedicated]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Execution.Tests/DedicatedLoopTests.cs
[test-scheduling]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Execution.Tests/SchedulingTests.cs
[test-shutdown]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Execution.Tests/ShutdownTests.cs
[test-gen-exec]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Execution.Tests/GeneratorTests.cs
[test-comp]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Composition.Tests/CompositionTests.cs
[test-comp-adapter]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Composition.Tests/GeneratorTests.AdapterReview.cs
[test-comp-callback]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Composition.Tests/CallbackTests.cs
[test-comp-function]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Composition.Tests/FunctionTests.cs
[test-gen-comp]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Composition.Tests/GeneratorTests.cs
[test-gen-comp-functions]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Composition.Tests/GeneratorTests.Functions.cs
[test-gen-comp-arguments]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Composition.Tests/GeneratorTests.ArgumentAdapters.cs
[test-state]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.StateMachines.Tests/StateMachineTests.cs
[test-state-candidates]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.StateMachines.Tests/StateCandidateTests.cs
[test-world]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Worlds.Tests/WorldStackTests.cs
[test-world-phase]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/tests/Core/Lumyte.Worlds.Tests/WorldPhaseTests.cs
[sample-bench]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/samples/Lumyte.Execution.Sample/ExecutionBenchmarks.cs
[sample-module]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/samples/Lumyte.Execution.Sample/DemoModule.cs
[sample-backend]: https://github.com/ikihiki/Lumyte/blob/05d391955a607e7fb948812c9660dc2bed32704d/samples/Lumyte.Execution.Sample/DemoBackend.cs
