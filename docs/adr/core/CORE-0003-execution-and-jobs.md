# ADR-CORE-0003: 所有スレッドとスレッド非依存のCPUジョブ実行

- 状態: 提案
- 日付: 2026-10-11
- 関連カテゴリ: engine、platform、input、graphics、animation、diagnostics

## 背景

Lumyteには、構築した物理スレッドで操作するInput、Activate時に所有者を固定する診断の実行ポイント、BrowserのJS interopなどのスレッド制約と、独立したデータに対して並列化できるCPU処理がある。Graphicsは各backendの制約と呼び出し側の同期に従い、RenderGraphはowner threadで記録する。すべての描画deviceへ一律の構築スレッド制約を追加するものではない。lockによる排他、仕事の順序、物理スレッドの固定は異なる契約である。また、Browserではmanaged threadを作れない構成が通常であり、非同期APIがあることは並列に実行できることを意味しない。

本ADRは固定所有スレッドへの配送とCPUジョブを共通の実行基盤として定義する。所有者・有界キュー・ホストのPump、CPUジョブの依存関係、スレッドなしでの進行、起動と終了を一つの契約として扱う。配置はcoreカテゴリとし、ホストや各機能はこの基盤を明示的に組み合わせる。

このPRの成果物は設計案である。実装プロジェクト、API実装、性能測定結果は含まない。実装時の検証条件を後述し、調査した他ライブラリの性能特性をLumyteの実測値として使わない。

## 決定

### 実行場所と責務

次の三つを区別する。

| 要求 | 提供する仕組み | 保証と利用例 |
| --- | --- | --- |
| 特定の物理スレッドで直列実行 | `IThreadExecutor` とownerのPump／専用thread | 生成から解放まで固定。Input、Window、JS interop、device操作 |
| 順序・非重複だけが必要 | CPUジョブ間の明示的な依存辺 | 先行完了後に後続開始。同じworkerになる保証はない |
| 相互に独立したCPU計算 | CPUジョブグラフと範囲分割 | nativeでは並列化可能。threadlessでは同じ依存関係で順次処理 |

「workerを一つにする」設定をowner保証の代用にしない。複数のHost／Game／Render／Audioの役割を同じexecutorへ割り当てられるようにし、機能数に比例してthreadを作らない。OSのメッセージループやSTAを必要とする役割は、適切なホストthreadに固定する。

CPUジョブにowner指定を混在させない。ジョブグラフはCPU計算だけで閉じ、Inputのsnapshot取得 → CPU計算 → ownerで結果適用という外側のworkflowで接続する。これにより、CPUジョブの進行が描画phaseのPumpを待つ循環を作らない。複数graphや複数frameで同じ可変対象を扱う場合も、外側のworkflowが先行runの完了を確認してから次を投入する。

配置は、所有スレッドへの配送を `src/Core/Lumyte.Threading/`、CPUジョブを `src/Core/Lumyte.Jobs/` とする。名前空間・NuGet名も各プロジェクト名に揃える。どちらもBCLに依存し、互いとInput／Graphics／Diagnostics／DIには依存しない。Engineまたはアプリの統合コードが両方を構成する。この設計PRではフォルダや空プロジェクトを追加しない。

```mermaid
flowchart LR
    H[Host / Engine] --> O[固定owner executor]
    H --> J[CPU JobScheduler]
    O --> S[入力snapshot・独立buffer]
    S --> J
    J --> R[完了済みCPU結果]
    R --> O
    O --> B[Input・Graphics・Audio]
```

### 所有スレッドへの配送API

比較元はmain `81bba9e6a30ae7d2e926d0cf3247c140cca3502c`。そこには `Lumyte.Threading` がなく、以下はすべて追加の提案である。所有スレッドへの配送とCPUジョブを本ADRで一緒に定義する。宣言は主要メンバーの抜粋とする。

```diff
+namespace Lumyte.Threading
+{
+    public interface IThreadExecutor
+    {
+        // 非空の診断用名称。一意性やthread識別には使用しない。
+        string Name { get; }
+        // 固定ownerかを確認する。受付状態や安全なframe phaseは保証しない。
+        bool CheckAccess();
+        // owner以外ならInvalidOperationException。
+        void VerifyAccess();
+        // 任意producerから同期区間をenqueueし、その終了を通知。async voidは禁止。
+        ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default);
+        // TがTask／ValueTaskでも内側の完了は追跡しない。
+        ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default);
+    }
+
+    public sealed class ExecutionQueueFullException : InvalidOperationException
+    {
+        // 未受理を表す。callbackは一度も実行されていない。
+        public ExecutionQueueFullException(string executorName);
+        public string ExecutorName { get; }
+    }
+
+    public sealed class HostThreadExecutor : IThreadExecutor
+    {
+        // 現在のhost threadを借用。capacity > 0、待機件数の上限。
+        public HostThreadExecutor(string name, int capacity = 256);
+        // owner限定・再入拒否。開始時点の待機仕事から最大件数を実行。
+        public int Pump(int maxWorkItems = 64);
+        // thread-safe・冪等。受付を閉じ、受理済み仕事はdrainする。
+        public void Complete();
+        // 全受理済み仕事と内部後始末の終了。待機中もhostがPumpを続ける。
+        public Task Completion { get; }
+        // IThreadExecutorの全メンバーを実装する。
+    }
+
+    public sealed class DedicatedThreadExecutor : IThreadExecutor
+    {
+        // 専用managed threadを起動し、受付準備完了後に返す。OS loop／STAは提供しない。
+        // Browserは初期対象外。他のthread非対応環境もPlatformNotSupportedExceptionで失敗する。
+        public static ValueTask<DedicatedThreadExecutor> CreateAsync(string name, int capacity = 256);
+        // thread-safe・冪等。受付を閉じ、専用threadで受理済み仕事をdrainして停止する。
+        public void Complete();
+        // 以後callbackを実行せず、専用threadの終了処理を終えた時点で完了。
+        public Task Completion { get; }
+        // IThreadExecutorの全メンバーを実装する。
+    }
+}
```

`name`と例外の`executorName`は非空、capacityとmaxWorkItemsは正値とする。null callbackを含む引数不正は同期的に引数例外を投げる。専用thread非対応や起動失敗はCreateAsyncの返すValueTaskを失敗させ、部分的に生成した実行資源を片付ける。ホストはthread利用可能性を確認して構成を選び、非対応時に起動失敗を試してからfallbackする運用にしない。

### 所有者とホストの責務

HostThreadExecutorのownerはコンストラクターを呼んだ物理thread、DedicatedThreadExecutorのownerは新しく起動した専用threadである。一度決めたownerは終了まで変更しない。`AsyncLocal`、Task ID、たまたまworkerが一つであることを判定に使わない。CheckAccess／VerifyAccessは終了後も同じowner判定を行い、受付終了を確認するAPIの代用にはならない。

Host／Game／Render／Audioの役割へ、構成側がexecutorをコンストラクター引数などで明示的に渡す。staticな現在のexecutorやサービスロケーターは設けない。初期構成では同じhost executorを共有し、既存backendの契約が許す場合だけRenderやAudioを専用threadへ分離する。Window・Input取得などOSイベントやSTAが必要な役割を汎用の専用executorへ移さない。HostThreadExecutorはOSメッセージループやrequestAnimationFrameを開始・停止しない。

専用executorは仕事を配送するだけで、GameのtickやAudio.Updateを生成しない。更新頻度と次回要求は統合側が管理し、遅れた更新を無制限に積まない。Game／Renderを分離するときはframe番号付きの不変snapshotまたは所有権を移したbufferを有界の枠で渡す。受け手が返す前に書き換えず、frame省略の方針も統合側が決める。

### 受付・順序・キャンセル

配送は同期区間に限り、ownerからのInvokeAsyncも常にenqueueする。同時producerを受付地点で順序付け、受理したcallbackをFIFOで一度だけ実行する。取消で開始を抑止した要求は実行しない。異なるexecutor間の全順序は保証しない。適切なphaseのownerからはVerifyAccess後に直接操作できるが、その操作はqueueのFIFO順序に含まれない。

容量は待機件数の上限であり、実行中の一件は別に数える。捕捉するPCMやsnapshotのbyte上限は統合側が管理する。満杯は `ExecutionQueueFullException`、受付終了は `ObjectDisposedException` として返すValueTaskを失敗させ、callbackを受理しない。隠れた無制限の待機queueは作らない。未受理なら資源と解放責任は呼び出し側に残る。要求元への拒否、後のturnでの再試行、音量など最新値の明示的な集約は利用側が選び、executorが再生・停止・解放を勝手に捨てたり集約したりしない。

既に取消済みのtokenは受付前にcanceled ValueTaskを返す。受理後の取消と開始は原子的な確定点で競合を解決する。取消が勝てばcallbackを呼ばず待機枠を返し、ValueTaskを取り消す。開始が勝てば同期callbackを最後まで追跡し、成功結果または例外を返す。開始後の取消だけで通知を先に終わらせないため、生成した資源が返却前に所有者を失うことを防げる。引数検証、受付前取消、受付状態と容量の順に確認し、Completeと受理の競合も同じ受付地点で決める。

callbackの例外はそのInvokeAsyncへ伝え、他の仕事やexecutorの正常終了を妨げない。返されたValueTaskは呼び出し側が一度観測する。複数箇所で観測する必要があれば一度AsTaskして共有し、fire-and-forgetのPostは設けない。予期しない内部障害では受付を閉じ、未実行要求を失敗させる。開始済みcallbackは終了まで追跡し、その後executorのCompletionも失敗させる。

### Pump・非同期処理・配送の完了

hostは安全な更新位置でPumpを呼ぶ。誤ownerとPump再入は `InvalidOperationException`。開始時点の待機仕事から最大maxWorkItems件のcallbackを処理し、途中で投入された仕事は次のPumpへ残す。戻り値は実行したcallback数で、開始前に取り消した要求を含めない。予算は件数であり時間の上限ではない。実行中のcallbackを中断できないため、大きなuploadや長い処理は呼び出し側で分割する。

配送するcallbackにasync void、ブロッキングI/O、GPU待機を使わない。`Func<T>`がTask／ValueTaskを返しても、executorが追跡するのは内側の非同期処理を開始して戻るまでである。必要ならownerで開始したTaskをcallback外でawaitし、関連する資源の寿命も外側のworkflowが追跡する。内側の継続がownerを要求する場合は、その非同期APIとhost自身が契約を満たす必要がある。executorがSynchronizationContextを差し替えて任意のasync delegateを固定threadへ閉じ込めることはしない。

ownerから未完了の配送、I/OやCPUジョブを `.Wait()`、`.Result`、`GetAwaiter().GetResult()` で待たない。同じexecutorだけでなく別executorとの相互待機や、複数の役割が同じinstanceを指す場合にも適用する。待機中の再入Pumpも使わない。外側のworkflowが非同期に順序を組み立て、hostは独立してPumpを駆動する。

完了継続はcallback内でinline実行しない。BCL／host runtimeがTaskの継続を配送することと、owner callbackを実行するthreadの固定は別の契約である。InvokeAsyncのawait後がownerであるとは保証しないため、次のowner操作も明示的に配送する。Browserでも通常のイベントループによる継続とPumpを維持し、主threadをblockしない。

Completeは受付を閉じるだけで、受理済みcallbackを取り消さずdrainする。Hostではその後もownerからPumpを続け、専用executorは自身の処理ループで進める。通常のcallback失敗は個々のInvokeAsyncに残し、全受理済み仕事と実行器の後始末が終わればexecutorのCompletionは正常に完了する。完了後のHost.Pumpは0を返す。executorは借用host threadを終了せず、資源の自動解放、GPU完了、Audio再生、callbackが開始した外部I/Oの完了も追跡しない。

UI、ユーザー操作を要するWeb Audioの再開、Browserのframe取得からPresentまでの同期区間はhostの既存制約に従う。一般queueへ移すことでユーザー操作の有効期間や借用frameを次turnへ持ち越してはならない。例えばAudioの再開はユーザー操作handler内で最初のawaitより前に開始し、同一turnが必要な描画は準備を済ませてからframeを取得する。

### 所有資源と統合側の契約

deviceを固定ownerで扱う構成では、生成、子の生成・利用・解放、device解放まで同じownerへ配置する。リソース取得とデコードは外で進め、deviceへ依存する生成・転送・解放だけを配送する。resource managerなどのlockを保持してcallbackを呼んだり配送完了を待ったりしない。借用bufferとdeviceの利用権は、配送の完了と外部側の使用終了の両方を満たすまで保持する。

device復旧やscene終了をまたぐ要求には統合側の世代番号を持たせ、実行時にも有効性を確認する。古い要求を新しいdeviceへ無条件に適用しない。既に生成した資源の解放は元のownerへ戻し、未受理や失敗時も参照と解放責任を保持する。cleanupには取り消されたload tokenを流用しない。

音声のリアルタイムcallbackはmanaged配送queueやCPUジョブへ投入して完了を待つ場所ではない。将来のstream接続は事前に準備した有界bufferから消費し、callbackへ割り当て、I/O、lock、Task待機を持ち込まない。

### CPUジョブの単位と依存関係

一つの `JobGraph<TContext>` は不変のDAGであり、毎frame作り直さず再利用する。Builderが仕事と依存辺を登録し、Buildで循環・異なるBuilderのID・重複辺を検査して確定する。`DependsOn(after, before)` はbeforeの全仕事が正常に完了してからafterを実行する。自己依存と循環は拒否し、重複辺は一辺へまとめる。

通常ノードは一つの短い同期callback、範囲ノードは `[0, length)` を重複しない連続範囲に分けたcallback群である。範囲内は呼び出し側がループする。`batchSize`は一回のcallbackの最大要素数とし、最終範囲だけ短くなる。同じ範囲ノードのcallbackは並行実行され得る。全範囲の成功がノードの成功となり、後続ノードを一度だけreadyにする。長さ0はcallbackを呼ばず正常完了し、後続へ進む。

長さはrunごとに `getLength(context)` で一度取得する。これは投入thread上で、scheduler内部lockの外で行う短い副作用のない処理とする。snapshot内の要素数だけを参照し、ownerオブジェクトやI/Oへアクセスしない。負数や例外は投入失敗であり、ジョブは一つも受理しない。0件だけのrunでも依存関係と完了通知は通常どおり処理する。

Contextは値型としてrunにコピーするが、含まれる参照先は深いコピーをしない。static delegateと明示Contextを基本とし、graphにframe固有のclosureを捕捉しない。入力snapshot、出力範囲、借用bufferの所有者は呼び出し側が管理し、runの完了まで書換え・返却・解放を行わない。同じgraphを複数runで使えるが、Contextの参照先まで共有してよいとは限らない。

単一runで直列にしたい操作は辺で結ぶ。別run間にも非重複が必要なら先行run完了後に次を受理させる。グローバルなSerialフラグや暗黙のresource lockを設けない。同じ再生者、出力、イベントsinkへ同時にアクセスしないことは利用者の契約であり、並行アクセスの自動推論は初期対象外である。

### CPUジョブAPIの追加差分

比較元はmain `81bba9e6a30ae7d2e926d0cf3247c140cca3502c`。そこには `Lumyte.Jobs` がなく、以下はすべて追加の提案である。宣言は主要メンバーの抜粋とする。

```diff
+namespace Lumyte.Jobs
+{
+    // 呼び出し中だけ有効なcontextのreadonly参照。async voidは禁止。
+    public delegate void JobAction<TContext>(in TContext context) where TContext : struct;
+    public delegate int JobLength<TContext>(in TContext context) where TContext : struct;
+    public delegate void JobRangeAction<TContext>(
+        in TContext context, int startInclusive, int endExclusive) where TContext : struct;
+
+    // Builder固有のID。defaultと別BuilderのIDは無効。
+    public readonly struct JobNodeId;
+
+    // 単一threadで構築。Build後の編集は拒否し、Buildの再呼出しは同じgraphを返す。
+    public sealed class JobGraphBuilder<TContext> where TContext : struct
+    {
+        public JobGraphBuilder();
+        public JobNodeId Add(string name, JobAction<TContext> action);
+        // getLengthは投入側で評価。batchSize > 0、範囲の最大要素数。
+        public JobNodeId AddParallel(string name, JobLength<TContext> getLength,
+            int batchSize, JobRangeAction<TContext> action);
+        public void DependsOn(JobNodeId after, JobNodeId before);
+        public JobGraph<TContext> Build();
+    }
+
+    // 不変で再利用可能。空graphも有効。公開constructorは持たない。
+    public sealed class JobGraph<TContext> where TContext : struct
+    {
+        public int NodeCount { get; }
+    }
+
+    public enum JobExecutionMode { Cooperative, ThreadPool }
+    public enum JobRunStatus { Pending, Running, Succeeded, Canceled, Faulted }
+    public enum JobShutdownMode { Drain, CancelPending }
+
+    public sealed class JobSchedulerOptions
+    {
+        // 正の値。既定64。未完了runの最大数。
+        public int MaxInFlightRuns { get; init; } = 64;
+        // 正の値。既定4096。未完了runが予約するノード数の合計上限。
+        public int MaxInFlightNodes { get; init; } = 4096;
+    }
+
+    public sealed class JobScheduler
+    {
+        // 現在threadを借用。CPU workerを生成せず、jobをThreadPoolへ投入しない。
+        public static JobScheduler CreateCooperative(JobSchedulerOptions? options = null);
+        // maxConcurrency > 0。hostがthread利用可能と判断した環境だけで使用。
+        // Browserは初期実装ではPlatformNotSupportedException。CPU数から自動選択しない。
+        public static JobScheduler CreateThreadPool(int maxConcurrency,
+            JobSchedulerOptions? options = null);
+        public JobExecutionMode Mode { get; }
+        // Cooperativeでは1。専用threadの本数ではなく、同時callback数の上限。
+        public int MaxConcurrency { get; }
+        // 容量不足だけfalse + run=null。全runを一括受理し、呼び出し内でjobを実行しない。
+        public bool TrySchedule<TContext>(JobGraph<TContext> graph, in TContext context,
+            out JobRun? run, CancellationToken cancellationToken = default) where TContext : struct;
+        // Cooperativeの所有thread限定。再入拒否。両予算は正値。
+        // 通常callback一つ／範囲callback一つをwork itemとして数える。
+        public int Pump(int maxWorkItems, TimeSpan maxElapsed);
+        // thread-safe・冪等。最初の呼び出しが停止modeを確定する。
+        public void BeginShutdown(JobShutdownMode mode = JobShutdownMode.Drain);
+        // 受付終了と全runのterminal化後、backendが以後jobを開始せず後始末を終えた時点で完了。
+        public Task Completion { get; }
+    }
+
+    // runごとに独立した参照。公開constructorなし、利用者へ公開したinstanceはpool再利用しない。
+    public sealed class JobRun
+    {
+        public JobRunStatus Status { get; }
+        // Succeeded／Canceled／Faultedでtrue。true観測は完了済み書き込みを公開する。
+        public bool IsCompleted { get; }
+        // 参照時に必要なら一つだけ生成。成功／取消／例外を通知する共有Task。
+        // Taskの同期Waitは使わない。await後のthreadは保証しない。
+        public Task Completion { get; }
+        // 未完了はInvalidOperationException、取消はOperationCanceledException、
+        // 失敗は全callback例外を保持するAggregateException。
+        public void ThrowIfFailed();
+    }
+}
```

名前は非空の診断用文字列で、識別や依存解決には使わない。同名を許す。null delegate／graph、無効ID、負数の長さ、非正の設定は引数例外とする。循環とBuild後の編集は `InvalidOperationException`。optionsは生成時にコピー・検証する。Buildに失敗したBuilderは編集可能なままとする。

`TrySchedule`は任意producerから呼べるが、任意のschedulerのjob callback／getLength内からの再投入は `InvalidOperationException` とする。終了後は `ObjectDisposedException`。既に取消済みtokenなら `OperationCanceledException` を同期的に返し未受理とする。graph単体のNodeCountが容量上限を超える場合は、待っても成功しないため `ArgumentException` とする。受理前にrun枠と全ノード枠を一括予約し、容量不足・終了との競合による部分実行を認めない。容量不足ではgetLengthを省略してよい。getLength評価後も受付と容量を再確認し、評価しただけでは受理済みとしない。

runの受付線形化点を過ぎた取消は受理後の取消として扱い、必ず非nullのrunを返す。`TrySchedule`自身はjobをinline実行しないが、ThreadPoolの別threadでは戻る前に開始・完了し得る。run間の開始・完了順は保証しない。

### 進行・完了・失敗

`Pending`は受理済みでcallback未開始、`Running`は一つ以上のcallbackを開始済み、terminalは `Succeeded`／`Canceled`／`Faulted` とする。空graphはPendingからSucceededへ直接遷移できる。terminal状態は変化しない。

依存カウンターと完了通知はrelease/acquire相当で同期する。先行callbackの書き込みは後続callbackと、IsCompleted=trueの観測／Completionのawaitに成功した消費者から見える。CPU完了はGPU fence、Audio再生、外部I/Oの完了を意味しない。並列の範囲・兄弟ノードの実行順や浮動小数点reductionのbit一致は保証しない。再現性が必要なら範囲別の出力を後続の固定順で集約する。

例外または取消要求を観測するとrun全体を停止方向へ進め、未開始callbackを抑止する。停止と開始は原子的な確定点で競合を解決する。開始が勝ったcallbackは最後まで実行させ、終了までcontextとbufferの借用を維持する。依存待ちノードも終了会計へ含め、handleを永久にPendingにしない。既に成功したcallbackの副作用はロールバックしない。

全開始済みcallbackの終了後、例外が一つでもあればFaulted、なければ受理後の取消が成功確定より先に観測された場合Canceled、それ以外Succeededとする。例外はrun内で収集し、全体をAggregateExceptionとして観測できる。callbackが自ら投げた `OperationCanceledException` も通常の失敗であり、制御tokenによるschedulerの取消とは区別する。兄弟ノードの開始タイミングによって完了した副作用の範囲は変わり得る。

terminal化時にcontext参照とscheduler内部のノード枠を返し、JobRunには安定した状態と例外情報だけを残す。公開JobRunとCompletionのTaskを別runへ再利用しないため、古いhandleが新しい実行を指すことはない。内部slotには世代を持たせ、遅い通知が再利用後のslotを変更しないよう検証する。run全体のTaskは任意で一つ、ノードや要素ごとには作らない。外部await継続はjobの完了処理内でinline実行しない。

Cooperativeが使わないのはCPU workerの生成・jobのThreadPool配送・完了の同期待機である。Taskの非同期継続はBCLとホストruntimeの仕組みで配送され、nativeではThreadPool、threadなしBrowserではイベントループを使い得る。これはCPUジョブのbackendとは別であり、継続の実行場所をschedulerが保証するものではない。ホストはawait継続とPumpの両方を進行させる。Taskを必要としないframe loopはIsCompletedとThrowIfFailedで観測できる。

### ThreadPoolとスレッドなしのbackend

初期native backendは.NET ThreadPoolを利用し、scheduler自身が同時実行数と受理量を制限する。固定専用CPU poolを先に作らない。ThreadPoolはプロセス共有のため、他のI/O継続と競合し、frame deadlineやworkerのidentityを保証しない。`SetMinThreads`／`SetMaxThreads`でプロセス全体を変更しない。

readyノードをすべてTaskにする方式は採らず、最大MaxConcurrency個のdrain callbackがready仕事から範囲を獲得する。範囲を配列として全展開せず、次の開始位置をclaimするcounterで管理する。runごとの可変状態とready情報は予約したノード数と同時実行数に比例させ、lengthに比例するTask／queue entryを作らない。graph自身の共有する依存配列はノード数＋辺数に比例し、runごとに複製しない。仕事がない場合はdrain callbackを返し、ready追加との競合をlost wakeupなく解決する。長いdrainによる共有pool占有を避けるため、実装時に件数／時間で再配送する量を測定する。

Cooperative backendは独立した実行経路とし、workerを起動して失敗してからfallbackする方式にしない。TryScheduleはキューへ受理するだけで、作成threadのPumpが依存順に仕事を進める。未完了JobRunをawaitするだけでは仕事は進まない。ホストの各turnにPumpを接続し、初期化・通常frame・終了の各段階でも駆動する。

Pumpは開始時点までに受理したrunを対象とし、そのrun内で新たにreadyになった後続も残り予算で実行できる。途中で受理したrunは次回へ回す。readyなrunをround-robinで選び、一つの巨大な範囲ノードが他runを飢餓させない。実行前に件数と経過時間を確認し、開始したcallbackは中断しない。これはsoft budgetであり、長いcallbackがframeを超えることは防げない。単一の範囲callbackが収まるbatchSizeを利用者が選ぶ。時間計測には単調時計を用いる。

予算確認はユーザーcallbackだけを対象にしない。長さ0ノードの完了伝播、取消による未開始ノードの整理も、各ノードの状態遷移と依存辺の有界な小バッチごとに経過時間を確認し、途中状態を次のPumpへ持ち越せるようにする。再帰でDAG全体を一度に完了させない。maxWorkItemsと戻り値は実行したcallback数であり、内部整理だけを進めて0を返すこともある。0は全run完了を意味しないため、進行と終了の判断には各Completionを使う。

| 環境 | owner処理 | CPU backend | 同期待機 |
| --- | --- | --- | --- |
| Windows／Linux | Host、必要時だけDedicated | ThreadPoolまたはCooperativeを明示選択 | owner／job callbackでは禁止 |
| Browser・通常のthreadなしWasm | main contextのHost | Cooperative | 禁止。各turnをJSへ返す |
| Browser・thread対応Wasm | JS interopのownerは別途維持 | 初期実装ではCooperative | mainをblockしない |
| その他のthread作成不可host | 既存host threadのHost | Cooperative | hostイベント処理を止めない |

.NET 10では公開の汎用thread-capability APIを前提にしない。`Environment.ProcessorCount`が複数でもthreadが作れるとは限らない。ホストが配置・ビルド条件に基づいてfactoryを選ぶ。ThreadPool backendの起動や実行機構に内部障害があれば新規受付を閉じ、受理済みrunを実行中callbackの終了後に失敗させる。実行途中で別backendへ移してcallbackを再実行しない。

.NET 10のWasm multithreadingは既定無効で実験的機能として説明されている。対応runtime、`WasmEnableThreads`、SharedArrayBuffer、COOP／COEPなどが必要になる。単一CPU backendのために配信条件を変更しない。Browser worker／OffscreenCanvasの対応は、実際のhostとJS interop制約を検証する別の設計対象とする。

### ブロッキングと非同期I/O

CPU callbackは同期処理のみである。`async void`、Taskを内部に起動して完了前に戻る処理、ブロッキングI/O、GPU待機、ownerへの配送待機を禁止する。任意delegate内部の `.Wait()` を一般に検出することはできないため、API契約・利用例・回帰検証で明示する。公開の同期 `Complete(run)` や、待機しながら他のownerのPumpを呼ぶAPIは設けない。

ネストしたグラフ実行・worker内joinも初期対象外とし、依存辺または外側の非同期workflowへ展開する。`Pump`と停止操作を任意のschedulerのjob callback／getLengthから呼ぶと `InvalidOperationException`。callback内で別schedulerを同期的に待つ構成も契約違反である。これにより、全workerが子仕事を待って止まる構成と、子の完了を待たず親のbufferを返す構成を避ける。

I/Oは本来のasync APIを直接使い、待つだけの `Task.Run` を作らない。I/Oで準備したデータをCPUジョブへ渡し、CPU完了後にownerへ適用を配送する。`SynchronizationContext`や`ExecutionContext`をジョブごとにcaptureしない。AsyncLocal、thread local、ambientなDI scope／Activity／cultureに計算入力や所有権を依存させず、必要な値をContextに明示する。Cooperativeでもホストのambient状態に依存するcallbackを移植可能とは扱わない。

### フレームへの接続例

典型的な一回のフレームは、ownerでイベント取得・診断Pump・Input更新・snapshot作成、CPUジョブ、ownerで結果適用・イベント配送・描画の順とする。複数frameを重ねる場合はsnapshotと出力bufferも世代ごとに分離し、有界の受け渡し枠を設ける。

```csharp
// 初期化時に一度だけ構築。FrameDataは借用bufferを持つreadonly struct。
var builder = new JobGraphBuilder<FrameData>();
JobNodeId evaluate = builder.AddParallel("animation", static (in FrameData frame) => frame.Count,
    batchSize: 64, static (in FrameData frame, int start, int end) =>
    {
        // 各要素は別々の再生者・出力・イベントsinkを持つ。
        for (int i = start; i < end; i++)
        {
            frame.Evaluate(i);
        }
    });
JobNodeId gather = builder.Add("gather", static (in FrameData frame) => frame.Gather());
builder.DependsOn(gather, evaluate);
JobGraph<FrameData> graph = builder.Build();

// owner上でsnapshot取得後。falseならbufferを保持して後のturnで再試行する。
if (jobs.TrySchedule(graph, in frame, out JobRun? run))
{
    pendingRun = run;
}
```

```csharp
// awaitしないhost更新callback。初期化・終了中も必要に応じて呼ぶ。
owner.VerifyAccess();
owner.Pump(maxWorkItems: 32);
if (jobs.Mode == JobExecutionMode.Cooperative)
{
    jobs.Pump(maxWorkItems: 16, maxElapsed: TimeSpan.FromMilliseconds(1));
}

if (pendingRun is { IsCompleted: true })
{
    // 失敗時も全callbackは停止済み。try/finallyで借用bufferを返す。
    try
    {
        pendingRun.ThrowIfFailed();
        ApplyCompletedFrame(); // ownerの安全なphaseでのみ適用する。
    }
    finally
    {
        ReturnFrameBuffers();
        pendingRun = null;
    }
}
// 未完了なら前の完成済みsnapshotを使うか、このframeの描画を延期してhostへ戻る。
// Browserのframe取得・記録・Submit・Presentは同じturn内で完結させる。
```

例のbatchSize=64、件数・1ms予算は説明用であり、性能の推奨値ではない。awaitを使う外側のworkflowでも同じCPU完了を待てるが、その間のCooperative Pumpはホストが独立して呼び続ける。await後の適用には `owner.InvokeAsync` を使う。

既存のInputSystemは構築から参照・更新までownerを維持する。Animationは異なる再生者単位で評価を分割し、イベントの実処理をownerへ戻す。Diagnosticsの `IDiagnosticPump<TPoint>` は既存のexecution pointを維持し、ジョブのdrainへ置き換えない。RenderGraphのRecordとcommand bufferは現行の同期契約を維持し、ジョブ化による暗黙の並列記録やbarrier生成を追加しない。

### 起動・停止と所有権

ホストを起動し、ownerの実行場所とCPU backendを選び、ownerでdevice・機能を構築する。Host.Pumpの開始前はowner上で同期初期化を直接呼ぶ。InvokeAsyncが必要な初期化は、先に起動用のイベントループへPumpを接続してから非同期に待つ。DedicatedはCreateAsync完了時点で自身の処理ループが稼働している。DIの同期factoryで非同期初期化をblockしない。

Cooperativeの初期化でジョブを使うなら初期化用turnでもPumpを駆動する。初期化完了後だけ開始する通常frameに進行を依存させない。途中失敗時は成功済みの子からowner上で解放し、実行器の受付と必要なPumpを後始末が終わるまで残す。

終了は次の順で行う。

1. 業務の新規producerを止め、I/O workflowを完了または取消し、後からCPU仕事が投入されない状態にする。ownerの受付・PumpとCooperativeのPumpは、この待機中から終了手順全体を通して継続する。
2. `BeginShutdown(Drain)`、または未開始計算を不要とする場合は `CancelPending` を呼ぶ。どちらも新規runを拒否する。CancelPendingは全未完了runに取消を要求し、開始済みcallbackは強制停止しない。
3. ホストのCooperative Pumpを継続してschedulerのCompletionを非同期に待つ。受理済みrunごとの結果を観測し、必要なowner適用・後始末を配送して、借用bufferを返す。
4. 外部GPU／Audioの完了条件も満たして子資源、device、scopeをownerで解放する。全機能の後始末が成功したら共有instanceにつき一度executorをCompleteし、Host.Pumpを続けながらそのCompletionを非同期に待つ。drainと実行器の後始末が完了してからホストを終了する。

`JobScheduler.Completion`はジョブの個別失敗では失敗せず、それらを各JobRunに残して正常に停止する。内部障害はschedulerのCompletionも失敗させる。停止後にPumpを呼べば残りを進められ、停止完了後は0を返す。ThreadPool modeでPumpを呼ぶことは `InvalidOperationException`。停止を開始しただけではbufferを解放してはならない。

executorをCompleteしてからcleanupを配送しても未受理になる。通常queueが満杯なら受付とownerを保ったまま先行仕事を進めて再試行する。子やdeviceの解放が失敗した場合も、失敗と生存資源の参照を保持する。安全に片付けられる独立した機能だけを進め、参照が残るdeviceやexecutorを先に閉じない。

キューが空でもI/O workflowが生きていればエンジン全体の停止完了ではない。停止期限はホストが管理し、期限超過でも実行中の参照を解放したりthreadを強制終了したりしない。呼び出し側へ未完了を報告する。非表示タブではrequestAnimationFrameだけに頼らず終了用のhost turnも使うが、ブラウザーに停止された間の進行は保証できない。

## 他ライブラリの調査と性能方針

2026-10-11に以下の一次資料を確認した。C++／Rust／Unityの設計上の知見を比較し、異なるruntimeの測定値をC#へ転用しない。

| 対象 | 資料で確認した方式 | 本設計への反映 |
| --- | --- | --- |
| [.NET TPL](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskscheduler?view=net-10.0)／[ThreadPool](https://learn.microsoft.com/en-us/dotnet/standard/threading/the-managed-thread-pool) | work stealing、local queue、worker数調整。プロセス共有でthread identityを固定しない | 初期CPU backendに使用。owner固定は別executor。同時数と投入量はLumyte側でも制限 |
| [enkiTS](https://github.com/dougbinks/enkiTS#project-features) | pinned task、待機中の仕事実行、scheduling時の割り当て抑制 | ownerと自由実行を分け、内部格納を再利用。汎用の協調waitは初期APIに持ち込まない |
| [Unity IJobParallelFor](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Jobs.IJobParallelFor.html) | batch分割とatomic work stealing。軽い計算ほど大きいbatchを使う | 要素ごとのTaskを避け、範囲callbackの粒度を指定。UnityのbatchSizeは最小粒度であり本APIの最大chunk長とは異なる |
| [Taskflow](https://taskflow.github.io/taskflow/ExecuteTaskflow.html) | worker内のblocking waitによる枯渇を説明し、協調実行corunを提供 | worker内の子仕事待機を初期契約で禁止し、依存辺で表現 |
| [Rayon 1.13.0](https://docs.rs/rayon-core/1.13.0/rayon_core/#global-fallback-when-threading-is-unsupported) | threadlessでjoinは順次実行できるが、spawnの進行は自動保証しない | threadlessを明示的なbackendとし、Pumpによる進行条件を定める |
| [Bevy tasks 0.17.3](https://docs.rs/bevy_tasks/0.17.3/bevy_tasks/) | single／multi-threadで共通API、Compute／AsyncCompute／IOの区分、owner上のThreadExecutorのtick | ownerの明示駆動とCPU／I/Oの責務分離。複数poolの分割は必要性を測定して決める |
| [oneTBB](https://uxlfoundation.github.io/oneTBB/main/tbb_userguide/Controlling_Chunking_os.html) | 小さいgrainの管理費用、大きいgrainの並列性低下、partitionerによる調整 | 一律の万能batch値を定めず、仕事量・分布・threadlessのturn予算を含めて調整 |

追加の.NET 10固有資料は [Wasm features](https://github.com/dotnet/runtime/blob/v10.0.0/src/mono/wasm/features.md#multi-threading)、[Threadの実装](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/Thread.cs)、[ThreadPoolTaskScheduler](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/ThreadPoolTaskScheduler.cs)。`Thread.IsThreadStartSupported`はinternalで公開契約に利用できない。`LongRunning`もthread非対応環境の回避策ではない。[UnsafeQueueUserWorkItem](https://learn.microsoft.com/en-us/dotnet/api/system.threading.threadpool.unsafequeueuserworkitem?view=net-10.0)のExecutionContext非伝播を前提とする場合も、前述のContext契約を維持する。

性能上の初期方針は次とする。

- graphの構築・循環検出・依存配列の確保をフレーム外へ置く。delegateをgraph内に保持し、runは状態配列をpoolから借りる。実行中node数と容量予約を一致させ、動的な無制限配列拡大で上限を回避しない。
- 範囲ノードは最大batchSizeで分け、rangeが一つなら通常ノード相当の経路を使う。小仕事をenqueueすべきかは直接ループとの比較で決める。性能を理由にTrySchedule内の暗黙inline実行へ切り替えない。
- 一つのrunにJobRunを一つ確保し、Completionを参照した場合だけTaskを確保する。全経路でzero allocationとは宣言しない。例外・取消・診断有効時の確保も別に測る。
- 完了したrunの枠を返し、job数ではなくnode数＋最大同時数に比例するready情報を保持する。Contextが参照するデータ量は含まれないため、Resources／frameのbyte予算を統合側で制限する。
- ownerの配送遅延、runの待機／実行時間、拒否件数、ready数、実行中数、取消／失敗を集約する。詳細トレースはopt-inとし、要素ごとのActivity・文字列生成・高cardinalityタグを既定で発行しない。
- 独自work stealing deque、spin時間、cache line padding、専用CPU pool、frame優先度は計測後に決める。ThreadPoolの共有負荷がp95／p99を悪化させる場合に固定poolを比較し、既存backendと同じAPI契約を保つ。

### 実装時の性能測定

直接の単一threadループを基準とし、Cooperative、ThreadPool＋本scheduler、`Parallel.For`、chunk単位のTaskを同じ結果・入力・warmup条件で比較する。必要性が出た場合だけ専用poolの試作を加える。graph構築、初回JIT／AOT起動、定常実行を分離し、Linux／Windowsのx64・ARM64とthreadなしBrowserで測定する。

| 負荷 | 変える条件 | 記録する値と判断 |
| --- | --- | --- |
| 空run／noop／短い依存鎖・全長さ0の密なDAG | node数、producer数、診断on/off | 受付～完了時間、jobs/s、割り当てbytes/run。管理費用の基準 |
| Animation相当の均一配列 | 要素数、計算量、batchSize、同時数 | wall time、speedup、CPU使用率。並列化の損益分岐 |
| 仕事量が偏る範囲・fan-out／fan-in | 分布、依存深さ、巨大range | worker遊休、末尾遅延、claim競合。work stealing導入判断 |
| owner配送＋CPU＋async I/O混在 | poolの外部負荷、frame頻度 | frame時間と配送遅延のp50／p95／p99、queue滞留、拒否率 |
| threadなしの大きなrun | batchSize、Pump時間・件数 | 一回のPump最大時間、host入力遅延、完了までのturn数 |
| 長時間繰返し・取消・容量飽和 | run数、成功／失敗率、停止mode | GC回数、最大保持memory、上限の遵守、停止所要時間 |

CPU、OS、.NET／Browser版、ビルド設定、論理CPU数、warmup・反復数、入力データ、実行コマンドと生データを残す。平均だけで判断せず分散とtail latencyを比較する。全threadの割り当てを観測し、投入threadだけのallocated bytesを総量としない。ブラウザー性能は実イベントループとWasm成果物で測る。具体的な性能目標・最適なbatch値は実測後に定める。

## 実装時の受け入れ条件

以下は今後の実装を受け入れるための検証であり、この文書PRで実行済みとは扱わない。

- ownerの生成・操作・解放のthread一致、複数役割の同一executor、誤thread／再入Pumpの拒否、開始時snapshotによるPump予算、FIFOと満杯時の未受理。CPU Pumpでは全長さ0のDAG、広いfan-out、取消整理でも時間予算を再確認し、途中から再開できること。
- executorの引数例外とfaulted／canceled ValueTaskの区別、満杯・受付終了・開始前取消と開始の競合、開始後取消でも生成した資源を失わないこと。callback例外後の継続、内部障害時の未実行要求とCompletionの失敗。
- Hostの借用threadとDedicatedの生成threadでのowner一致、専用threadの起動失敗時cleanup、Completeの冪等性・受理競合、全受理済み仕事のdrain。Taskを返すcallbackの外側workflowが内側の完了と資源寿命を追跡すること。
- 通常frame開始前の初期化Pump、途中失敗の逆順cleanup、共有executorの終了、cleanup満杯・解放失敗の再試行、取消済みload tokenをcleanupへ流用しないこと。
- chain／diamond／独立枝の依存順と一度だけ実行、循環／無効IDの拒否、0件・端数・大きなlengthの範囲重複／欠落なし、先行結果のメモリ可視性。
- 同じgraphの複数runと独立Context、異なるrun間の枠再利用、古いJobRunを保持したまま新しいrunが完了しても状態が混ざらないこと。
- 任意producerの容量競合、予約中の受付終了、getLength例外、受理直前／直後の取消、run単位の全受理または全拒否、ready追加とdrain終了のlost wakeup。
- 実行中callbackの取消・兄弟の例外・依存待ち・0件ノードを含めた全runのterminal化。完了前のbuffer返却が起きず、例外後も別runが進むこと。
- job／getLength内の再投入・Pump・停止操作の拒否（別schedulerへの呼出しも含む）。wait禁止の利用例と検査を用意し、worker枯渇を前提に進行するテストを書かない。
- Cooperativeをnativeでも強制し、schedulerによるCPU worker生成・jobのThreadPool配送・blocking waitを呼ぶと失敗する検査で通常・取消・停止を実行する。Taskの非同期継続はruntimeの配送として別に検証する。加えてthreads無効のWasmを実Browserで動かし、JSへturnが戻り、初期化／終了のPumpも進むことを確認する。
- MaxConcurrency=1が物理thread固定を意味しない契約、同時数上限、node／run容量上限、ThreadPoolの個数変更なし、ambient contextに依存しない結果を確認する。
- 入力snapshot、独立Animation評価、owner結果適用、既存Diagnostics execution point、同一turn内Presentをつないだ統合。CPU成功だけでGPU資源を解放しないこと。
- Drain／CancelPending、I/O中でqueueが空の終了、owner cleanup中の満杯、停止期限超過を検証し、未完了handleや生存bufferを成功として破棄しない。

実装は、まずHostThreadExecutorとgraph／Cooperativeで契約を確立し、次にDedicatedThreadExecutorとThreadPoolを各契約の検証へ通し、最後に実ホスト統合と性能測定を行う。NativeのテストだけでBrowser対応済みとはしない。

## 検討した代替案

| 代替案 | 採用しない理由／再検討条件 |
| --- | --- |
| すべてTask.Run＋lock | 物理thread、phase、threadlessでの進行を保証せず、細粒度では管理費用が大きい |
| 機能ごとに専用thread | 機能数に伴う過剰なthreadと配送が増える。owner固定が必要な役割だけに限定 |
| 独自の固定pool＋work stealingを最初から実装 | correctness・wake・停止・移植の負担に対する実測根拠がない。共有poolのtail latencyが問題なら比較する |
| owner taskをCPU DAGに混在させ、wait中に実行 | owner phaseの暗黙再入や、ownerのPump待ちによる循環が生じる。外側のworkflowで分離する |
| threadなしは未対応／即時inlineのみ | Browserを除外するか、一度に長い処理でhostを止める。共通の依存関係と予算付きPumpを選ぶ |
| 汎用async delegate・nested join | 継続の配置、再入、終了、threadlessの進行が複雑化する。同期CPU区間とI/O workflowを分ける |
| ownerからのInvokeAsyncを暗黙にinline実行 | 先行要求を追い越し、更新途中へ再入し得る。常時enqueueと適切なphaseの直接呼出しを分ける |
| backendごとの自動配送・無制限queue | 呼出しの完了条件とframe境界が見えにくく、遅いuploadや停止タブでmemoryが増える。有界配送と明示的な負荷制御を選ぶ |
| 外部ライブラリを直接導入 | enkiTS／Taskflow／oneTBBは主にC++、Rayon／BevyはRust、Unity JobsはUnityの実行・データモデルに依存する。C#のAOTとBrowserを含む現状ではBCLと小さな独自契約を選ぶ |

## 結果と影響

- 物理thread固定、順序・排他、並列計算を明確に扱い、threadなしでも同じgraphを進行できる。
- 既存のowner契約・GPU完了・診断phaseを維持し、backendへ暗黙の配送やlockを加えない。
- reusable graphとchunk処理で定常時の管理費用を抑える設計だが、直接ループより速いことは未検証である。小仕事には直接実行を選ぶ余地を残す。
- 同期CPU callback、明示Context、ownerとの外側workflowという制約が利用コードに加わる。bufferの寿命、データ競合、frameの遅延・省略方針をschedulerだけで解決するものではない。
- ThreadPool共有の競合と、Cooperativeの非preemptiveな予算超過は残る。粒度調整と実測が必要になる。

## 別途決定する事項

- ホスト別のwake通知、Engineのframe phaseとDI登録API。owner配送とCPUジョブの基本契約は本ADRで定める。
- 固定専用CPU pool、優先度、複数pool、動的な子ジョブ、跨run依存、協調joinの必要性と公開API。
- Browserのmanaged worker、JS interop所有者、OffscreenCanvasを含む対応構成。
- ECSの読み書き宣言による依存自動生成、Native／SIMDカーネル、可変graphの差分更新。

## 関連資料

- [ADR-0001: ADRの書き方と運用](../0001-adr-writing-policy.md)
- [Inputの所有権](../input/INPUT-0001-input-system.md)
- [Animationの共有定義と実行者](../animation/ANIMATION-0001-animation-system.md)
- [Diagnosticsの実行ポイント](../diagnostics/DIAGNOSTICS-0001-diagnostics-transport.md)
- [RenderGraph](../graphics/GRAPHICS-0015-render-graph.md)
- [Graphicsの明示的な資源寿命](../graphics/GRAPHICS-0014-caller-managed-resource-validation.md)
- [Browser backend](../../../src/Graphics/Lumyte.Graphics.Browser/README.md)
