# ADR-RESOURCES-0006: runtime依存とGraphicsデバイス世代を追跡するGPU出力

- 状態: 提案
- 日付: 2026-10-20

## 背景

GPU出力は物理デバイスに属する。ロスト後のGPU値を取得させず、
旧デバイスの解放前に子リソースと実行中の利用を終了させる必要がある。

## 決定

共通のruntime依存契約をCoreに導入し、Graphics連携もこのPRで追加する。
providerは単調増加するversion、利用可否、不可逆の失効watermarkを提供する。
Stageは使用したproviderのidentity・version・Build/Resident寿命を記録する。
Resident依存の失効はその親へ伝播する。Buildだけで使用した独立出力は構築完了後も利用を継続できる。
取得時にもproviderを観測し、通知順に依存しない。構築中のversion変更は公開を拒否する。
provider契約違反はmanager操作へ報告し、既存のhandleは復旧後のRebuildRuntimeAsyncで新世代へ接続する。
providerとサービスはDIまたは呼び出し側から借用し、購読解除をmanager終了時に行う。
ファイルHotReloadだけを扱うADR-RESOURCES-0002へruntime対応は要求しない。

Graphics.Hostingの名前付きIGraphicsDeviceServiceへDIで接続し、
service instanceとversionをmanagerのruntime依存に対応させる。
UseGraphicsAsyncは正確なデバイスleaseを取得し、依存を記録してcontextのcleanupへ返却を登録する。
Stageが返されたleaseを独自にDisposeしてはならない。

Resident依存のGPU値はロストで失効し、親へ伝播する。CPU値はruntime依存を持たない限り維持する。
アプリがretirement確認、旧GPU値の解放、旧owner退役、復旧と再構築を制御する。
GPU childの解放をepoch lease返却より先に完了し、失敗時は所有を保持して再試行する。
コマンド配送、submission、GPU完了待ちはStageと利用側の責任とする。
デバイス本体のロスト通知・回復実装は既存Graphics.Hostingを使用する。

## APIと契約

比較元はこのPRの依存ブランチ。以下は判断対象の追加・変更APIの抜粋。

```diff
+namespace Lumyte.Resources
+{
+    public interface IResourceRuntimeDependency
+    {
+        string Id { get; }
+        object Identity { get; }
+        ResourceRuntimeSnapshot Snapshot { get; }
+        event EventHandler? Changed;
+    }
+    public readonly record struct ResourceRuntimeSnapshot(ulong Version, bool IsAvailable, ulong InvalidatedThroughVersion);
+    public sealed class ResourceUnavailableException : InvalidOperationException
+    {
+        public string DependencyId { get; }
+        public ulong Version { get; }
+    }
+    public sealed class ResourceBuildContext
+    {
+        public void TrackRuntimeDependency(string dependencyId, ulong expectedVersion, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident, object? providerIdentity = null);
+    }
+    public sealed class ResourceManager
+    {
-        public ResourceManager(string name, IResourceSource source, IEnumerable<IResourceStage> stages, IMemoryManager memory, ResourceReloadOptions? reloadOptions = null);
+        public ResourceManager(string name, IResourceSource source, IEnumerable<IResourceStage> stages, IMemoryManager memory, IEnumerable<IResourceRuntimeDependency>? runtimeDependencies = null, ResourceReloadOptions? reloadOptions = null);
+        public ValueTask<ResourceReloadResult> RebuildRuntimeAsync(IReadOnlyCollection<string> dependencyIds, CancellationToken cancellationToken = default);
+    }
+}
+namespace Lumyte.Resources.DependencyInjection
+{
+    public sealed class ResourceRegistrationBuilder
+    {
+        public ResourceRegistrationBuilder UseRuntimeDependency<TDependency>(string dependencyId) where TDependency : class, IResourceRuntimeDependency;
+    }
+    public static class ResourceServiceCollectionExtensions
+    {
+        public static IServiceCollection AddResourceRuntimeDependency<TDependency>(this IServiceCollection services, string managerName, string dependencyId, Func<IServiceProvider, TDependency> factory) where TDependency : class, IResourceRuntimeDependency;
+    }
+}
+namespace Lumyte.Resources.Graphics
+{
+    public static class ResourceGraphicsBuildExtensions
+    {
+        // 戻り値はcontextからの借用。childを解放してからcontextが返却する。
+        public static ValueTask<GraphicsDeviceLease> UseGraphicsAsync(this ResourceBuildContext context, IGraphicsDeviceService service, string dependencyId, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident, CancellationToken cancellationToken = default);
+    }
+    public static class ResourceGraphicsServiceCollectionExtensions
+    {
+        public static IServiceCollection AddResourceGraphics(this IServiceCollection services, string resourceManagerName, string graphicsDeviceName, string dependencyId);
+    }
+}
```

## 検討した代替案

GPU値を通常のファイル内容だけで識別すると、ロスト後も取得できてしまう。
managerがデバイスを直接作成すると、バックエンド・画面・DI scopeの責任が混ざる。

## 結果と影響

論理handleを維持してGPU出力だけ再構築できる。StageとアプリはGPU完了と退役の順序を管理する必要がある。
復旧は暗黙に実行せず、失敗や再試行を利用側へ公開する。

## 検証方針

共通providerでversion進行、旧lease保持、通知順、契約違反、Build/Residentの失効伝播、
復旧と同一handleへの再接続、DI登録名の一致を検証する。
Hosting serviceとテストdeviceで、ロスト、同一handleへの復旧、旧epoch保持、
Build/Residentの違い、child解放失敗の再試行、異なるservice instanceの拒否を検証する。

## 関連ADR

- [ADR-RESOURCES-0002](RESOURCES-0002-generations-reload.md)
