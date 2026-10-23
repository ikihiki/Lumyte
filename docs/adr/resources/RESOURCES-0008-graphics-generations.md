# ADR-RESOURCES-0008: ResourcesとGraphicsデバイス世代の接続

- 状態: 提案
- 日付: 2026-10-23
- 関連カテゴリ: graphics

## 背景

GPUリソースは生成に使った物理デバイスに属する。Stageに生のデバイスを注入して保持すると、デバイスの復旧後も古い実体で生成し続ける。一方、参照先を常に最新デバイスへ転送すると、旧リソースの解放に別のデバイスを使ってしまう。

Resourcesの論理ハンドルを維持したままGPU出力だけを再構築し、独立したCPU値を使い続けるため、Graphics.Hostingの世代所有とResourcesの構築・公開・解放を接続する。

## 決定

### 配置と責務

`src/Graphics/Lumyte.Resources.Graphics` に接続パッケージを置く。Resources.DependencyInjectionとGraphics.Hostingへ依存し、特定のGraphics backendには依存しない。Resources本体はGraphics型を参照せず、共通の `IResourceRuntimeDependency` だけを扱う。

Graphics.Hostingが物理デバイスの初期化、loss検出、復旧、旧ownerの退役を所有する。ResourceManagerは使用したRuntimeの識別情報と世代を記録し、依存する出力の使用可否と一括公開を管理する。Stageとアプリはbackendの実行スレッド、submission、GPU完了待ち、安全なNative解放を担当する。接続パッケージはこれらを暗黙に実行しない。

### 安定したserviceと構築ごとの世代取得

Stageは安定した `IGraphicsDeviceService` をconstructor injectionで受け取り、各 `BuildAsync` 内で `UseGraphicsAsync` を呼ぶ。この呼び出しは一つの正確な `GraphicsDeviceLease` を取得し、取得時のVersionとservice instanceを構築contextへ記録する。

内部の `GraphicsResourceDependency` は次の対応を行う。

- Identityは登録時のservice instance。別DI scopeの同名serviceは別のproviderとして拒否する。
- VersionとInvalidatedThroughVersionはserviceのStateをそのまま使用する。
- IsAvailableはserviceがReadyのときだけtrueとする。Readyはデバイスが利用可能という意味で、GPUアセットの復元完了ではない。
- Changedの購読はservice.StateChangedへ転送する。通知を再生成要求には変換しない。

文字列の依存IDはManager内で一意とし、Stageが渡すIDと登録IDを一致させる。接続先が違う場合は生成を進めず、取得済みleaseを返して失敗する。Versionだけが一致しても別serviceの使用を認めない。

### BuildとResidentの寿命

`UseGraphicsAsync` の戻り値はcontextからの借用である。Stageは独自にDisposeせず、取得したデバイスと必要な実行contextを出力の解放まで保持する。

既定のResident寿命ではGPU子資源を解放した後でdevice leaseを返す。出力を生成できなかった場合も、Stageは部分生成物をcleanupへ登録するか自ら解放し、leaseだけが先に返ることを防ぐ。ReleaseAsyncやcleanupの失敗時はManagerが未完了の責任とleaseを保持し、TrimAsyncで再試行する。

GPU readbackで得た独立したCPU配列など、構築完了後にデバイスを必要としない出力はBuild寿命を指定できる。完了前の失効は公開を拒否するが、完成済みのCPU値は後のlossで失効させない。GPU実体を出力に含むStageがBuild寿命を指定することは契約違反である。

### loss、再構築、終了

loss通知で旧Ready Versionが失効すると、Resident依存を持つResourceLeaseとその親が使用不能になる。既存leaseの保持は寿命を保証するだけであり、lostしたGPUを利用可能にはしない。Stage実行中や公開直前にlossした候補も公開しない。

アプリは描画・GPU job・mapped領域へのアクセスを止め、旧epochの `WaitForRetirementAsync` で安全なNative解放を確認する。利用側の旧ResourceLease・Snapshotを返し、Stageの子資源解放、contextのdevice lease返却、serviceの `RetireAsync` による旧owner解放を進める。安全性を確認できなければ所有権とメモリ計上を保持する。

`RecoverAsync` で新デバイスがReadyになった後、`RebuildRuntimeAsync([dependencyId])` で既存の論理ハンドルへ新世代を一括公開する。新Capsへの適合、描画対象の復元完了、描画再開はアプリが判断する。構築失敗や公開競合は旧公開mapを置換せず、失効済みの旧値を復活させることもない。

終了時もResourceManagerによるGPU子資源の解放を先に完了し、その後でGraphics serviceのscopeを閉じる。解放に使うownerやdispatcherを先に停止しない。

## 公開APIと利用例

比較元は `split/resources-monitoring` の `a153471`。接続パッケージの公開APIは以下の二つであり、providerの実装型は公開しない。

```diff
+namespace Lumyte.Resources.Graphics
+{
+    public static class ResourceGraphicsBuildExtensions
+    {
+        // 正確なepochを取得してcontextに所有を移す。戻り値のDisposeは呼ばない。
+        // Residentは出力解放まで、Buildは構築完了までdeviceを必要とする契約。
+        public static ValueTask<GraphicsDeviceLease> UseGraphicsAsync(
+            this ResourceBuildContext context, IGraphicsDeviceService service,
+            string dependencyId,
+            ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident,
+            CancellationToken cancellationToken = default);
+    }
+    public static class ResourceGraphicsServiceCollectionExtensions
+    {
+        // 同じDI scopeの名前付きserviceを接続する。デバイスは初期化しない。
+        // service未登録、Manager内の依存ID重複、無効な名前を拒否する。
+        public static IServiceCollection AddResourceGraphics(
+            this IServiceCollection services, string resourceManagerName,
+            string deviceServiceName, string dependencyId);
+    }
+}
```

```csharp
services.AddGraphicsDevice<AppDeviceFactory>("main");
services.AddResources("scene", builder => builder
    .UseSource<AppSource>()
    .AddSourcePipeline<CpuMesh, CpuMeshStage>()
    .AddTransformPipeline<CpuMesh, GpuMesh, GpuMeshStage>());
services.AddResourceGraphics("scene", "main", "graphics.main");

// GpuMeshStageは[FromKeyedServices("main")]のserviceを受け取る。
GraphicsDeviceLease lease = await context.UseGraphicsAsync(
    devices, "graphics.main", cancellationToken: cancellationToken);
IGraphicDevice device = lease.Device;
// このdeviceで生成・uploadし、完了を待ってから出力を返す。
```

例のSource、CPU Stage、GPU Stage、factoryはアプリが実装する。DIを使わない構成では同じ `IResourceRuntimeDependency` 契約でserviceをManagerへ登録し、Stageへ同じinstanceを渡す。

## 検討した代替案

- Resourcesがデバイスを再作成する: backend選択やowner退役をGraphics.Hostingと二重に管理する。接続は世代依存の記録に限定する。
- 生のデバイスをStageへ注入する: 復旧後の生成が古い実体に固定される。安定したserviceから構築ごとにleaseを取得する。
- 最新デバイスへ操作を転送する: 旧子資源と新デバイスの混在を隠す。生成と解放は取得時のepochに固定する。
- lossで全Resourcesを破棄する: デバイスに依存しないCPUデータまで失う。Resident依存の範囲で使用を拒否し、Buildだけの独立出力は維持する。

## 結果と影響

デバイスの世代変更をResourceManagerが把握でき、Stageを再生成せずGPU出力を作り直せる。取得時の世代記録とdevice leaseの確保が構築ごとに必要になるが、通常の値参照でNative queryやGPU待機を追加しない。

旧GPU子資源、device lease、メモリchargeは物理解放まで残る。復旧中には新旧デバイスとアセットのメモリが重なるため、利用側は予算と復旧手順を管理する。接続パッケージだけでGPU操作のスレッド安全性やデバイス復旧の成功を保証しない。

## 検証方針

実際のGraphics.Hostingとfake backendを使い、同一scopeの接続、別scopeの同名service拒否とlease返却、登録不足・重複IDの拒否を検証する。loss後のGPU値拒否、CPU値維持、構築中lossの公開拒否、復旧後の同一handleへの公開を確認する。

退役確認と子資源解放をそれぞれ失敗させ、旧ownerが保持され、再試行後に子 → lease → ownerの順で解放されることを検証する。実backendのloss検出・Native退役安全性はGraphics.Hostingと各backendの検証責任であり、fakeでの成功を実機確認として扱わない。

## 関連ADR

- [Runtimeと実行先](RESOURCES-0003-runtime-execution.md)
- [HotReload](RESOURCES-0004-hot-reload.md)
- [デバイス世代とloss復旧](../graphics/GRAPHICS-0016-device-di-and-loss-recovery.md)
