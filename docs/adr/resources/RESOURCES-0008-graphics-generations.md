# ADR-RESOURCES-0008: ResourcesとGraphicsデバイス世代の接続

- 状態: 提案
- 日付: 2026-10-23
- 関連カテゴリ: graphics

## 背景

GPUリソースは生成に使った物理デバイスに属する。Stageに生のデバイスを注入して保持すると、デバイスの復旧後も古い実体で生成し続ける。一方、参照先を常に最新デバイスへ転送すると、旧リソースの解放に別のデバイスを使ってしまう。

Resourcesの論理ハンドルを維持したままGPU出力だけを再構築し、独立したCPU値を使い続けるため、Graphics.Hostingの世代所有とResourcesの構築・公開・解放を接続する。

## 決定

### 配置と責務

`src/Graphics/Lumyte.Resources.Graphics` に接続パッケージを置く。Resources.DependencyInjectionとGraphics.Hostingへ依存し、特定のGraphics backendには依存しない。Resources本体はGraphics型を参照せず、共通の `IResourceRuntime` と `IResourceRuntimeDependency` を扱う。

公開する `GraphicsResourceRuntime` がStageの実行環境となり、共有のExecutor、安定したdevice service、現在世代の取得、旧epochの退役確認をまとめる。CPU Stageは既定Runtimeを使い、GPU Stageには出力型ごとにGraphics Runtimeを割り当てる。複数の名前付きdeviceを別Stageに割り当てることもできる。

Graphics.Hostingが物理デバイスの初期化、loss検出、復旧、旧ownerの退役を所有する。ResourceManagerはRuntimeのExecutorとDependenciesを登録時に収集・検証し、選択済み環境と使用した世代を記録する。Stageの構築・解放の手順、依存する出力の使用可否と一括公開もManagerが管理する。Stageは選択された環境で個別のGPU生成、submission、GPU完了待ちを記述する。Executorによるジョブ配送はbackend固有の同期制約を変更しない。

### 安定したserviceと構築ごとの世代取得

Stageは `IResourceSourceStage<T, GraphicsResourceRuntime>` または `IResourceTransform<TInput, TOutput, GraphicsResourceRuntime>` を宣言し、BuildAsyncの型付きruntime引数で選択済み環境を受け取り、`AcquireAsync(context, ...)` を呼ぶ。同じRuntime instanceの依存構築は一つのジョブで進み、別のRuntimeへの切り替えと独立ロードでジョブを分ける。Runtimeは安定したserviceを保持し、毎回現在の正確な `GraphicsDeviceLease` を取得する。取得時のVersionとservice instanceを構築contextへ記録するため、deviceをStageへキャッシュしない。

Managerは内容世代に選択済みRuntimeを保存し、解放時もそのRuntimeを型付きReleaseAsyncへ渡す。解放だけのために出力へRuntimeを重ねて保存する必要はない。実際のdeviceとepochのLeaseは、出力またはcleanupが保持する。出力が完成する前の失敗も回収するcleanupは、必要な元RuntimeとLeaseを保持する。新しい構築で使う環境と旧世代の解放環境を混同しない。

`GraphicsResourceRuntime` は自身を固定のDependencies一覧へ含める。Managerは選択した環境から世代providerを一度収集するため、同じproviderを別にDI登録する必要はない。providerは次の対応を行う。

- Identityは登録時のservice instance。別DI rootの同名serviceは別のproviderとして拒否する。
- VersionとInvalidatedThroughVersionはserviceのStateをそのまま使用する。
- IsAvailableはserviceがReadyのときだけtrueとする。Readyはデバイスが利用可能という意味で、GPUアセットの復元完了ではない。
- Changedの購読はservice.StateChangedへ転送する。通知を再生成要求には変換しない。

依存IDはRuntimeの生成時に安定したdevice名から内部生成し、Manager内で一意とする。アプリはIDを命名せず、構築・復旧後も同じRuntimeのIdを使う。IDの書式は公開契約にしない。Runtimeが自身のIDとserviceを使って取得・記録し、別のRuntimeが選択されたcontextへのAcquireを拒否する。低水準のUseGraphicsAsyncも選択済みGraphics Runtimeを必要とする。接続先が違う場合は生成を進めず、取得済みleaseを返して失敗する。Versionだけが一致しても別serviceの使用を認めない。

### BuildとResidentの寿命

`GraphicsResourceRuntime.AcquireAsync` の戻り値はcontextからの借用である。Stageは独自にDisposeせず、取得したデバイスと必要な実行contextを出力の解放まで保持する。

既定のResident寿命ではGPU子資源を解放した後でdevice leaseを返す。出力を生成できなかった場合も、Stageは部分生成物をcleanupへ登録するか自ら解放し、leaseだけが先に返ることを防ぐ。ReleaseAsyncやcleanupの失敗時はManagerが未完了の責任とleaseを保持し、TrimAsyncで再試行する。

Runtimeを選択しただけでは世代へのResident依存を追加しない。GPU readbackで得た独立したCPU配列など、構築完了後にデバイスを必要としない出力はBuild寿命を指定できる。完了前の失効は公開を拒否するが、完成済みのCPU値は後のlossで失効させない。GPU実体を出力に含むStageがBuild寿命を指定することは契約違反である。

### loss、再構築、終了

loss通知で旧Ready Versionが失効すると、Resident依存を持つResourceLeaseとその親が使用不能になる。既存leaseの保持は寿命を保証するだけであり、lostしたGPUを利用可能にはしない。Stage実行中や公開直前にlossした候補も公開しない。

アプリは描画・GPU job・mapped領域へのアクセスを止め、旧epochの `WaitForRetirementAsync` で安全なNative解放を確認する。利用側の旧ResourceLease・Snapshotを返し、Stageの子資源解放、contextのdevice lease返却、serviceの `RetireAsync` による旧owner解放を進める。安全性を確認できなければ所有権とメモリ計上を保持する。

`RecoverAsync` で新デバイスがReadyになった後、選択済み環境の `runtime.Id` を `RebuildRuntimeAsync([runtime.Id])` へ渡し、既存の論理ハンドルへ新世代を一括公開する。新Capsへの適合、描画対象の復元完了、描画再開はアプリが判断する。構築失敗や公開競合は旧公開mapを置換せず、失効済みの旧値を復活させることもない。

終了時もResourceManagerによるGPU子資源の解放を先に完了し、その後でGraphics serviceを終了してDI rootを閉じる。解放に使うownerやdispatcherを先に停止しない。

## 公開APIと利用例

ResourceManagerはDI rootに一つのunkeyed singletonとする。Graphics Runtimeは既存device名ごとのsingletonとし、
アプリ寿命のdevice serviceを借用する。既定環境が未登録なら最初の登録をGraphicsResourceRuntimeとして型で解決し、追加登録で既定環境を置き換えない。
複数deviceを使うStageだけ、GetResourceGraphicsRuntimeへ既存のdevice名を指定する。依存IDの追加指定は不要とする。
`AddGraphicsDevice`は既存利用向けにScopedを既定とするため、Resourcesと接続する場合はSingletonを指定する。
子scopeの終了ではManager・Runtime・deviceを終了しない。

Graphics RuntimeはDIなしでも生成できる公開型とし、Stageの実行環境と世代providerを兼ねる。

```diff
+namespace Lumyte.Resources.Graphics
+{
+    public sealed class GraphicsResourceRuntime : IResourceRuntime, IResourceRuntimeDependency
+    {
+        public GraphicsResourceRuntime(IGraphicsDeviceService devices, IResourceRuntime jobs);
+        public IResourceExecutor Executor { get; }
+        public IReadOnlyList<IResourceRuntimeDependency> Dependencies { get; }
+        public ValueTask<GraphicsDeviceLease> AcquireAsync(ResourceBuildContext context,
+            ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident,
+            CancellationToken cancellationToken = default);
+        public ValueTask WaitForRetirementAsync(ulong epoch, CancellationToken cancellationToken = default);
+        // Id、Identity、Snapshot、ChangedはIResourceRuntimeDependencyに従う。
+    }
+    public static class ResourceGraphicsBuildExtensions
+    {
+        // 正確なepochを取得してcontextに所有を移す。戻り値のDisposeは呼ばない。
+        // Residentは出力解放まで、Buildは構築完了までdeviceを必要とする契約。
+        public static ValueTask<GraphicsDeviceLease> UseGraphicsAsync(
+            this ResourceBuildContext context, IGraphicsDeviceService service,
+            ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident,
+            CancellationToken cancellationToken = default);
+    }
+    public static class ResourceGraphicsServiceCollectionExtensions
+    {
+        // 同じDI rootのsingletonな名前付きserviceを接続する。デバイスは初期化しない。
+        // Manager・service未登録、scoped service、同じdeviceの重複、無効な名前を拒否する。
+        public static IServiceCollection AddResourceGraphics(
+            this IServiceCollection services, string deviceServiceName);
+        public static GraphicsResourceRuntime GetResourceGraphicsRuntime(
+            this IServiceProvider provider, string deviceServiceName);
+    }
+}
```

```csharp
services.AddGraphicsDevice<AppDeviceFactory>("main", ServiceLifetime.Singleton);
services.AddResources(builder => builder
    .UseSource<AppSource>()
    .AddSourcePipeline<CpuMesh, CpuMeshStage>()
    .AddTransformPipeline<CpuMesh, GpuMesh, GpuMeshStage>()
    .UseStageRuntime<GpuMesh, GraphicsResourceRuntime>());
services.AddResourceGraphics("main");

// GpuMeshStageは登録で選ばれたRuntimeを使う。
// BuildAsyncのGraphicsResourceRuntime runtime引数を使用する。
GraphicsDeviceLease lease = await runtime.AcquireAsync(
    context, cancellationToken: cancellationToken);
IGraphicDevice device = lease.Device;
// このdeviceで生成・uploadし、完了を待ってから出力を返す。
```

例のSource、CPU Stage、GPU Stage、factoryはアプリが実装する。DIを使わない構成では `new GraphicsResourceRuntime(devices, jobs)` を作り、Managerの `stageRuntimes` に `new ResourceStageRuntime(typeof(GpuMesh), graphics)` を渡す。選択したRuntimeのDependenciesから世代providerもManagerへ自動登録される。deviceの実世代とBuild／Resident寿命は各Acquireで記録する。

## 検討した代替案

- Resourcesがデバイスを再作成する: backend選択やowner退役をGraphics.Hostingと二重に管理する。実行環境はHostingの安定したserviceを利用し、物理デバイスの所有を二重化しない。
- 生のデバイスをStageへ注入する: 復旧後の生成が古い実体に固定される。安定したserviceから構築ごとにleaseを取得する。
- 最新デバイスへ操作を転送する: 旧子資源と新デバイスの混在を隠す。生成と解放は取得時のepochに固定する。
- lossで全Resourcesを破棄する: デバイスに依存しないCPUデータまで失う。Resident依存の範囲で使用を拒否し、Buildだけの独立出力は維持する。

## 結果と影響

デバイスの世代変更をResourceManagerが把握でき、Stageを再生成せずGPU出力を作り直せる。取得時の世代記録とdevice leaseの確保が構築ごとに必要になるが、通常の値参照でNative queryやGPU待機を追加しない。

旧GPU子資源、device lease、メモリchargeは物理解放まで残る。復旧中には新旧デバイスとアセットのメモリが重なるため、利用側は予算と復旧手順を管理する。接続パッケージだけでGPU操作のスレッド安全性やデバイス復旧の成功を保証しない。

## 検証方針

実際のGraphics.Hostingとfake backendを使い、型で選んだ既定環境とdevice名で選んだ環境の一致、追加登録後の既定環境維持、異なるdeviceの依存ID分離、Stageごとの既定Runtime／複数Graphics Runtimeの選択、子scope間のsingleton共有、別DI rootの同名service拒否とlease返却、scoped device・登録不足・重複deviceの拒否を検証する。型付きReleaseAsyncに生成時と同じRuntimeが渡され、別途provider登録なしでlossを監視できることも確認する。loss後のGPU値拒否、CPU値維持、構築中lossの公開拒否、復旧後の同一handleへの公開を確認する。

退役確認と子資源解放をそれぞれ失敗させ、旧ownerが保持され、再試行後に子 → lease → ownerの順で解放されることを検証する。実backendのloss検出・Native退役安全性はGraphics.Hostingと各backendの検証責任であり、fakeでの成功を実機確認として扱わない。

## 関連ADR

- [Runtimeと実行先](RESOURCES-0003-runtime-execution.md)
- [HotReload](RESOURCES-0004-hot-reload.md)
- [デバイス世代とloss復旧](../graphics/GRAPHICS-0016-device-di-and-loss-recovery.md)
