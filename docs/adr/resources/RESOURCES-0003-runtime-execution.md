# ADR-RESOURCES-0003: 非同期ロードとRuntimeの実行先

- 状態: 採用
- 日付: 2026-10-23
- 関連カテゴリ: core

## 背景

リソース構築は入力I/O、CPU変換、特定スレッド上の実体生成を組み合わせる。
Stage全体をCPUジョブにすると、依存ロードや所有スレッドへの配送を待つ間もCPU枠を占有し、
単一スレッド環境では進行を止める。デバイスの復旧で実体が交換されると、
長寿命Stageが保持した古い参照から使用不能なリソースを公開するおそれもある。

実行先の選択と、生成に使用した外部サービスの世代を検証する仕組みを分ける。
ロード依存は動的に探索し、候補の構築から公開・失敗時の解放までをResourceManagerが管理する。

## 決定

### 責務と依存方向

`Lumyte.Resources`はStage、依存グラフ、内容世代、公開mapとRuntime依存を扱う。
ここでのRuntime依存は外部サービスの利用可否を表すメタデータであり、実行器ではない。
`Lumyte.Resources.Execution`がResourcesの処理をCoreのExecutionRuntimeへ接続する。
ExecutionRuntimeはモジュールの宣言から配置・容量・並列度を決め、Resourcesのキーや公開mapを知らない。

Stageは非同期workflowとしてSource I/OとRequireAsyncを待ち、有限なCPU処理だけを
ResourceJobへ送る。同期handler内で依存ロード・I/O待機・所有スレッド待機を行わない。
Resourcesのロード依存グラフをExecutionのJobGraphへ変換せず、動的なキー解決と、
有限なCPU処理のスケジューリングをそれぞれの管理者に置く。
Native実体の生成・解放はStageが個別モジュールのコマンドへ配送し、実体の完成を待つ。
Stageの非同期継続にはスレッド親和性を仮定しない。Resources自身はThreadやTask.Runワーカーを追加しない。

### Runtimeの識別と世代

ManagerへIResourceRuntimeDependencyを明示的に登録する。IdはManager内の一意な名前、
Identityは安定したservice instanceであり、どちらも登録後に変更しない。
Stageはserviceを保持し、構築ごとに実際に使う世代を借用する。
TrackRuntimeDependencyへそのVersionとservice identityを渡し、別serviceの同名登録との混同を拒否する。
identity引数の省略は許すが、外部サービスのアダプターは取得元を明示する。

SnapshotのVersionとInvalidatedThroughVersionは単調増加とし、巻き戻しと周回を許さない。
状態が変わるたびにVersionを進め、利用可能なVersionは0より大きく失効watermarkを超える必要がある。
Changedはメタデータ更新の通知とし、handlerから同期的に再構築を呼ばない。
通知が未配送でも取得・参照時にSnapshotを再確認する。providerのgetterは軽量で副作用のない操作とする。
provider契約違反はManagerの失敗として保持し、以後の通常操作で通知する。

構築中は直接記録したVersionと現在のVersionの一致を要求し、一つのStageが同じIdを異なるVersionで直接記録することを拒否する。
既存のResident子が使う有効な旧Versionと、親が直接使う新Versionは共存でき、それぞれを検証する。
公開済みの値は、Versionが進んだだけでは失効しない。利用不可またはwatermark以下になった
Resident依存を使用不能とする。watermarkによる不可逆失効は、新しいdeviceがReadyになっても復活しない。
可用性だけが一時的にfalseになりwatermarkが進まなければ、旧Leaseは可用性の回復後に再び利用できる。

### 依存の寿命と公開

Build依存は構築時と公開前に検証する。完成した独立値は、その後のBuild依存の喪失では失効しない。
Resident依存は完成後も使用可否を検証し、Residentの子を経由する依存も親へ伝播する。
推移的なResident Runtime依存は構築完了時に平坦化し、Lease参照時の再帰探索を省く。
Buildの子を経由する依存をResidentへ昇格させない。

RebuildRuntimeAsyncは指定Runtimeを使う出力と依存する親を候補として再構築する。
公開中、失効中、Referenceで保持されるキーを対象にし、Source Revision、Runtime Version、
公開Versionを検証して候補mapを一括公開する。既存Handleは新しい内容世代を指す。
競合はConflict、変更不要はUnchangedとし、候補を部分公開しない。
構築失敗は例外として伝え、旧公開を置き換えない。既に失効した旧値の利用を保証するものではない。

旧Leaseは元の世代を保持する。失効と物理解放を分け、解放には生成時の実体と実行先を使う。
Stageの出力解放を先に完了させ、世代Leaseなどの補助cleanupを返す。
解放失敗では所有と進捗を保持し、TrimAsyncで未完了部分を再試行する。

## 公開APIと契約

比較元はハンドル・世代管理のrevision `c3a8686`。判断対象の追加・変更を示す。

```diff
 namespace Lumyte.Resources
 {
+    public interface IResourceRuntimeDependency
+    {
+        // 登録中はIdとIdentityを変更しない。Identityの既定値はthis。
+        string Id { get; }
+        object Identity { get; }
+        ResourceRuntimeSnapshot Snapshot { get; }
+        event EventHandler? Changed;
+    }
+    public readonly record struct ResourceRuntimeSnapshot(
+        ulong Version, bool IsAvailable, ulong InvalidatedThroughVersion);
     public sealed class ResourceBuildContext
     {
+        // 未登録IdはArgumentException。identity不一致・混在VersionはInvalidOperationException。
+        // 利用不能・Version不一致・失効済みはResourceUnavailableException。
+        public void TrackRuntimeDependency(string dependencyId, ulong expectedVersion,
+            ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident,
+            object? providerIdentity = null);
     }
     public sealed partial class ResourceManager
     {
-        public ResourceManager(string name, IResourceSource source,
-            IEnumerable<IResourceStage> stages, IMemoryManager memory);
+        // Runtime providerも借用し、終了時に通知購読を解除する。
+        public ResourceManager(string name, IResourceSource source,
+            IEnumerable<IResourceStage> stages, IMemoryManager memory,
+            IEnumerable<IResourceRuntimeDependency>? runtimeDependencies = null);
+        public ValueTask<ResourceReloadResult> RebuildRuntimeAsync(
+            IReadOnlyCollection<string> dependencyIds, CancellationToken cancellationToken = default);
     }
+    public enum ResourceReloadStatus { Unchanged, Published, Conflict }
+    public sealed record ResourceReloadResult
+    {
+        public required ResourceReloadStatus Status { get; init; }
+        public required ulong PublicationVersion { get; init; }
+        // 新規公開した論理出力数。Unchanged/Conflictでは0。
+        public required int UpdatedResources { get; init; }
+    }
+    public sealed class ResourceUnavailableException : InvalidOperationException
+    {
+        public ResourceUnavailableException(string dependencyId, ulong version);
+        public string DependencyId { get; }
+        public ulong Version { get; }
+    }
     public sealed class ResourceLease<T> where T : class
     {
         // Resident Runtimeが利用不能ならfalse。内容の保持と利用可否を区別する。
         public bool IsUsable { get; }
         // Resident Runtimeが利用不能ならResourceUnavailableException。
         public T Value { get; }
     }
 }
+namespace Lumyte.Resources.Execution
+{
+    // 同じ閉じた型のstatic portを共有するIExecutionModule。
+    public sealed class ResourceJob<TCommand, TResult, THandler> : IExecutionModule
+        where TCommand : struct
+        where THandler : struct, ICommandHandler<TCommand, TResult>
+    {
+        // 戻り値は一度だけawaitする。容量不足はExecutionQueueFullException。
+        // 起動前/停止後はInvalidOperationException。実行開始後は実結果を返す。
+        public ValueTask<TResult> Execute(TCommand command, CancellationToken cancellationToken = default);
+    }
+}
 namespace Lumyte.Resources.DependencyInjection
 {
     public sealed class ResourceRegistrationBuilder
     {
+        // 名前付きManagerにscopedなadapterを登録。adapter.Idとの一致も検証する。
+        public ResourceRegistrationBuilder UseRuntimeDependency<TDependency>(string dependencyId)
+            where TDependency : class, IResourceRuntimeDependency;
     }
     public static class ResourceServiceCollectionExtensions
     {
+        // 既存の名前付きManagerへfactoryでadapterを追加する。
+        public static IServiceCollection AddResourceRuntimeDependency<TDependency>(
+            this IServiceCollection services, string managerName, string dependencyId,
+            Func<IServiceProvider, TDependency> factory)
+            where TDependency : class, IResourceRuntimeDependency;
     }
 }
```

## 起動・実行・終了

DIではResourceJobを具象型とIExecutionModuleから同じinstanceとして解決する。
Stageへそのjobを注入し、ExecutionRuntimeへ収集したmodule集合を渡す。
Runtime adapterはUseRuntimeDependencyまたはAddResourceRuntimeDependencyで名前付きManagerへ登録する。
DIなしでは同じmodule・Stage・provider実体をそれぞれのconstructorへ渡す。
DI解決やManager構築だけではExecutionRuntimeは起動しない。

ホストがPrepareOnCurrentHostで配置とportを確定し、StartAsyncの完了後にロードを受け付ける。
ResourceJobのportは閉じたgeneric型ごとに一つで、同じプロセスで停止後に再登録しない。
ThreadPoolはCPU handlerをworkerへ、CooperativeはPumpJobsへ配置する。
Host上の実体生成もホストのPumpで進めるため、待機中に同期Wait/Resultでホストを止めない。

commandの入力bufferはhandlerの実完了まで保持する。取消が実行開始より先なら副作用なしで終え、
開始後は結果または例外まで待ち、未回収のNative出力や入力の早期解放を生まない。
Managerの共有ロード待機の取消と、単一CPUコマンドの実行取消を同じものと扱わない。
終了時はロード受付を止め、Reference・Lease・Snapshotを返し、ResourceManagerの解放を完了する。
その間も元ownerのcleanup受付とHost Pumpを維持し、最後にExecutionRuntimeを停止する。
Managerは借用したSource・Stage・Runtime providerの寿命を所有しない。

## 性能と制約

ジョブ送信は型付きstructとstatic portを使い、送信ごとのラムダやTask.Runを追加しない。
I/Oや依存待機はCPU実行枠の外へ置くが、Stageの非同期状態機械やロードグラフの確保は残る。
ResourceJob全体を無割り当てとする保証はしない。

Leaseの検証では全登録providerのSnapshotを確認した後、平坦なRuntime依存を走査する。
provider状態が変わるとManager lock内で公開mapを走査し、必要時にコピーする。
依存数に対する定数時間や、状態変更時の短いlock保持時間は保証しない。
再構築は候補mapと旧世代を同時に保持するため、
更新ピークのメモリは新旧両方を含む。CPU handlerの粒度・キュー容量・同時実行数は
負荷に合わせて設定し、Cooperative環境で巨大な同期handlerを実行してフレームを占有しない。
遅延分布と実アセットでのピークメモリは、別途測定が必要である。

## 検討した代替案

- Stage全体をJobGraphへ載せる: 動的な依存探索やI/O待機までCPU枠の管理と結合するため採用しない。
- Stageへ生deviceを固定注入する: 復旧後に交換された世代を取得できず、旧世代の解放先も曖昧になる。
- 現在Versionとの不一致をすべて失効とする: 有効な旧Leaseまで利用不能にするため、不可逆な失効はwatermarkで区別する。
- Lease参照のたびに依存グラフを再探索する: 繰り返しアクセス時の探索・一時確保を避け、完成時にResident依存を平坦化する。

## 結果と影響

同じStageを初回ロードとRuntime再構築で使い、実行器の配置と公開の整合性を独立して管理できる。
一方、外部サービスのadapterには安定したidentity、正確な世代借用、失効通知の契約が必要になる。
ExecutionRuntimeの起動とcleanupを含む終了順はホストの責任として残る。

## 検証方針

Runtimeの更新のみでは旧Leaseが残り、不可逆失効ではResidentの親にも利用不能が伝わることを検証する。
Build依存の独立値、生成中のVersion変化、Changed通知より先の参照、同名別provider、
Snapshotの巻き戻しと同Versionでの状態変更を確認する。
再構築と通常ロードの競合では一括公開かConflictのどちらかとなり、失敗候補が公開されないことを確認する。
旧世代の保持・解放失敗の再試行はManagerの公開APIで検証する。
CPU配送はCoreの容量・取消・ThreadPool/Cooperativeの契約に従い、
個別実体の生成・解放スレッドはそのモジュールの結合テストで確認する。

## 関連ADR

- [ハンドルと世代](RESOURCES-0002-generations-reload.md)
- [実行基盤](../core/CORE-0003-execution-and-jobs.md)
