# ADR-RESOURCES-0001: 出力型で解決する共有Pipelineと所有権

- 状態: 提案
- 日付: 2026-10-20

## 背景

同じ文書から異なる型を取得し、CPU中間型を経由して変換したい。
重複読み込み、動的な依存循環、解放順序、DIサービスとデータの所有者を明確にする必要がある。

## 決定

manager内では正確な出力型のRuntimeTypeHandleにつき一つのPipelineを登録する。
ResourceKeyは型とResourceAddress（Id・Subresource・Variant）で論理出力を指定する。
型handleはプロセス内だけで使用し、永続化しない。
Transformの一次入力はGetInputでアドレスを決め、追加依存はRequireAsyncで記録する。
構築中の同じキーを共有し、依存辺の追加時に循環を拒否する。
Sourceは観測したRevisionの入力descriptorを貸し、取得したstreamはStageが閉じる。
読み込みの整合性と変更検出の限界はSourceが定める。入力ファイルの旧版保存は要求しない。

ResourceManagerはIResourceStageの集合を直接受け取る。IResourceSourceStageとIResourceTransformは
IResourceStageを継承し、型情報・型付き呼び出し・主入力のRequireAsyncを既定実装で提供する。
ResourcePipeline抽象クラスやSourcePipeline/TransformPipelineのラッパーは設けない。
一つのStage instanceは一つの出力型と主入力の組を表す。複数型の処理は別Stageまたは閉じたgeneric型に分ける。
DIはIResourceStageとして実装型を直接登録してconstructor injectionを行う。
同一instanceを複数のservice登録で所有せず、破棄は一度だけ行う。
DIを使わない場合もStage instanceの集合を同じconstructorへ渡し、呼び出し側がサービス寿命を管理する。

Source・Stageは名前付きDI scopeまたは呼び出し側から借用する。managerが所有するのは出力、snapshot、
memory scope、cleanup、依存への利用権である。leaseを返してからmanagerを閉じる。
Build依存は構築完了後、Resident依存は親の解放後に返す。
出力、補助cleanup、snapshot、memory scope、依存の順で解放する。
解放失敗時は所有を維持し、TrimAsyncで未完了部分だけを再試行する。
共有構築の待機キャンセルは他の待機者の構築をキャンセルしない。

ResourceGenerationの寿命はboolの組み合わせではなく、排他的なenum状態で表す。
正常系はBuilding → Ready → Releasing → Releasedと遷移する。
構築失敗時はBuildingからReleasingへ進み、解放失敗時はCleanupFailedへ遷移する。
TrimAsyncでCleanupFailed → RetryingCleanup → Releasedを試み、再失敗ならCleanupFailedへ戻る。
再試行中も同じキーの新規構築を拒否する。公開中かどうかはmanagerの公開mapで管理する。
解放の進捗は別のenumでOutput → Resources → Completeと記録し、失敗しても保持する。
Resources内の個別cleanup・入力・memory・依存は、それぞれの完了記録と所有情報から再開する。

Memoryのスレッド別チャンクを使用し、外部割り当ては予約とchargeで計上する。
ReclaimAsyncは共有poolのReleasedBytes差分を報告するため、同時に返された容量も含む。
managerの公開状態更新には短い同期区間を使い、I/O・Stage・解放callbackをその中で実行しない。
世代切替、非所有handle、監視、ストリーミング、具体的なSource、Graphics連携は後続PRで扱う。

複合リソースの構造は各データ型が定め、子への具体的な参照を保持する。
managerは汎用のサブリソース一覧や出力型一覧を要求しない。
Stageが借用した子を親に保持する場合はResident依存を記録して寿命を保証する。
後続の非所有handleを使う場合も、複合型の構造と常駐の所有者は利用側で定義する。

## APIと契約

比較元はmain (`b5702c0`)。対象APIは新規追加する。

```diff
+namespace Lumyte.Resources
+{
+    public readonly record struct ResourceId { public ResourceId(string value); public string? Value { get; } }
+    public readonly record struct ResourceAddress { public ResourceAddress(ResourceId id, string subresource = "", string variant = ""); }
+    public readonly record struct ResourceKey<T> where T : class
+    {
+        public ResourceKey(string id, string subresource = "", string variant = "");
+        public ResourceKey(ResourceId id, string subresource = "", string variant = "");
+        public ResourceKey(ResourceAddress address);
+        public ResourceAddress Address { get; }
+        public RuntimeTypeHandle TypeHandle { get; }
+    }
+    public sealed class ResourceLease<T> : IDisposable where T : class
+    {
+        public ResourceKey<T> Key { get; }
+        public ulong Revision { get; }
+        public bool IsUsable { get; }
+        // 返却後のアクセスはObjectDisposedException。
+        public T Value { get; }
+        public void Dispose();
+    }
+    public interface IResourceSource
+    {
+        ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default);
+        ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default);
+    }
+    public abstract class ResourceSourceSnapshot : IAsyncDisposable
+    {
+        public abstract ResourceId Id { get; }
+        public abstract string Revision { get; }
+        public abstract long? Length { get; }
+        public abstract bool CanReadRange { get; }
+        public abstract ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default);
+        public abstract ValueTask DisposeAsync();
+    }
+    public interface IResourceStage
+    {
+        Type OutputType { get; }
+        Type? InputType { get; }
+        ValueTask<object> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken);
+        ValueTask ReleaseAsync(object resource);
+    }
+    public interface IResourceSourceStage<T> : IResourceStage where T : class
+    {
+        new ValueTask<T> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken);
+        ValueTask ReleaseAsync(T resource);
+    }
+    public interface IResourceTransform<TInput, TOutput> : IResourceStage where TInput : class where TOutput : class
+    {
+        ResourceDependencyLifetime InputLifetime { get; }
+        ResourceAddress GetInput(ResourceAddress output);
+        ValueTask<TOutput> BuildAsync(TInput input, ResourceBuildContext context, CancellationToken cancellationToken);
+        ValueTask ReleaseAsync(TOutput resource);
+    }
+    public sealed class ResourceBuildContext
+    {
+        public ResourceAddress Address { get; }
+        // 戻り値は借用。managerが依存の利用権を管理する。
+        public ValueTask<T> RequireAsync<T>(ResourceKey<T> key, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident, CancellationToken cancellationToken = default) where T : class;
+        public ValueTask<ResourceSourceSnapshot> OpenSourceAsync(ResourceId id, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Build, CancellationToken cancellationToken = default);
+        public IMemoryScope GetMemoryScope(ResourceDependencyLifetime lifetime);
+        public void RegisterCleanup(Func<ValueTask> cleanup, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Build);
+    }
+    public sealed class ResourceManager : IAsyncDisposable, IMemoryReclaimer
+    {
+        public ResourceManager(string name, IResourceSource source, IEnumerable<IResourceStage> stages, IMemoryManager memory);
+        public ValueTask<ResourceLease<T>> AcquireAsync<T>(ResourceKey<T> key, CancellationToken cancellationToken = default) where T : class;
+        public ValueTask<int> TrimAsync(CancellationToken cancellationToken = default);
+        public ValueTask<ulong> ReclaimAsync(MemoryPoolId pool, ulong targetBytes, CancellationToken cancellationToken = default);
+        // 外部leaseや構築が残る場合はInvalidOperationException。
+        public ValueTask DisposeAsync();
+    }
+}
+namespace Lumyte.Resources.DependencyInjection
+{
+    public static class ResourceServiceCollectionExtensions
+    {
+        public static IServiceCollection AddResources(this IServiceCollection services, string name, Action<ResourceRegistrationBuilder> configure);
+    }
+    public sealed class ResourceRegistrationBuilder
+    {
+        public ResourceRegistrationBuilder UseSource<TSource>(ServiceLifetime lifetime = ServiceLifetime.Scoped) where TSource : class, IResourceSource;
+        public ResourceRegistrationBuilder AddSourcePipeline<TOutput, TStage>(ServiceLifetime lifetime = ServiceLifetime.Scoped) where TOutput : class where TStage : class, IResourceSourceStage<TOutput>;
+        public ResourceRegistrationBuilder AddTransformPipeline<TInput, TOutput, TStage>(ServiceLifetime lifetime = ServiceLifetime.Scoped) where TInput : class where TOutput : class where TStage : class, IResourceTransform<TInput, TOutput>;
+    }
+}
+namespace Lumyte.Memory
+{
+    public sealed record MemorySnapshot { public ulong ReleasedBytes { get; init; } }
+}
```

## 検討した代替案

文字列のPipeline名は型と設定の食い違いを許すため採用しない。
Stageごとの独立キャッシュでは横断的な依存循環や解放順序を管理しにくい。
全依存を出力寿命まで保持すると一時デコード領域を回収できない。
DI専用のラッパーは手動構築時に同じ橋渡し処理の再実装を要求するため採用しない。

## 結果と影響

型安全な取得と複合documentの共有ができる。一つの型に複数の登録はできず、条件はVariantで表す。
依存を借用するStageには寿命契約の理解が必要で、借用値そのものを別の所有出力として返すことは禁止する。

## 検証方針

メモリSourceを使い、多段変換、diamond共有、構築キャンセル、静的・動的循環、DI寿命、
解放失敗の再試行、異なるAssemblyLoadContextの型識別を公開APIで検証する。
