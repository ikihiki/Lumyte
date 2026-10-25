# ADR-CORE-0003: 宣言した仕事と型付きコマンドによる実行基盤

- 状態: 採用
- 日付: 2026-10-11
- 関連カテゴリ: engine、platform、input、graphics、audio、resources、ecs、animation、diagnostics

## 背景

Audioの生成・操作・解放など、同じ所有スレッドが必要な処理と、並列化できるCPU処理を共通の仕組みで扱う。Browserのようにmanaged threadを作れない環境でも、同じ仕事をHost上で実行できる必要がある。

呼び出し側からラムダを配送する方式では、実行条件・順序・寿命の管理が分散する。モジュールが仕事を宣言し、利用側が型付きデータを送る設計を採る。coreの実行基盤とgeneratorが、所有制約・DI・Host統合を共通化する。Audio backendやResources／ECS固有の状態と資源所有は、それぞれの統合層が管理する。

## 決定

### 責務と配置

coreに `Lumyte.Execution` を置き、BCLだけへ依存させる。メソッドから仕事と送信コードを生成する `Lumyte.Execution.Generators` は別のAnalyzer packageとする。Audio／Resources／ECSが統合層を提供し、coreからこれらやDIへ逆依存しない。

| 仕事 | 宣言 | 実行 |
| --- | --- | --- |
| command | 入力・結果型、handler、ownerまたはCPU、順序group、gate | 条件成立後に一度実行 |
| range | 共通入力、範囲handler、依存辺、分割粒度 | CPUで範囲を分割して実行 |
| update | owner、更新handler、gate | ownerがHost phaseまたは専用threadの周期で直接呼ぶ |

handlerは登録時に一度保持し、送信時には型付きcommandだけを渡す。handlerは同期・有限時間で返し、内部からの別仕事の投入、未完了wait、Pump、停止は禁止する。I/Oや非同期の起動処理は外側のworkflowで行う。

| 環境 | ownerとCPUの配置 |
| --- | --- |
| Desktop | OS／InputはHost、必要なAudio等はDedicated。CPUはThreadPoolまたはCooperative |
| 単一thread／Browser | Hostに役割を統合し、CPUはCooperative。各turnでPumpし、JSへ戻る |
| Native AOT | 同じ契約と閉じたgeneric登録を使用。実行時のReflectionや動的コード生成を不要にする |

HostOnlyの処理は専用threadへ移さない。thread非対応環境のDedicated／ThreadPoolは起動前に拒否し、暗黙のfallbackは行わない。CPU同時数1も物理thread固定の代用にはならない。

### 固定構成とライフサイクル

1. `ExecutionRuntime(IEnumerable<IExecutionModule>)` がmodule一覧を一度だけ収集し、全moduleの名前・実装型を登録してから各 `Declare` を一度呼ぶ。仕事・owner・依存先・静的queue入口の定義だけを取得し、Hostを捕捉せずdeviceやthreadも作らない。
2. アプリが固定Host上で `PrepareOnCurrentHost` を呼び、FixedThreadの配置方針、CPU backend、容量を渡す。runtimeは各moduleの `Configure` を呼び、ownerの配置とphase・周期を集める。HostOnlyは常にHostとする。
3. Prepareが検証・freezeし、静的入口を一括予約・接続する。競合するPrepareも部分接続を公開せず、失敗時は未公開の予約を戻す。
4. HostのPumpを接続してから `StartAsync` を呼ぶ。専用threadが起動し、依存順にowner上で初期化する。全体成功後だけ通常受付と更新を有効にする。
5. 停止は通常受付・更新を閉じ、仕事の静止、依存の逆順のcleanup、専用thread終了の順に進める。借りたHost自体は終了しない。

収集時はnull、同一instance・実装型・名前の重複を拒否する。依存は `DependsOn<TModule>()` の厳密な実装型で解決し、未登録・自己依存・循環を拒否する。DI登録順には依存しない。Declare／Configureは定義だけを扱い、取得したbuilderを保持しない。構成失敗時はruntimeとmoduleを再利用せず組み直す。

Configure用builderは自moduleのowner・updateだけを設定できる。Prepareで配置・駆動設定の欠落や重複、OwnerRequirement／選択したOwnerPlacementとの不一致を拒否する。

生成する入口は仕事ごとの `static readonly CommandPort<TCommand, TResult>` とする。成功Prepare後は型の寿命中一度だけ同じmodule・runtime・owner・設定へ接続し、二重登録・差し替え・停止後の再接続を拒否する。static初期化は空のmanaged入口だけを作る。仕事ごとの入口は共通のOrderGroup queueを使い、型ごとの独立FIFOにはしない。

状態は `Unprepared → Created → Starting → Running → Quiescing → Stopping → Stopped`。Prepareは一回限りで、成功前のStart／Pumpは拒否する。起動中の停止は実初期化の静止と部分生成物のcleanupを待ち、通常受付を再開しない。Startは一回限りで、進行中のStart／Stopへの同時呼び出しは同じ結果を観測する。

停止成功後は静的入口を閉鎖済みのまま残し、runtime・handler・device・不要なstoreへの強参照を外す。未観測結果はrunが保持する。cleanup失敗時は元ownerと残存stateを保持し、Stopの再試行で未解放分だけ処理する。失敗moduleの依存先は先に停止しない。別構成の生成APIテストは別process等へ分離する。

### 主要API

比較元はmain `81bba9e6a30ae7d2e926d0cf3247c140cca3502c`。以下は追加APIの主要メンバーの抜粋である。実装と利用方法は[実行基盤](../../../src/Core/Lumyte.Execution/README.md)を参照する。keyはbuilder／plan所属の不透明値で、default・別plan・型不一致は拒否する。

```diff
+namespace Lumyte.Execution
+{
+    public readonly struct Unit;
+    public readonly struct OwnerKey;
+    public readonly struct ThreadKey;
+    public readonly struct PhaseKey;
+    public readonly struct OrderGroupKey;
+    public readonly struct GateKey;
+    public readonly struct UpdateKey;
+    public readonly struct WorkKey<TCommand, TResult> where TCommand : struct;
+    public readonly struct RangeWorkKey<TContext> where TContext : struct;
+    public enum OwnerRequirement { HostOnly, FixedThread }
+    public enum OwnerPlacement { Host, Dedicated }
+    public enum WorkAdmission { Normal, Startup, Cleanup }
+    public enum CpuExecutionMode { Cooperative, ThreadPool }
+    public enum ShutdownMode { Drain, CancelPending }
+    public enum ExecutionStatus { Unprepared, Created, Starting, Running, Quiescing, Stopping, Stopped }
+    public readonly struct WorkTarget
+    {
+        public static WorkTarget Cpu { get; }
+        public static WorkTarget On(OwnerKey owner);
+    }
+    public sealed class WorkOptions
+    {
+        public WorkTarget Target { get; init; } // defaultは無効
+        public OrderGroupKey OrderGroup { get; init; } // owner仕事では必須
+        public WorkAdmission Admission { get; init; } = WorkAdmission.Normal;
+        public ReadOnlyMemory<GateKey> RequiredGates { get; init; } // 登録時にcopy
+    }
+    public readonly struct WorkContext { public void VerifyAccess(); }
+    public interface ICommandHandler<TCommand, TResult> where TCommand : struct
+    {
+        TResult Execute(in TCommand command, in WorkContext context);
+    }
+    public interface IExecutionModule
+    {
+        string Name { get; }
+        void Declare(ModuleBuilder builder); // 宣言だけ。device生成禁止
+        void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement);
+        ValueTask StartAsync(ModuleContext context, CancellationToken token);
+        ValueTask StopAsync(ModuleContext context);
+    }
+    public sealed class ModuleBuilder
+    {
+        public OwnerKey Owner(string name, OwnerRequirement requirement);
+        public OrderGroupKey OrderGroup(string name, OwnerKey owner);
+        public GateKey Gate(string name);
+        public void DependsOn<TModule>() where TModule : IExecutionModule;
+        // port省略時も登録専用入口を一つ作る。接続はPrepare時。
+        public WorkKey<TCommand, TResult> Command<TCommand, TResult, THandler>(
+            string name, THandler handler, WorkOptions options,
+            CommandPort<TCommand, TResult>? port = null)
+            where TCommand : struct where THandler : ICommandHandler<TCommand, TResult>;
+        public RangeWorkKey<TContext> Range<TContext, THandler>(string name, THandler handler)
+            where TContext : struct where THandler : IRangeWorkHandler<TContext>;
+        public UpdateKey Update<THandler>(string name, OwnerKey owner, THandler handler,
+            ReadOnlyMemory<GateKey> requiredGates = default) where THandler : IUpdateHandler;
+    }
+    public sealed class ExecutionPlanBuilder
+    {
+        // runtimeがmoduleごとに渡す配置builder。名前はmodule内で一意。
+        public PhaseKey HostPhase(string name);
+        public ThreadKey DedicatedThread(string name); // 宣言だけ。まだ起動しない
+        public void BindHost(OwnerKey owner, PhaseKey phase);
+        public void BindDedicated(OwnerKey owner, ThreadKey thread);
+        public void DriveOnHost(UpdateKey update, PhaseKey phase);
+        public void DrivePeriodic(UpdateKey update, ThreadLoopOptions options);
+    }
+    public sealed class ExecutionOptions
+    {
+        // すべて正値。未回収結果と再利用待ちblockも容量へ含める。
+        public int MaxInFlightRuns { get; init; } = 64;
+        public int MaxCommands { get; init; } = 4096;
+        public int MaxGraphNodes { get; init; } = 4096;
+        public long MaxPayloadBytes { get; init; } = 4 * 1024 * 1024;
+        // 停止中の権限付きcleanup専用。通常枠への追加容量。
+        public int ReservedCleanupCommands { get; init; } = 32;
+        public int ReservedCleanupRuns { get; init; } = 4;
+        public long ReservedCleanupPayloadBytes { get; init; } = 64 * 1024;
+    }
+    public enum CommandEnqueueStatus { Accepted, Canceled, NotRunning, QueueFull }
+    public sealed class ExecutionQueueFullException : InvalidOperationException
+    {
+        public ExecutionQueueFullException(string message);
+    }
+    public sealed class CommandPort<TCommand, TResult> where TCommand : struct
+    {
+        public CommandPort(); // 空の固定入口。device/thread/storeは作らない
+        // 一件を予約・格納・commit・通知。Accepted時だけcompletionが非null。
+        public CommandEnqueueStatus TryEnqueue(in TCommand command, CancellationToken token,
+            out System.Threading.Tasks.Sources.IValueTaskSource<TResult>? completion);
+        // 手書き用便宜API。生成senderからは呼ばない。
+        public ValueTask<TResult> ExecuteAsync(TCommand command, CancellationToken token = default);
+    }
+    public readonly struct CommandResultKey<TResult>;
+    public sealed class CommandBatch : IDisposable
+    {
+        public CommandBatch(int maxCommands); // 単一producerの再利用staging
+        public CommandResultKey<TResult> Add<TCommand, TResult>(
+            CommandPort<TCommand, TResult> port, in TCommand command) where TCommand : struct;
+        public void Reset(); // 未受理、または対応run完了・Dispose後だけ
+        public void Dispose();
+    }
+    public sealed class CommandRun : IDisposable
+    {
+        public bool IsCompleted { get; }
+        public Task Completion { get; } // 要求された場合だけ共有Taskを作る
+        public void RequestCancel();
+        public void ThrowIfFailed();
+        public TResult GetResult<TResult>(CommandResultKey<TResult> key); // 全体完了後
+        public void Dispose(); // 完了前は拒否。結果slot返却のみ、native解放なし
+    }
+    public sealed class ModuleContext
+    {
+        // activeなlifecycleの所属・依存・権限を検査する。
+        public CommandEnqueueStatus TryEnqueue<TCommand, TResult>(
+            WorkKey<TCommand, TResult> key, in TCommand command, CancellationToken token,
+            out System.Threading.Tasks.Sources.IValueTaskSource<TResult>? completion)
+            where TCommand : struct;
+        public ValueTask<TResult> ExecuteAsync<TCommand, TResult>(
+            WorkKey<TCommand, TResult> key, TCommand command, CancellationToken token = default)
+            where TCommand : struct;
+        public GateControl GetGate(GateKey key); // 自moduleだけ
+    }
+    public sealed class GateControl
+    {
+        public void Open();
+        public void Close(); // 開始済みhandlerを中断しない
+        public void Fail(Exception error); // 永続失敗。再Open不可
+    }
+    public sealed class ExecutionRuntime
+    {
+        public ExecutionRuntime(IEnumerable<IExecutionModule> modules); // 定義を収集する唯一の公開constructor
+        public static bool ShouldYield(TimeSpan budget); // 現在のworkflow同期区間の経過を判定
+        public void PrepareOnCurrentHost(OwnerPlacement placement, CpuExecutionMode cpuMode,
+            int maxCpuConcurrency, ExecutionOptions options);
+        public ExecutionStatus Status { get; }
+        public Exception? Failure { get; }
+        public CommandPort<TCommand, TResult> Bind<TCommand, TResult>(WorkKey<TCommand, TResult> key)
+            where TCommand : struct; // 接続済みの同じ入口を返す。Startupは拒否
+        public ValueTask StartAsync(CancellationToken token = default);
+        public ValueTask StopAsync(ShutdownMode mode = ShutdownMode.CancelPending);
+        public bool TrySubmit(CommandBatch batch, out CommandRun? run,
+            CancellationToken token = default); // falseは容量不足だけ。全件未受理
+        public bool TrySchedule<TContext>(JobGraph<TContext> graph, in TContext context,
+            out JobRun? run, CancellationToken token = default) where TContext : struct;
+        public int PumpHostLifecycle(int maxWorkItems, TimeSpan maxElapsed);
+        public int PumpJobs(int maxWorkItems, TimeSpan maxElapsed);
+        public int RunHostPhase(PhaseKey phase, int maxWorkItems, TimeSpan maxElapsed);
+    }
+}
```

範囲計算と更新の契約は次のとおり。CPU同時数とbatchSizeは正値、Cooperativeの同時数は1とする。

```diff
+namespace Lumyte.Execution
+{
+    public interface IRangeWorkHandler<TContext> where TContext : struct
+    {
+        int GetLength(in TContext context); // 純粋。受付前に一度評価
+        void Execute(in TContext context, int first, int endExclusive);
+    }
+    public readonly struct JobNodeId;
+    public sealed class JobGraphBuilder<TContext> where TContext : struct
+    {
+        public JobGraphBuilder();
+        public JobNodeId Add(string name, WorkKey<TContext, Unit> work);
+        public JobNodeId AddRange(string name, RangeWorkKey<TContext> work, int batchSize);
+        public void DependsOn(JobNodeId after, JobNodeId before);
+        public JobGraph<TContext> Build(); // 空・循環・無効key・Startupを拒否
+    }
+    public sealed class JobGraph<TContext> where TContext : struct { public int NodeCount { get; } }
+    public sealed class JobRun : IDisposable
+    {
+        public bool IsCompleted { get; }
+        public Task Completion { get; }
+        public void RequestCancel();
+        public void ThrowIfFailed();
+        public void Dispose(); // 完了後だけ
+    }
+    public readonly struct UpdateTick
+    {
+        public TimeSpan Elapsed { get; }
+        public long Index { get; }
+        public long SkippedTicks { get; }
+        public TimeSpan ScheduledTime { get; }
+        public TimeSpan StartedTime { get; }
+    }
+    public interface IUpdateHandler { void Update(in UpdateTick tick, in WorkContext context); }
+    public sealed class ThreadLoopOptions
+    {
+        // period > 0、件数 > 0、0 < maxControlTime < period。
+        public ThreadLoopOptions(TimeSpan period, int maxControlWorkItems, TimeSpan maxControlTime);
+        public TimeSpan Period { get; }
+        public int MaxControlWorkItems { get; }
+        public TimeSpan MaxControlTime { get; }
+    }
+}
```

### メソッドから生成する仕事と送信構造体

コマンドの定義は、元インスタンスへの参照一本を持つreadonly partial structへ置く。このラッパー自体が送信APIを持ち、別のsender型やbinding object、ID表を作らない。Sender／UseReceiver／ExecutionTargetによる切り替えは提供しない。

```csharp
public readonly partial struct DemoVoice(IDemoVoice original)
{
    private readonly IDemoVoice _original = original
        ?? throw new ArgumentNullException(nameof(original));

    [ExecutionCommand]
    private void PlayCore() => _original.Play();
}

// Module.Declareでstatic queueへ一度だけ接続する。
DemoVoice.DeclarePlay(builder, options);

// 実体生成を待ってから利用する。
var voice = new DemoVoice(original);
await voice.Play();
voice.AddPlayTo(batch);
```

ExecutionCommandAttributeはName、Input、AllowAwaitableResultだけを持つ。Core接尾辞を外した名前からtyped command、statelessなreadonly struct handler、静的なDeclareメソッドとWork key、直接送信メソッド、AddToを生成する。classによるコマンド宣言、非readonly struct、複数instance field、value field、nullable参照は拒否する。defaultラッパーの送信も拒否する。

commandはラッパー値と引数を保持する。ラッパーは参照一本なので、元インスタンスを保持するための追加allocationはない。handlerはWorkContext.VerifyAccess後にラッパーのCoreメソッドを直接呼ぶ。送信メソッドはTryEnqueueとValueTask構築を本体へ展開し、ExecuteAsyncの中継、lambda、async state machine、reflection、StackTraceHiddenを生成しない。

返り値なしはUnit。ExistingCommand入力は一つのin structをtyped envelopeへ入れ、receiverも保持する。末尾のin WorkContextは実行時に注入する。生成Workのlifecycle送信はExecuteAsync(context, receiver, arguments...)で、対象を明示する。pending Task／ValueTaskを同期handlerから返す場合はAllowAwaitableResultでStartup限定を宣言し、外側で実operationを待ってowner上で確定する。

ExecutionUpdateとExecutionRangeはowner駆動・CPU範囲処理のhandlerを生成し、ラッパーまたは専用partial classに宣言できる。詳細な公開APIと診断は[generator](../../../src/Core/Lumyte.Execution.Generators/README.md)を参照する。

### 受付・順序・結果の契約

- TryEnqueueは引数、handler内投入禁止、所属、Admissionを検査する。不正は同期例外。正しい呼び出しでは、未接続／受付閉鎖をNotRunning、token取消をCanceled、容量不足をQueueFullとする。Canceledは渡されたtokenが取消済みの場合だけ返す。Acceptedは受付完了であり実行完了ではない。
- 実行条件は先行成功、起動状態、gate、ownerのphase、group順序、CPU枠。異型のPlay／Stop／Destroyも同じgroupで受付順を守る。gate待ちはgroup別に管理し、同じHost上の他groupを止めず公平に進める。同じ物理threadから送ってもinline実行しない。
- batchは同じowner・group、または非順序化CPU仕事に限定する。全件受理か全件未受理とし、同じOrderGroup内では同一batchのsequenceへ他batchは割り込まない。別groupは間で進行でき、CPU batchの実行順序は保証しない。副作用のrollbackやsample精度の同時適用は保証しない。未接続／閉鎖済み／StartupのAddは拒否し、Created中はstagingだけ許す。Submit時も再検査する。
- TrySubmit／TryScheduleはRunning中のみ。引数・状態違反・事前取消は同期例外、容量不足だけfalseとrun=null。受理済みbatchの変更・再投入は禁止し、run完了・Dispose後にResetする。未受理なら内容を保って再試行できる。
- 取消が開始より先なら副作用なし、開始が先なら実際の結果／例外を保持する。一件の失敗は同じrunの未開始の後続を抑止し、全開始済みhandlerの静止後に完了する。失敗、開始前取消、成功の順で全体結果を決め、成功済みの個別結果は失敗runでも取得できる。handler自身のOperationCanceledExceptionはhandler失敗として扱う。

payload／resultはGCが追跡する型付きstoreへ格納し、objectへのboxingやmanaged参照のraw byte化をしない。run・header・payload・result・graph nodeを受付前に一括予約し、失敗時は全返却する。初期実装では型付きcommand object自身をqueue nodeとし、MaxPayloadBytesへinput／resultと保守的な管理費を課金する。MaxGraphNodesにはedgeとrange workerも含める。未回収結果と空きblockも容量へ含め、不要blockは別型へ再配分できるようにする。参照はpayloadの静止後、batchのReset／Dispose時、結果回収時にclearし、slot／result keyの世代を検査する。参照先bufferの予算とleaseは統合側が管理する。

キャンセル可能なgate待ちCPU仕事とgraphにはtokenの通知を必要時だけ登録する。単発run自身が非poolのIValueTaskSourceを実装し、token=0のValueTaskを一度だけ観測する。GetResultはtoken・完了・単一consumerを検査した後、成功／取消／失敗のfinallyでslotを返す。別の完了wrapper・CommandBatch・Taskは作らない。batchの公開CommandRunも再利用せず、結果をDisposeまで保持する。未観測結果をGCで早期回収しない。

結果と先行の書き込みはrelease/acquire相当で公開する。外部継続をhandler内でinline実行せず、OnCompletedのcontext指定を尊重する。await後のownerは保証しない。通常commandの完了は同期handlerの終了を表し、音が聞こえる時刻、GPU fence、外部I/Oの終了とは区別する。

### 非同期workflowジョブ

Resourcesなどの非同期処理には`IWorkflowHandler<TCommand, TResult>.ExecuteAsync`と
`WorkflowPort<TCommand, TResult>`を用い、`ModuleBuilder.Workflow`でCPU仕事として宣言する。
通常commandの有限同期handlerとは別の契約とし、workflowから同じRuntimeの個別CommandPort／WorkflowPortへ送ってawaitできる。
batch・graphの組立て、投入、結果取得はworkflow外で行う。
Pump、同期Wait、runtimeの起動・停止をworkflow内から行わない。

開始と通常のawait後の継続を同じCPU schedulerへ配送する。
実行中の同期区間だけがCPU枠を使い、未完了のI/Oや子ジョブを待つ間は返す。
`ExecutionRuntime.ShouldYield(budget)`は、現在実行中のworkflowの同期区間が指定した時間予算を超えたかを返すAPIである。handlerまたは継続を実行するたびに計測を始め、I/O・キュー待機の時間を含めない。budgetには正の時間を指定し、実行中workflowの外ではfalseを返す。呼出側が境界でyieldするために使い、同期処理を自動中断するものではない。
ThreadPoolでは並列度を制限し、Cooperativeでは`PumpJobs`が各同期区間を進める。
同じ物理threadへ戻る保証はない。SynchronizationContextを捕捉しない
`ConfigureAwait(false)`やTask.Runはこの制御の対象外となるため、workflowのCPU処理では通常のawaitを使う。

workflow全体を実完了まで未完了として追跡し、予約容量は結果の一度だけの観測で返す。
root処理が成功・失敗しても、既に受理した継続callbackを処理してから完了を通知する。rootの結果は一度だけ回収する。
未awaitで切り離した処理が将来到着することまで待つ契約ではない。
未開始なら取消を受け付け、開始後はCancelPendingでも継続を保持して実際の結果・例外を返す。
Stopは結果の観測ではなく実完了を待つ。実行途中の同期区間は中断しない。
workflow portはmodule instanceに属する入口も許し、固定構成の一つのruntimeへ一度だけ接続する。
これにより独立したホスト間でstatic入口を共有せず、生成された制御commandの静的入口とは分けて構成できる。
受付予算はinput・resultと固定の管理費を対象とし、workflow内部の非同期状態やfan-outで増える
継続callback、参照先bufferの総量を制限するものではない。それらの寿命と規模は操作側が管理する。

### CPU graphとownerの駆動

graphは不変・再利用可能とし、各nodeに同じTContext、結果Unitを使う。型の異なる結果の自動配線は行わず、明示した出力領域でつなぐ。無効key・別builder・空・循環・Startupは拒否し、重複辺はまとめる。GetLengthの負数・例外は未受理、0件はcallbackなしで成功、各chunkは重複しない範囲を扱う。

全nodeと将来のowner配送枠を受付時に予約し、owner nodeのgroup sequenceはready時に確定する。gate／owner待ちはCPU枠を消費しない。取消・失敗で未開始node／chunkを抑止し、静止と依存伝播を終えて完了する。ThreadPoolは同時数を制限したdrainで範囲をclaimし、要素ごとのTaskやchunk数分の管理objectを作らない。共有poolの設定を変えず、CPU入力はExecutionContext等へ暗黙依存させない。

Runtimeはgraph全体を未完了の1件として追跡する。内部workerの完了はgraphが管理し、最後の依存伝播stepで完了通知・graphの除去・未完了数の更新を同じlock内で確定する。これは停止待ちの管理であり、graph全体の容量予約とrun.Disposeまでの保持は変えない。

Dedicatedは到着通知と周期でwakeし、owner自身がUpdateを直接呼ぶ。絶対予定時刻を基準に遅延tickを間引き、SkippedTicksとElapsedを渡す。due updateを優先し、合間のcommandを件数・時間予算で制限する。到着ごとの予算リセット、無制限catch-up、busy spinを行わず、空判定と待機登録の通知競合を解決する。同じthreadを共有する更新は周期・予算を揃える。音声sampleの供給はdevice callback／mixer／AudioWorkletの時計に従わせる。

Hostのphaseはpass開始時のsequence上限と継続cursorを持ち、commandと更新を有界に進める。PumpJobsもcallbackだけでなく依存伝播・取消整理を有界化する。戻り値0は全完了ではない。Quiescing中は更新を止めても通常phaseのdrainを続け、初期化・cleanupはPumpHostLifecycleで進める。各Pumpは固定Host限定・再入不可。ThreadPool構成のPumpJobsはCPU仕事をHostへ移さない。

### 起動権限と終了処理

StartのModuleContextは自moduleの全Admissionと、初期化済み依存先のNormal／Cleanupを許す。Stopでは自module・未停止依存先のCleanupだけを許す。contextはworkflow終了時に失効し、自moduleから得たGateControlはruntime世代終了まで有効とする。Startupは静的Portが参照できても通常配送で実行できない。起動中の権限付き仕事はPumpHostLifecycleで進め、gateとowner検査は省略しない。gateはClosedから始まり、Fail後は再Openできない。Open／Failは通常queue容量や閉じたgateに依存しない制御経路で通知し、cleanupも失敗したReady gateに依存させない。

Drainは受付済み仕事を待ち、CancelPendingは未開始分を抑止する。DrainからCancelPendingへの強化だけ許し、閉じたgateや実行中仕事を残して成功扱いしない。更新例外・内部障害はruntimeのFailureへ記録し通常受付を閉じる。通常commandのDomain例外はそのrunへ返す。

停止中（起動workflowがQuiescing中に行う終了処理も含む）のcontext経由cleanupにはrun・header・payload・result・完了領域を含む独立した予約枠を使う。Prepareで各Cleanup一件が収まることを確認する。Running中の外部Cleanupは通常枠を使う。予約不足は外側workflowで進行中cleanupを待って再試行し、進行も回収余地もない場合は失敗を報告する。未回収の通常結果でcleanupを閉塞させない。

アプリは外部producer／I/O workflowの終了も追跡する。native資源は利用者／Resourcesが生成結果の利用権を保持し、owner上で明示的に解放する。実行層の生成物一覧やrun.Disposeによる自動回収に任せない。停止後に結果を読めても、そのnative実体が生存しているとは限らない。実体の独立した利用権を渡す場合は返却をcleanupで確認する。失敗したownerの未解放物や過去のFailureを隠さない。

### DIなしの起動と利用例

DemoModuleは引数なしのconstructorを持ち、内部にmanagedなDemoStateを一つ保持する。Nameは `"Audio"`、DeclareでFixedThreadのOwner・group、Create／Play／StopのNormal仕事、Destroy／CloseのCleanup仕事、InitializeのStartup仕事、Updateを登録する。StartAsyncはInitializeをModuleContextから送って待ち、StopAsyncは同様にCloseを送る。deviceはInitialize handler内で作る。

DemoModuleのConfigureは次のように配置と更新条件を宣言する。HostPhaseはmoduleの公開propertyに保存し、Host構成のアプリがPump時に参照する。

```csharp
public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
{
    if (placement == OwnerPlacement.Host)
    {
        HostPhase = builder.HostPhase("Control");
        builder.BindHost(Owner, HostPhase);
        builder.DriveOnHost(Update, HostPhase);
    }
    else
    {
        var thread = builder.DedicatedThread("Control");
        builder.BindDedicated(Owner, thread);
        builder.DrivePeriodic(Update,
            new ThreadLoopOptions(TimeSpan.FromMilliseconds(10), 64, TimeSpan.FromMilliseconds(2)));
    }
}
```

以下のhostは固定threadのevent loopからOnHostTurnを呼ぶ。例は提案APIの使用順を示し、ReportFailure等のアプリ処理とDemoModule本体は省略する。

```csharp
var audio = new DemoModule();
var runtime = new ExecutionRuntime([audio]); // Declare。定義を収集
runtime.PrepareOnCurrentHost(OwnerPlacement.Dedicated,
    CpuExecutionMode.ThreadPool, 4, new ExecutionOptions()); // Configure、検証・接続

Task? startup = null;
Task? shutdown = null;
bool startupObserved = false, stopping = false, ready = false;
void OnHostTurn()
{
    runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
    runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
    if (!stopping && runtime.Failure is { } failure) { ReportFailure(failure); BeginStop(); }
    if (!startupObserved && startup is { IsCompleted: true })
    {
        startupObserved = true;
        try { startup.GetAwaiter().GetResult(); ready = !stopping; }
        catch (Exception error) { ReportFailure(error); BeginStop(); }
    }
    if (shutdown is { IsCompleted: true })
    {
        var completed = shutdown;
        shutdown = null;
        try { completed.GetAwaiter().GetResult(); OnStopped(); }
        catch (Exception error) { ReportFailure(error); } // Pumpを残しcleanupを再試行
    }
}
void BeginStop()
{
    if (stopping) return;
    stopping = true;
    ready = false;
    shutdown = runtime.StopAsync().AsTask();
}
// OnHostTurnをhostへ接続してから起動。ここで専用thread→owner初期化が始まる。
startup = runtime.StartAsync().AsTask();

// ready後、event loopを止めない外側workflowから呼ぶ。
async ValueTask<DemoState.Voice> CreateAndPlayAsync(ClipId clip)
{
    var original = await audio.State.CreateVoice(clip); // 実体完成を待つ
    var voice = new DemoVoice(original); // 元参照一本
    await voice.Play();
    return voice;
}
```

高頻度の制御は `voice.AddPlayTo(batch)` 等でstagingし、TrySubmitで一括送信する。完了をpollし、結果観測→run.Dispose→batch.Resetの順で再利用する。単発のValueTaskも必ず観測する。終了ではproducerを止め、cleanup失敗から回復したら同じruntime.StopAsyncを再試行し、成功観測後だけevent loopを閉じる。

BrowserではPrepareの引数を `OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions()` とし、Running／Quiescingのturnで `runtime.RunHostPhase(audio.HostPhase, 64, TimeSpan.FromMilliseconds(2))` も呼ぶ。起動・停止中もPumpを続け、未完了のGetResult／Waitでevent loopを止めない。

### DIありの構成例

アプリ側で `Microsoft.Extensions.DependencyInjection` を使う場合は、前節の構成部分を次で置き換える。登録は引数・factory不要とし、ExecutionRuntimeがDIの収集した `IEnumerable<IExecutionModule>` をconstructorで受け取る。coreにはDI依存やIServiceProviderを持ち込まない。

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton<IExecutionModule, DemoModule>();
services.AddSingleton<ExecutionRuntime>();
var provider = services.BuildServiceProvider(); // まだruntimeもthreadも作らない

var runtime = provider.GetRequiredService<ExecutionRuntime>(); // module収集→Declare。Host未確定
var audio = provider.GetServices<IExecutionModule>().OfType<DemoModule>().Single();
// Prepareは必ず固定Host上で行う。ここで配置と静的入口の接続を確定する。
runtime.PrepareOnCurrentHost(OwnerPlacement.Dedicated,
    CpuExecutionMode.ThreadPool, 4, new ExecutionOptions());
// 以降は前節のTask状態、OnHostTurn、BeginStop、CreateAndPlayAsyncを共用する。
```

他のmoduleも `AddSingleton<IExecutionModule, TModule>()` で追加する。標準DIの `AddSingleton<DemoModule>()` だけではこの一覧に入らない。moduleはExecutionRuntimeへconstructor依存せず、Declare／Configureとlifecycle contextから接続する。

このruntimeへ前節のOnHostTurnを接続し、`startup = runtime.StartAsync().AsTask()` を明示的に呼ぶと、専用threadの起動とowner上の初期化が始まる。Browserは前節と同じHost＋CooperativeをPrepareへ渡し、`provider.GetServices<IExecutionModule>().OfType<DemoModule>().Single()` で同じmoduleのHostPhaseを取得してPumpする。DemoModuleを具象型として重ねて登録しない。

root providerは一つとし、scopeごとにruntimeを作らない。停止中もproviderとPumpを維持し、StopAsyncの成功をOnHostTurnで観測した後にproviderを破棄する（この例ではOnStopped内で `provider.Dispose()`）。停止失敗時は破棄せずcleanupを再試行する。DIのDisposeでnative解放やruntime.StopAsyncを代行せず、生成Voiceの静的送信先もDIで差し替えない。

### 他モジュールとの接続

Resourcesは構築時に実行先へ型付き生成要求を送り、実体完成とRuntime世代を検証して公開する。
解放は生成元ownerへCleanupを送り、失敗時は所有記録と借用を保持して再試行する。
ECSは所有済みsnapshotを送信し、受信側からlive worldを直接読まない。
Audioのbackend接続、デコード、デバイス世代と復旧は独立したAudio機能の責務とする。

### 他ライブラリの調査

2026-10-11の一次資料を基にした比較である。初期実装のHost上の割当と平均送信費用は[サンプル](../../../samples/Lumyte.Execution.Sample/README.md)で測定し、各ライブラリとの実性能比較は別途行う。

| 参考 | 確認した方式と採用点 |
| --- | --- |
| [Unreal FAudioThread](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/Engine/FAudioThread) | 関数配送と直接実行の経路がある。ラムダが一律に遅いとはせず、本案は宣言済み型と常時enqueueを選ぶ |
| [FMOD Studio](https://www.fmod.com/docs/2.03/api/studio-api-threads.html) | command bufferと専用thread。batch化を参考にするが、満杯時のblockingは採用しない |
| [XAudio2 operation sets](https://learn.microsoft.com/en-us/windows/win32/xaudio2/xaudio2-operation-sets) | 関連操作をまとめて適用。本案の原子性は受付だけで、sample精度の同時適用ではない |
| [Kira 0.12.5](https://github.com/tesselode/kira/blob/v0.12.5/crates/kira/src/command.rs) | 型付きlatest-valueと用途別ring。状態更新とFIFO操作を区別する |
| [OpenAL Soft 1.25.2](https://github.com/kcat/openal-soft/blob/1.25.2/al/source.cpp) | property領域の再利用とVoiceChange列。内部同期があってもbackendのowner検査を保つ |
| [enkiTS](https://github.com/dougbinks/enkiTS)、[Unity Jobs](https://docs.unity3d.com/6000.0/Documentation/Manual/JobSystem.html)、[Taskflow](https://taskflow.github.io/taskflow/ExecuteTaskflow.html) | 範囲・依存・固定ownerを分け、worker内blocking joinを避ける |
| [Rayon](https://docs.rs/rayon-core/1.13.0/rayon_core/#global-fallback-when-threading-is-unsupported)、[Bevy tasks](https://docs.rs/bevy_tasks/0.17.3/bevy_tasks/)、[oneTBB](https://uxlfoundation.github.io/oneTBB/main/tbb_userguide/Controlling_Chunking_os.html) | threadlessの明示駆動と分割粒度を参考にし、一律の最適値を決めない |

[C# delegateの費用](https://devblogs.microsoft.com/dotnet/understanding-the-cost-of-csharp-delegates/)はclosure生成・cached delegate・間接呼び出しを分けて比較する。[.NET 10 Wasm](https://github.com/dotnet/runtime/blob/v10.0.0/src/mono/wasm/features.md#multi-threading)の制約を前提に、Task.Runやtimerをowner固定・thread非対応の回避策にしない。[PortAudio callback指針](https://portaudio.com/docs/v19-doxydocs/writing_a_callback.html)も踏まえ、制御queueとsample供給を分離する。

## 検証と実装順序

Host／Dedicated、周期駆動、Cooperative／ThreadPool graph、generatorの再現手順は[実行サンプル](../../../samples/Lumyte.Execution.Sample/README.md)を参照する。AudioやResources固有の所有制約は各統合層のテストで検証する。実アセットでの遅延分布は実行環境ごとの測定を必要とする。受け入れ条件は次に集約する。

- 生成後コンパイル、診断、決定的生成、AOT／実Browser。元参照1本、静的入口、本体展開、中継call・boxing・async state machine・StackTraceHiddenの不在を確認する。
- DIなし／引数なしのDI登録からの収集・起動・停止、登録順に依存しない宣言と依存検証、Prepare／Pumpの固定Host制約、Prepare競合とrollback、権限、静的入口の再接続拒否、停止後の参照解放と未観測結果保持を確認する。
- 異型commandのFIFO、batch全件受付、公平性、gate・取消・停止との競合、slot世代、全仕事静止後の完了、cleanup予約と失敗再試行を確認する。
- graphの依存・範囲・owner予約、Hostの予算と継続cursor、周期遅延と省略、通知の取りこぼし、Browserのgesture／実operation追跡を確認する。
- 単発sourceの先行完了／先行継続登録、未完了・二重GetResult、成功／取消／失敗の回収、Audioの所属・破棄・leaseとECS snapshot寿命を確認する。

性能は直接実行、capture／cached delegate、型付き単発／batch、instance binding／静的入口、本体展開前後を比較する。全threadのallocated bytes・GC、senderサイズ、copy量、queue待ちと反映遅延のp50／p95／p99／max、wake／競合、call数・native code size、最大保持memoryを記録する。初回構築と定常時、成功と取消／例外、送信側と実行側を分け、環境・入力・生データを保存する。lock-free MPSC、spin、専用CPU pool等は測定後に判断する。

## 検討した代替案

| 代替案 | 採用しない理由 |
| --- | --- |
| 任意ラムダ配送／実行時Reflection | 条件と寿命が分散し、closureやAOT対応が必要になる |
| voiceごとのproxy・binding・ID表 | 固定構成では元参照一つと静的入口で足りる。native寿命検査は残す |
| ExecuteAsync転送／StackTraceHidden | 中継を隠す費用削減ではなく、生成本体へ便宜処理を展開する |
| queue処理全体を各メソッドへ展開 | 原子性の重複実装とコード肥大を招く。受付primitiveは共有する |
| 一つのFIFO／全操作latest-value／同Hostならinline | 他groupの進行、操作順序、再入禁止を満たさない |
| 外部から毎周期Updateを投入／worker内wait | 送り手の遅延と配送費用、worker枯渇を招く。owner直接駆動と依存辺を使う |
| 停止後の静的queue再接続 | 古いsenderが新しいruntimeへ届く。固定構成のまま閉じる |

## 結果と影響

- 同じ宣言と送信APIをDedicatedとHost共有で使い、生成・制御・解放を正しいownerへ配送できる。
- 送信ごとのラムダ、対象ごとのbinding、不要な中継を省く一方、単発run・queue同期・GC・OS schedulingの費用は残る。無割り当てやhard realtimeは保証しない。
- 固定構成と生成コードの互換性、容量・順序・終了・資源所有の管理が必要になる。生成APIの同一静的型を別runtimeへ使い回すことはできない。

## 別途決定する事項

Browser worker、複数周期の共有thread、priority、work stealing、跨run依存、generatorのgeneric・nested対応は、この実行契約には含めない。各モジュールの公開型やホストへの組込みは統合層の契約で定める。

## 関連資料

- [ADRの書き方](../0001-adr-writing-policy.md)、[Diagnosticsのgenerator方針](../diagnostics/DIAGNOSTICS-0001-diagnostics-transport.md)、[既存OperationGenerator](../../../src/Diagnostics/Lumyte.Diagnostics.Generators/OperationGenerator.cs)
- [Input](../input/INPUT-0001-input-system.md)、[RenderGraph](../graphics/GRAPHICS-0015-render-graph.md)、[Graphicsの資源寿命](../graphics/GRAPHICS-0014-caller-managed-resource-validation.md)、[Browser backend](../../../src/Graphics/Lumyte.Graphics.Browser/README.md)
- [Audio APIの調査時点](https://github.com/ikihiki/Lumyte/blob/27b5e2bd71e198312836bfae081144d43855fb08/docs/adr/audio/AUDIO-0001-audio-system.md)（PR #28のAudio部分のみ）、[Resourcesの調査時点](https://github.com/ikihiki/Lumyte/blob/e7c219e2a223e3fcb60b4d279fefe5f1b142caa1/docs/adr/resources/RESOURCES-0001-resource-management.md)
- [IValueTaskSource](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.sources.ivaluetasksource-1?view=net-10.0)、[ValueTask](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1?view=net-10.0)、[ExceptionDispatchInfo](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.exceptionservices.exceptiondispatchinfo?view=net-10.0)
