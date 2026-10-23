# ADR-RESOURCES-0003: リソースRuntimeと非同期ロード

- 状態: 採用
- 日付: 2026-10-23
- 関連カテゴリ: core

## 背景

リソース構築は入力I/O、CPU変換、特定スレッド上の実体生成を組み合わせる。
Stageが個別にジョブを送る構成では、実行基盤を知らないStageの同期処理が呼出元を占有する。
一方、非同期処理の完了までworkerを拘束すると、依存ロードや所有スレッドへの配送で進行が止まる。
デバイスの復旧で実体が交換されると、
長寿命Stageが保持した古い参照から使用不能なリソースを公開するおそれもある。

実行先、device、世代とその借用をRuntimeへまとめ、Stageが必要とするリソース環境を表す。
ロード依存は動的に探索し、候補の構築から公開・失敗時の解放までをResourceManagerが管理する。

## 決定

### 責務と依存方向

`IResourceRuntime`は構築・解放の入口を持つリソース環境である。
既定の`ResourceRuntime`は`Lumyte.Resources.Execution`に置き、共有のジョブ実行環境を提供する。
Audio・GraphicsなどのRuntimeも同じ契約を実装し、既定RuntimeをCPU処理に使いながら、device・owner・世代・Leaseを管理する。
これらのRuntimeはdevice交換をまたいで保持できる安定した窓口とする。

```text
ResourceManager → 出力型に対応するIResourceRuntime
                  ├─ 既定ResourceRuntime → Executionのworkflowジョブ
                  └─ モジュールRuntime   → 共通ジョブ + owner処理 + device世代の借用
```

ManagerはStage・依存グラフ・内容世代・公開mapを管理する。
出力型ごとの`ResourceStageRuntime`を構築時に固定し、指定のないStageには既定Runtimeを使う。
選択したRuntimeを各内容世代に保存し、初回ロード・再構築のBuildAsyncと、その出力のReleaseAsyncに使う。
Stageは`ResourceBuildContext.Runtime`から自身の環境を取得でき、ジョブAPIやExecutionRuntimeの参照は不要である。

ExecutionRuntimeは配置・容量・CPU並列度を管理し、Resourcesのキーや公開mapを知らない。
動的なロード依存グラフをExecutionのJobGraphへ変換しない。
RuntimeはCPU処理と所有スレッドの処理を調整するものであり、Stage全体を一つの固定スレッドへ置く契約ではない。
Native生成・解放はRuntimeが提供するownerコマンドへ配送し、実完了を待つ。

### 非同期Stageの実行

既定RuntimeはStageの開始をworkflowジョブへ送り、通常の`await`の継続もCPUキューへ戻す。
ThreadPoolではworker、Cooperativeではホストの`PumpJobs`で同期区間を実行する。
同じworkerへ戻る保証はなく、device操作は各モジュールのownerコマンドへ配送する。
I/O、依存Stage、所有スレッドの処理を待つ間はCPU枠を解放するため、並列数1でも子の処理を進められる。
workflow内から別ジョブや所有スレッドのコマンドを送れるが、同期的なWait/Resultや再入Pumpは許さない。
通常の有限CPUコマンドhandlerの再入制約は維持する。

共通ジョブで動くStageとCPU処理を含む補助メソッドは通常の`await`を使い、ジョブ環境のSynchronizationContextを捕捉する。
`ConfigureAwait(false)`、Task.Run、独自スレッドで処理を逃がした場合、CPU並列度とPump予算の保証対象外となる。
Sourceやdevice service内部の非同期実装は`ConfigureAwait(false)`を使える。呼び出したStage側の通常のawaitが実行先を復元する。
同期区間を途中で中断する仕組みはないため、大きなCPU変換は有限の区間に分け、必要に応じてTask.Yieldをawaitして次の区間をキューへ戻す。

workflowは未完了の非同期操作とその継続を追跡し、実際のStage完了まで入力と出力の所有を維持する。
未開始の処理は取消で実行を省略できる。開始後はCancelPendingでも継続を維持し、渡したtokenによる協調取消と実完了を待つ。
公開はBuildAsyncの実完了と依存検証の後に行う。取消要求だけで実行中の出力を捨てない。
共有ロードに対するAcquireの取消は呼出元の待機だけに適用し、Manager所有の構築取消とは分ける。

### Runtimeの識別と世代

`IResourceRuntimeDependency`はRuntimeのidentity・世代・可用性を検証する契約である。
Stageに選択されたRuntime（既定を含む）が実装していればManagerが自動収集し、明示的なprovider登録とも合わせる。
同じprovider instanceの重複だけを除き、同じIdを持つ別instanceは拒否する。
IdはManager内で一意、Identityは安定したservice identityとし、登録後に変更しない。

Stageはcontext.Runtimeから構築ごとに現在のdevice世代を借りる。Stageへ生deviceを固定注入しない。
自動収集はproviderの登録であり、使用した世代の記録は省略しない。
TrackRuntimeDependencyへ実際に借りたVersionとprovider identityを渡し、別serviceの同名登録との混同を拒否する。
identity引数の省略は許すが、モジュールRuntimeから取得元を明示する。
旧出力の解放には保存したRuntimeと生成時の世代Lease・ownerを使い、現在のdeviceを取り直さない。

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

Runtimeの構築・解放、Stageとの対応付け、環境世代の検証を共通契約にする。

```diff
 namespace Lumyte.Resources
 {
+    public interface IResourceRuntime
+    {
+        ValueTask<object> BuildAsync(IResourceStage stage, ResourceBuildContext context,
+            CancellationToken cancellationToken);
+        ValueTask ReleaseAsync(IResourceStage stage, object resource);
+    }
+    public readonly record struct ResourceStageRuntime(Type OutputType, IResourceRuntime Runtime);
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
+        // この内容世代に選択した安定したRuntime。
+        public IResourceRuntime Runtime { get; }
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
+        // Runtimeと世代providerは借用し、providerの通知購読は終了時に解除する。
+        public ResourceManager(string name, IResourceSource source,
+            IEnumerable<IResourceStage> stages, IMemoryManager memory, IResourceRuntime runtime,
+            IEnumerable<IResourceRuntimeDependency>? runtimeDependencies = null,
+            IEnumerable<ResourceStageRuntime>? stageRuntimes = null);
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
+    // 複数Managerで共有する既定のジョブ環境。portはinstanceに属する。
+    public sealed class ResourceRuntime : IResourceRuntime, IExecutionModule
+    {
+        public ResourceRuntime();
+        public ValueTask<object> BuildAsync(IResourceStage stage, ResourceBuildContext context,
+            CancellationToken cancellationToken);
+        public ValueTask ReleaseAsync(IResourceStage stage, object resource);
+    }
+}
 namespace Lumyte.Resources.DependencyInjection
 {
     public sealed class ResourceRegistrationBuilder
     {
+        // 既存サービスをManager構築時に一度解決し、出力型へ対応付ける。
+        public ResourceRegistrationBuilder UseStageRuntime<TOutput, TRuntime>()
+            where TOutput : class where TRuntime : class, IResourceRuntime;
+        public ResourceRegistrationBuilder UseStageRuntime<TOutput>(
+            Func<IServiceProvider, IResourceRuntime> resolve) where TOutput : class;
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

`AddResources`は既定のResourceRuntime、IResourceRuntime、IExecutionModule、ExecutionRuntimeをsingleton登録する。
複数の名前付きManagerは同じ既定Runtimeを借り、既存の登録は上書きしない。
ResourceRuntimeのmodule登録はAddResourcesに任せ、同じinstanceを別途IExecutionModuleへ重複登録しない。
UseStageRuntimeで既存のモジュールRuntimeを出力型に対応付け、Managerごとに一度解決する。
Stageへのジョブ注入は不要である。追加の世代providerはUseRuntimeDependencyまたはAddResourceRuntimeDependencyで登録できる。
DI解決やManager構築だけではworkerを起動せず、ホストがPrepare/Start/Pump/Stopを管理する。

DIなしでは既定RuntimeをExecutionRuntimeのmodule集合とManagerへ渡す。
次のMeshRuntimeはアプリ定義の安定した環境で、共通ジョブとdeviceServiceを組み合わせる例である。
deviceModuleもホストが登録し、Source・Stage・Memory・Runtimeの寿命を保証する。

```csharp
var resourceRuntime = new ResourceRuntime();
var meshRuntime = new MeshRuntime(resourceRuntime, deviceService);
var execution = new ExecutionRuntime([resourceRuntime, deviceModule]);
execution.PrepareOnCurrentHost(
    OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 2, new ExecutionOptions());
await execution.StartAsync();

await using (var manager = new ResourceManager(
    "assets", source, [new DocumentStage(), new MeshStage()], memory, resourceRuntime,
    stageRuntimes: [new(typeof(Mesh), meshRuntime)]))
{
    using ResourceReference<Mesh> mesh = await manager.AcquireAsync(meshKey);
    using ResourceLease<Mesh> lease = mesh.Acquire();
    Consume(lease.Value);
}

await execution.StopAsync();
```

同じ対応付けをDIで登録する例を示す。MeshRuntimeが必要とするdevice serviceとmoduleは起動前に登録しておく。

```csharp
services.AddSingleton<IMemoryManager>(memory);
services.AddSingleton<MeshRuntime>(); // ResourceRuntimeとdevice serviceをconstructorで受け取る。
services.AddResources("assets", resources => resources
    .UseSource<MySource>()
    .AddSourcePipeline<Document, DocumentStage>()
    .AddTransformPipeline<Document, Mesh, MeshStage>()
    .UseStageRuntime<Mesh, MeshRuntime>());
await using ServiceProvider provider = services.BuildServiceProvider();
ExecutionRuntime execution = provider.GetRequiredService<ExecutionRuntime>();
execution.PrepareOnCurrentHost(
    OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 2, new ExecutionOptions());
await execution.StartAsync();

AsyncServiceScope scope = provider.CreateAsyncScope();
ResourceManager manager = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("assets");
{
    using ResourceReference<Mesh> mesh = await manager.AcquireAsync(meshKey);
    using ResourceLease<Mesh> lease = mesh.Acquire();
    Consume(lease.Value);
}

await manager.DisposeAsync(); // 失敗時は同じmanagerで再試行し、成功してからscopeを閉じる。
await scope.DisposeAsync();
await execution.StopAsync();
```

portはResourceRuntime instanceに属し、独立したホストやDI root間で接続を共有しない。
同じホストの全ManagerとモジュールRuntimeで既定のジョブ環境を共有し、Managerごとにworkerを追加しない。
起動前・停止後の配送は拒否し、容量不足はExecutionQueueFullExceptionで通知する。

BrowserはHost/Cooperativeに配置し、イベントループからPumpJobsと所有スレッドのHost phaseを進める。
ロード・解放をawaitしている間もPumpを続け、同期Wait/Resultでホストを止めない。
終了時は新規ロードを止め、Reference・Lease・Snapshotを返し、Managerの構築と解放を完了する。
その間も継続と元ownerのcleanup受付を維持し、最後にExecutionRuntimeを停止する。
解放は通常の受付容量を使う。容量不足やbackend解放失敗では所有を保ち、回復後に同じManagerのDisposeAsyncを再試行する。
上の例は解放成功時の経路であり、失敗時はManager・scope・各Runtime・ExecutionRuntimeを保持して処理する。
Managerの解放が成功する前にDI scopeを破棄せず、scope自体のDisposeAsyncの再試行には依存しない。
Managerは借用したSource・Stage・各Runtime・世代providerの寿命を所有しない。

## 性能と制約

出力型とRuntimeの対応はManager構築時に固定し、ロードごとにDI解決やRuntime生成を行わない。
開始・解放の送信は型付きstructと共有ジョブ環境のportを使い、送信ごとのラムダやTask.Runを追加しない。
workflowは実行待ちと継続のための状態を保持する。非同期状態機械、継続callback、ロードグラフの確保は残り、無割り当てを保証しない。
I/O待機はCPU枠を占有しない。未完了workflowは実完了まで追跡し、受付容量は返したValueTaskの結果を一度awaitして回収するまで保持する。
停止待機は実完了までであり、完了結果の回収そのものは待たない。

Leaseの検証では全登録providerのSnapshotを確認した後、平坦なRuntime依存を走査する。
provider状態が変わるとManager lock内で公開mapを走査し、必要時にコピーする。
依存数に対する定数時間や、状態変更時の短いlock保持時間は保証しない。
再構築は候補mapと旧世代を同時に保持するため、
更新ピークのメモリは新旧両方を含む。CPU handlerの粒度・キュー容量・同時実行数は
負荷に合わせて設定し、Cooperative環境で巨大な同期handlerを実行してフレームを占有しない。
遅延分布と実アセットでのピークメモリは、別途測定が必要である。

## 検討した代替案

- Stageの完了を同期handler内で待つ: I/Oや子の処理を待つ間にCPU枠を占有するため、継続を別の実行区間へ配送する。
- Stageごとに明示的なジョブ配送を行う: Stage作者へ実行基盤の知識を要求するため、既定Runtimeが開始・継続を配送する。
- Stageへ生deviceを固定注入する: 復旧後に交換された世代を取得できず、旧世代の解放先も曖昧になる。
- 現在Versionとの不一致をすべて失効とする: 有効な旧Leaseまで利用不能にするため、不可逆な失効はwatermarkで区別する。
- Lease参照のたびに依存グラフを再探索する: 繰り返しアクセス時の探索・一時確保を避け、完成時にResident依存を平坦化する。

## 結果と影響

同じStageを初回ロードと再構築で使い、出力型に選択したRuntimeが必要な実行・device環境を提供する。
既定ジョブ環境を共有しながら、モジュールRuntimeがowner処理と世代の借用を調整できる。
Stage作者は通常のawaitと有限な同期区間を守り、Runtimeには安定したidentity、正確な世代借用、失効通知が必要になる。
ExecutionRuntimeの起動とcleanupを含む終了順はホストの責任として残る。

## 検証方針

Runtimeの更新のみでは旧Leaseが残り、不可逆失効ではResidentの親にも利用不能が伝わることを検証する。
Build依存の独立値、生成中のVersion変化、Changed通知より先の参照、同名別provider、
Snapshotの巻き戻しと同Versionでの状態変更を確認する。
再構築と通常ロードの競合では一括公開かConflictのどちらかとなり、失敗候補が公開されないことを確認する。
旧世代の保持・解放失敗の再試行はManagerの公開APIで検証する。
既定Runtimeについて、Pump前にStageが始まらないこと、await後もPumpで継続すること、
並列数1でI/O待機と依存Stage・ownerコマンドが進むことを検証する。
受付前の取消、容量不足、開始後の取消、例外、未完了中の終了で所有を失わないことを確認する。
複数Managerの共有と独立DI rootのport分離、初回・再構築・解放の共通配送経路も対象とする。
Stage別の選択、context.Runtime、DIの一度だけの解決、providerの同一instance重複除去、生成時Runtimeでの解放を確認する。
個別実体の生成・解放スレッドはそのモジュールの結合テストで確認する。

## 関連ADR

- [ハンドルと世代](RESOURCES-0002-generations-reload.md)
- [実行基盤](../core/CORE-0003-execution-and-jobs.md)
