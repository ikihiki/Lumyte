# ADR-RESOURCES-0001: 出力型で解決する共有Pipelineと所有権

- 状態: 提案
- 日付: 2026-10-20

## 背景

同じ文書からMeshやMaterialなど異なる型を取得し、CPU中間型を共有して変換したい。
型ごとに独立したキャッシュを置くと、重複読み込み、動的な依存循環、入力と出力の解放順序を横断して管理できない。
DIサービスの寿命と構築したデータの寿命も異なるため、取得・公開・解放の所有者を一つに定める。

## 決定

### 型の解決と責務

`Lumyte.Resources` に型付きStage、Source契約、ResourceManagerを置き、メモリ管理は `Lumyte.Memory` を使用する。
`Lumyte.Resources.DependencyInjection` が標準DIとの接続を提供する。Sourceの取得方式とデータ形式は各実装が定める。

manager内では正確な出力型のRuntimeTypeHandleにつき一つのStageを登録する。
ResourceKeyは出力型とResourceAddress（Id・Subresource・Variant）で論理出力を指定する。
型handleはプロセス内の識別だけに使い、型名の一致や継承関係で代替しない。
入力・出力には非nullの参照型と値型を扱い、readonly structのラッパーもその型で登録する。
異なるAssemblyLoadContextの同名型も別の型であり、型handleやキーを永続形式としない。
VariantはStageが解釈する設定の識別子であり、別のStage登録を自動選択するものではない。

ResourceManagerはIResourceStageの集合を直接受け取る。
IResourceSourceStageとIResourceTransformの既定実装が型情報、型付き呼び出し、主入力の取得を提供するため、Pipeline用のラッパーは置かない。
一つのStage instanceは一つの出力型と主入力の組を表す。複数型の処理は別Stageまたは閉じたgeneric型へ分ける。
TransformはGetInputで主入力のアドレスを決定し、追加の入力をRequireAsyncで取得する。

### 構築から公開まで

登録時に出力型の重複、開いたgeneric型、nullable値型、ref structなどobjectへ保持できない型、未登録の主入力、静的な型依存循環を拒否する。
未登録型の取得はNotSupportedExceptionとし、Stageを暗黙に生成しない。
同じキーの進行中ロードは共有し、構築view内の依存辺を追加する時点で動的な循環を拒否する。
互いに別の非同期処理から依存を追加しても、相互待機を始める前に循環を検出する。

Stageは利用可能な出力を返す。必要な転送や初期化が完了する前の実体を公開しない。
managerはStageが要求した依存とSource操作の完了を待ち、Build寿命の所有物を返す。
入力Revisionと構築中に使った公開値の整合性を検証してから、完了した候補を公開mapへ反映する。
変更や競合を観測した場合は候補を公開せず例外を返し、呼出側が取得を再試行する。

Sourceは観測したRevisionの入力descriptorを貸す。同じ構築viewで同じIdに異なるRevisionを観測した場合は失敗する。
Snapshotという名前は入力の履歴保存を保証しない。読取りの整合性と変更検出の限界はSourceが定める。
Stageが開いたstreamはStageが閉じ、snapshotの返却前に読み取りを終える。

### 所有権と解放

Source・Stage・IMemoryManagerは借用する。managerは出力、snapshot、memory scope、補助cleanupと依存への利用権を所有する。
RequireAsyncの戻り値は借用であり、それ自体を別キーの所有出力として返すことは禁止する。
親の内部に子への参照を残す場合はResident依存にする。managerは汎用サブリソース一覧を要求せず、複合データ型が構造を定義する。

Build依存は構築完了時、Resident依存は親出力の解放後に返す。
同じ依存をBuildとResidentで要求した場合は一つに共有してResidentへ昇格する。
BuildContextは一つの構築だけで有効で、構築終了後の追加操作をObjectDisposedExceptionで拒否する。

解放順は出力 → 補助cleanup（登録の逆順）→ snapshot → memory scope → 依存の利用権とする。
出力やcleanupの失敗時には、後段の所有物と計上メモリを保持する。
TrimAsyncまたは終了の再試行で未完了部分を再開し、成功済みcallbackを再実行しない。
一つのcallback内の部分成功はcallback自身が記録する必要がある。
値型の出力は世代へ一度boxして保持し、取得時は追加のboxを作らずコピーする。コピーは内部参照の所有を移さず、利用期間も延ばさない。Stageは解放の進行状態を元の所有オブジェクトへ保存し、値型のfield更新だけに頼らない。借用した内部参照を別の所有出力として解放することは禁止し、参照型の同一instance検出を値の等価比較へ置き換えない。

内部の世代状態はBuilding → Ready → Releasing → Releasedを基本とし、構築失敗ではBuildingからReleasingへ進む。
解放失敗はCleanupFailed、再試行中はRetryingCleanupとし、成功するとReleasedへ進む。
失敗した世代のcleanupが残る間は同じキーを再構築せず、失敗した所有物を新しい構築で上書きしない。
公開の有無は公開map、解放済みの工程は別の進捗情報で管理する。

### 非同期処理・DI・終了

公開状態と所有数の変更には短い同期区間を使い、I/O、Stage、解放callbackはその外で実行する。
異なるキーのStage呼び出しは重なるため、共有Stageの可変状態は実装側で同期する。
AcquireAsyncの呼出側キャンセルはその待機だけを止め、他の待機者と共有する構築を打ち切らない。
待機者がいなくなっても進行中処理と生成物の所有は完了まで追跡する。
Lease.Disposeは所有数の返却だけを行い、実体の破棄やI/Oを実行しない。
実体の解放はTrimAsyncとDisposeAsyncで進める。

DI rootにつき一つのsingleton ResourceManagerへ全出力型のPipelineを登録する。SourceとStageもアプリ寿命で借用し、子scopeからも同じManagerを使う。
Manager・同一出力型の重複登録と設定完了後の変更を拒否する。
IMemoryManagerは呼出側が登録する。手動構築でも同じStage集合を渡し、呼出側が借用サービスの寿命を維持する。

終了前に外部Leaseを返し、ロードと操作の完了を待つ。残存時のDisposeAsyncはInvalidOperationExceptionで拒否する。
終了のcleanupに失敗した場合は所有を残してDisposeAsyncを再試行できるが、新規取得は再開しない。
managerの終了が完了してから各systemとDI rootまたは借用サービスを閉じる。

## APIと契約

比較元はmain (`b5702c0`)。対象APIは新規追加する。

```diff
+namespace Lumyte.Resources
+{
+    // Ordinal比較のSource相対パス。空要素、..、URI構文などは拒否する。
+    public readonly record struct ResourceId { public ResourceId(string value); public string? Value { get; } }
+    public readonly record struct ResourceAddress
+    {
+        public ResourceAddress(ResourceId id, string subresource = "", string variant = "");
+        public ResourceId Id { get; }
+        // Stage固有の要素指定と変換設定の識別子。ファイルパスとは解釈しない。
+        public string Subresource { get; }
+        public string Variant { get; }
+    }
+    public readonly record struct ResourceKey<T> where T : notnull
+    {
+        public ResourceKey(string id, string subresource = "", string variant = "");
+        public ResourceKey(ResourceId id, string subresource = "", string variant = "");
+        public ResourceKey(ResourceAddress address);
+        public ResourceAddress Address { get; }
+        public RuntimeTypeHandle TypeHandle { get; }
+    }
+    public sealed class ResourceLease<T> : IDisposable where T : notnull
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
+        // 不在はnull。RevisionはSourceが定義する不透明な比較値。
+        ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default);
+    }
+    public abstract class ResourceSourceSnapshot : IAsyncDisposable
+    {
+        public abstract ResourceId Id { get; }
+        public abstract string Revision { get; }
+        public abstract long? Length { get; }
+        public abstract bool CanReadRange { get; }
+        // streamは呼出側が所有し、snapshot返却前に閉じる。
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
+    public interface IResourceSourceStage<T> : IResourceStage where T : notnull
+    {
+        new ValueTask<T> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken);
+        ValueTask ReleaseAsync(T resource);
+    }
+    public interface IResourceTransform<TInput, TOutput> : IResourceStage where TInput : notnull where TOutput : notnull
+    {
+        ResourceDependencyLifetime InputLifetime { get; }
+        ResourceAddress GetInput(ResourceAddress output);
+        ValueTask<TOutput> BuildAsync(TInput input, ResourceBuildContext context, CancellationToken cancellationToken);
+        ValueTask ReleaseAsync(TOutput resource);
+    }
+    public enum ResourceDependencyLifetime { Build, Resident }
+    public sealed class ResourceBuildContext
+    {
+        public ResourceAddress Address { get; }
+        public RuntimeTypeHandle OutputTypeHandle { get; }
+        // 戻り値は借用。managerが依存の利用権を管理する。
+        public ValueTask<T> RequireAsync<T>(ResourceKey<T> key, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident, CancellationToken cancellationToken = default) where T : notnull;
+        public ValueTask<ResourceSourceSnapshot> OpenSourceAsync(ResourceId id, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Build, CancellationToken cancellationToken = default);
+        public IMemoryScope GetMemoryScope(ResourceDependencyLifetime lifetime);
+        // 所有権を移す。callbackは再試行可能にし、完了した仕事を二重解放しない。
+        public void RegisterCleanup(Func<ValueTask> cleanup, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Build);
+    }
+    public sealed class ResourceManager : IAsyncDisposable, IMemoryReclaimer
+    {
+        public ResourceManager(string name, IResourceSource source, IEnumerable<IResourceStage> stages, IMemoryManager memory);
+        // 呼出側の取消は待機だけを止め、共有構築は完了まで所有する。
+        public ValueTask<ResourceLease<T>> AcquireAsync<T>(ResourceKey<T> key, CancellationToken cancellationToken = default) where T : notnull;
+        // 未使用世代と失敗したcleanupを処理し、完全解放した世代数を返す。
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
+        public static IServiceCollection AddResources(this IServiceCollection services, Action<ResourceRegistrationBuilder> configure);
+    }
+    public sealed class ResourceRegistrationBuilder
+    {
+        // singleton SourceとStageを登録する。登録確定後の変更は拒否する。
+        public ResourceRegistrationBuilder UseSource<TSource>() where TSource : class, IResourceSource;
+        public ResourceRegistrationBuilder AddSourcePipeline<TOutput, TStage>() where TOutput : notnull where TStage : class, IResourceSourceStage<TOutput>;
+        public ResourceRegistrationBuilder AddTransformPipeline<TInput, TOutput, TStage>() where TInput : notnull where TOutput : notnull where TStage : class, IResourceTransform<TInput, TOutput>;
+    }
+}
+namespace Lumyte.Memory
+{
+    public sealed record MemorySnapshot { public ulong ReleasedBytes { get; init; } }
+}
```

## 性能と容量

同一キーのロードとdiamond依存を共有し、Build用の入力とメモリを公開前に返すことで重複I/Oと一時領域の常駐を抑える。
memory scopeは必要になった時点で作り、Memoryのチャンクと予約・chargeによって内部領域と外部確保を計上する。
ReclaimAsyncは共有poolのReleasedBytes差分を報告するため、同時に返された容量を含み、要求量ぴったりの回収は保証しない。

公開更新ではmapのコピー、動的依存の追加では循環探索が発生する。
ロード処理を無割り当てや一定時間とは規定しない。測定では同時取得数、依存の頂点・辺数、公開mapのサイズ、cleanup失敗時の保持量を分ける。
長寿命のLeaseとResident依存がある値は、メモリ圧力があっても強制解放しない。

## 検討した代替案

- 文字列のPipeline名による解決は出力型との食い違いを許すため、型を登録キーとする。
- Stageごとの独立キャッシュは横断的な循環と解放順を扱えないため、所有権をmanagerへ集約する。
- 全依存を出力寿命まで保持すると一時デコード領域を回収できないため、BuildとResidentを分ける。
- DI専用のPipelineラッパーは手動構築にも同じ仲介を要求するため、Stageの既定実装で型付き境界を接続する。
- cleanup失敗時に所有記録を捨てるとNative実体とメモリ計上が失われるため、失敗を伝えて再試行可能な状態を残す。

## 結果と影響

型安全な取得と複合documentの共有を同じ所有モデルで扱える。
利用側は借用と所有、およびBuildとResidentを正しく選択し、解放callbackを再試行可能にする必要がある。
入力の履歴保存、データ形式ごとの変換、Native実行先の制約はSourceとStageの責務として残る。

## 検証方針

- 多段変換とdiamond共有で構築回数、Build入力の早期返却、親から子への解放順を確認する。
- 同時Acquireの共有、待機キャンセルの分離、静的・動的循環の拒否、入力Revisionの競合を確認する。
- 借用値の所有出力への転用を拒否し、BuildからResidentへの昇格を一つの依存として保持する。
- 構築・出力解放・補助cleanupをそれぞれ失敗させ、メモリ計上と依存が維持され、成功済み処理を再実行しないことを確認する。
- DI root間の分離と子scope間の同一性、登録確定後の変更拒否、借用サービスの破棄回数、異なるAssemblyLoadContextの型識別を検証する。
