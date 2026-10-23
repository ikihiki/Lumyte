# ADR-RESOURCES-0002: 論理ハンドルと内容世代

- 状態: 提案
- 日付: 2026-10-23

## 背景

ECSのunmanaged値からリソースを参照し、描画や処理の間は具体的な値を独立して保持したい。
値への参照を長期間持つだけでは、論理リソースを使い続ける期間と、一回の処理が特定の内容を使う期間を分けられない。
管理対象を示す小さな識別子、論理的な常駐の所有者、内容世代の所有者を別の契約とする。

## 決定

### 識別子と所有者

ResourceReferenceは論理リソースのslotを所有し、ResourceHandleはそのslotを非所有で識別する。
ResourceLeaseは取得した値とRevisionを固定し、ResourceSnapshotは複数の取得が参照する公開mapを固定する。
これらはすべてResourceManagerの所有管理へ接続し、別のキャッシュを持たない。

ResourceHandleはmanager識別子64 bit、slot32 bit、slot世代32 bitからなる16 byteのunmanaged structとする。
Tは正確な出力型の指定であり、handle内にTやmanaged参照を保持しない。
manager識別子はプロセス内で再利用せず、slot再利用時はslot世代を増やす。
slot世代の上限に達したslotは再利用しない。default handleは無効であり、識別子の永続化やプロセス間転送は行わない。

slot世代は論理所有の再利用を検出する番号であり、内容のRevisionや公開mapのPublicationVersionとは別である。
handleのコピーでは所有数を増やさない。ECSにhandleだけを格納する場合も、別の所有者がReferenceを保持する必要がある。
最後のReferenceを返すとslotは失効し、同じキーを再取得しても古いhandleは復活しない。

### 取得と公開状態のスナップショット

Manager.AcquireAsyncはキーをロードしてReferenceを返す。
Manager.TryAcquire(handle, out reference)は有効なslotから追加のReferenceを取得する。
それぞれのReferenceは独立した論理所有者であり、一つの返却で他のReferenceを失効させない。

Reference.Acquire/TryAcquireはI/OやStage実行を行わず、現在の値とRevisionを保持するLeaseを発行する。
LeaseはReferenceやSnapshotから独立して所有を持つため、それらを返した後も取得済みの値を使用できる。
公開値そのものは利用側が直接書き換えない。Leaseはwriterを止めるmutexや値のdeep copyを提供しない。

CaptureSnapshotは呼出時点の公開mapとPublicationVersionを捕捉し、そのmap内の全世代を保持する。
複数のReferenceから同じSnapshotを指定して取得すると、一つの公開状態に存在した値を選べる。
scopeの開始後に追加されたキーは、そのscopeから取得できない。
公開mapを保持していても論理slotの有効性は毎回検証するため、最後のReference返却後に古いhandleから新しいLeaseは取得できない。

### 失効・失敗・スレッド契約

default、別manager、失効したslot、再利用前のslot世代、異なる出力型、対象mapに存在しない値はTryAcquireでfalseを返す。
Reference.Acquireで値が利用できない場合はInvalidOperationExceptionとする。
返却済みReferenceのHandle/Acquire/TryAcquire、返却済みLeaseのValue、返却済みSnapshotのTryAcquireはObjectDisposedExceptionを返す。
IsAvailableとIsUsableは所有の返却後にfalseとなる。非defaultであるだけではhandleの有効性を保証しない。

manager内のslot・公開map・所有数は同じ同期境界で検証と更新を行う。
同じReference、Lease、Snapshotについて利用とDisposeを並行実行してはならない。
Disposeは一度だけ所有を返し、I/O、Stage.ReleaseAsync、GPU待機を実行しない。
実体解放はmanagerのTrimAsyncとDisposeAsyncで進める。

外部Reference、Lease、Snapshotが残っている間はManager.DisposeAsyncを拒否する。
終了前に三種類の所有者をすべて返し、進行中の構築と操作を完了する。
Snapshotを長く保持すると参照した値だけでなくmap全体の世代が残るため、通常は一回のフレームや処理の範囲に限定する。

## APIと契約

比較元は型付きPipelineを持つ `8a1dac3`。AcquireAsyncの戻り値を変更し、論理所有と内容の取得を分離する。

```diff
 namespace Lumyte.Resources
 {
     public sealed class ResourceManager
     {
-        public ValueTask<ResourceLease<T>> AcquireAsync<T>(ResourceKey<T> key, CancellationToken cancellationToken = default) where T : class;
+        // 初回ロードを待ち、論理所有を返す。取消はこの待機だけに適用する。
+        public ValueTask<ResourceReference<T>> AcquireAsync<T>(ResourceKey<T> key, CancellationToken cancellationToken = default) where T : class;
+        public ulong ManagerId { get; }
+        public ulong PublicationVersion { get; }
+        // ロードせず論理所有を増やす。無効なhandleはfalse。
+        public bool TryAcquire<T>(ResourceHandle<T> handle, out ResourceReference<T>? reference) where T : class;
+        // 現在の公開map内の全世代を保持する。返却は呼出側の責任。
+        public ResourceSnapshot CaptureSnapshot();
     }
+    public readonly struct ResourceHandle<T> : IEquatable<ResourceHandle<T>> where T : class
+    {
+        public ulong ManagerId { get; }
+        public uint Slot { get; }
+        public uint Generation { get; }
+        // 未初期化かだけを調べる。生存確認ではない。
+        public bool IsDefault { get; }
+        public bool Equals(ResourceHandle<T> other);
+    }
+    public sealed class ResourceReference<T> : IDisposable where T : class
+    {
+        public ResourceKey<T> Key { get; }
+        public ResourceHandle<T> Handle { get; }
+        public bool IsAvailable { get; }
+        // scope未指定は現在の公開値、指定時は捕捉した公開値を選ぶ。
+        public ResourceLease<T> Acquire(ResourceSnapshot? snapshot = null);
+        public bool TryAcquire(out ResourceLease<T>? lease, ResourceSnapshot? snapshot = null);
+        // 論理所有を一度返す。既存Leaseの内容は保持される。
+        public void Dispose();
+    }
+    public sealed class ResourceSnapshot : IDisposable
+    {
+        public ulong PublicationVersion { get; }
+        public bool TryAcquire<T>(ResourceHandle<T> handle, out ResourceLease<T>? lease) where T : class;
+        // mapの保持を返す。発行済みLeaseは独立して有効。
+        public void Dispose();
+    }
     public sealed class ResourceLease<T> : IDisposable where T : class
     {
         public ResourceKey<T> Key { get; }
         public ulong Revision { get; }
         public bool IsUsable { get; }
         public T Value { get; }
         public void Dispose();
     }
 }
```

複数の出力を一つの公開状態から取得したい場合にSnapshotを共有する。単一の値だけならReference.Acquire()でよい。

```csharp
using ResourceReference<Mesh> mesh = await manager.AcquireAsync(meshKey);
using ResourceReference<Material> material = await manager.AcquireAsync(materialKey);
using ResourceSnapshot snapshot = manager.CaptureSnapshot();
using ResourceLease<Mesh> meshLease = mesh.Acquire(snapshot);
using ResourceLease<Material> materialLease = material.Acquire(snapshot);
// 両方ともsnapshotが保持する同じ公開状態から取得する。
Draw(meshLease.Value, materialLease.Value);
```

AcquireAsyncがLeaseを直接返す既存利用箇所は、Referenceを保持したうえでAcquireを呼ぶ形へ変更する。
毎フレームの描画でロードを呼び直す必要はなく、保持したReferenceから同期的に内容を取得できる。

## 性能と容量

handleのコピーは16 byteの値コピーで、ヒープ確保や所有数の更新を行わない。
一方Reference、Lease、Snapshotはmanaged objectであり、取得は無割り当てではない。
slot解決は同期区間内で識別子・世代・型を検証するため、lock-freeとは規定しない。

CaptureSnapshotとSnapshot.Disposeは捕捉したmapの全世代を保持・返却するので、公開リソース数に比例した処理がある。
slotの新規割当では再利用候補の走査を行う。性能測定ではhandleのコピー、単一Lease取得、scope開始・終了、slotの再取得を分ける。
公開mapを保持する方式は一貫した複数読取りを簡単にする一方、長いscopeの保持によって回収が遅れる。

## 検討した代替案

- 生ポインターやslot番号だけではmanager違いとslot再利用を検出できないため、manager識別子とslot世代を含める。
- handleのコピーで所有を増やす方式はunmanaged値のコピーに返却責任を持ち込むため、Referenceに所有を分離する。
- Leaseだけを長期間保持する方式では論理的な常駐と内容の使用期間が一致してしまうため、二種類の所有者を設ける。
- 毎回現在の公開mapを読む方式では複数の取得が異なる公開状態を観測するため、必要な処理でSnapshotを使う。

## 結果と影響

ECSには小さな非所有値を保持し、Referenceで論理常駐、Leaseで具体的な内容の寿命を管理できる。
slot世代と内容Revisionを混同せず、利用側が所有期間を明示する必要がある。
Snapshotは入力の内容を複製せず、保持中の値を回収しないことによって一貫した公開状態を提供する。

## 検証方針

- handleが16 byteかつmanaged参照を含まないことと、コピーによって寿命が延びないことを確認する。
- default・別manager・異なる型・返却済みslot・slot再利用前のhandleを拒否する。
- handleから取得した複数Referenceの独立所有と、最後のReference返却後も取得済みLeaseが値を保持することを確認する。
- Snapshot開始後に公開したキーの取得を拒否し、scopeから発行済みのLeaseがscope返却後も使えることを確認する。
- Reference・Lease・Snapshotが残る場合の終了拒否と、返却後の正常な終了を検証する。

## 関連ADR

- [ADR-RESOURCES-0001](RESOURCES-0001-typed-pipelines.md)
