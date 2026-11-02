# ADR-RESOURCES-0009: RetainとPinによる所有権操作の区別

- 状態: 採用
- 日付: 2026-10-26
- 基準コミット: `16d856a0e9cda6a6ecfa559aaf55d87b4085b139`
- 変更関係: 一部変更
- 変更元: [ADR-RESOURCES-0002](RESOURCES-0002-generations-reload.md)「取得と公開状態のスナップショット」「APIと契約」（基準時点では提案）
- 変更対象: ResourceManager、ResourceReference、ResourceSnapshotの取得名とTry取得のnullability注釈、Snapshotの例外を返す取得入口
- 継続する判断: 基準コードの所有権、失効、スレッド、キャンセル、公開世代の選択

## 背景

リソースを使う側は、更新後の内容を取得し続けるResourceReferenceと、一回の描画などで特定世代を保持するResourceLeaseを使い分ける。基準コードでは両方をAcquireと呼ぶため、呼び出し箇所だけでは所有の違いを判別しにくい。

変更前は、基準コミットの `src/Core/Lumyte.Resources/ResourceManager.cs`、`ResourceReference.cs`、`ResourceSnapshot.cs` の取得メソッドで確認できる。

## 決定

更新に追従する所有をRetain、内容世代の固定をPinと呼び分ける。利用側はAcquire系の呼び出しを新名へ書き換え、返される所有物の扱いは維持する。

### 利用者の変更前・変更後

同じリソースを取得して一回描画する例を示す。Managerの構築とPipeline登録は省略し、`Mesh`と`Draw`はアプリが定義する。両例ともスコープの終了時にLease、Referenceの順で返す。

変更前（基準コード）:

```csharp
using ResourceReference<Mesh> mesh = await manager.AcquireAsync(meshKey, cancellationToken);
using ResourceLease<Mesh> frame = mesh.Acquire();
Draw(frame.Value);
```

変更後（本ADRの決定）:

```csharp
using ResourceReference<Mesh> mesh = await manager.RetainAsync(meshKey, cancellationToken);
using ResourceLease<Mesh> frame = mesh.Pin();
Draw(frame.Value);
```

描画結果と保持期間は変わらない。Referenceを保持している間も公開内容は更新され、取得済みのLeaseは元の世代を保持する。

### 契約とAPI差分

RetainAsyncは必要なら初回ロードを待ち、TryRetainは有効なhandleからI/Oなしで所有を追加する。PinとTryPinは現在、または指定したSnapshotの世代を保持するLeaseを返す。PinはGCアドレスを固定せず、値の複製や実行環境の失効防止も行わない。

この3型の旧AcquireAsync、Acquire、TryAcquireは削除する。Snapshotにも、取得できなければ例外を返すPinと、falseを返すTryPinをそろえる。Try取得の成功時に出力が非nullになることを `System.Diagnostics.CodeAnalysis.NotNullWhen` で示す。

返却済みReference・Snapshotへの取得はObjectDisposedExceptionとする。各所有物を返す責任は呼び出し側にあり、取得済みLeaseは元のReference・Snapshotを返した後も保持される。同じ所有物の利用とDisposeを並行実行しない。改名によって所有数、キャンセル、公開世代の選択、例外を変えない。

基準コミットの上記3ファイルに対する差分を示す。

```diff
 namespace Lumyte.Resources
 {
     public sealed partial class ResourceManager
     {
-        public ValueTask<ResourceReference<T>> AcquireAsync<T>(ResourceKey<T> key, CancellationToken cancellationToken = default) where T : notnull;
+        // 初回ロードを待ち、更新に追従する所有を返す。取消はこの待機に適用する。
+        public ValueTask<ResourceReference<T>> RetainAsync<T>(ResourceKey<T> key, CancellationToken cancellationToken = default) where T : notnull;
-        public bool TryAcquire<T>(ResourceHandle<T> handle, out ResourceReference<T>? reference) where T : notnull;
+        // I/Oを行わず論理所有を追加する。無効・失効したhandleはfalseとnull。
+        public bool TryRetain<T>(ResourceHandle<T> handle, [NotNullWhen(true)] out ResourceReference<T>? reference) where T : notnull;
     }
     public sealed class ResourceReference<T> where T : notnull
     {
-        public ResourceLease<T> Acquire(ResourceSnapshot? snapshot = null);
+        // 現在値、またはsnapshot内の値とRevisionを独立して保持する。取得不能はInvalidOperationException。
+        public ResourceLease<T> Pin(ResourceSnapshot? snapshot = null);
-        public bool TryAcquire(out ResourceLease<T>? lease, ResourceSnapshot? snapshot = null);
+        // I/OやStage実行を行わない。取得不能はfalseとnull。
+        public bool TryPin([NotNullWhen(true)] out ResourceLease<T>? lease, ResourceSnapshot? snapshot = null);
     }
     public sealed class ResourceSnapshot
     {
+        // 捕捉した公開状態から取得する。無効なhandleや取得不能な値はInvalidOperationException。
+        public ResourceLease<T> Pin<T>(ResourceHandle<T> handle) where T : notnull;
-        public bool TryAcquire<T>(ResourceHandle<T> handle, out ResourceLease<T>? lease) where T : notnull;
+        // 捕捉した公開状態から取得する。無効なhandleや取得不能な値はfalseとnull。
+        public bool TryPin<T>(ResourceHandle<T> handle, [NotNullWhen(true)] out ResourceLease<T>? lease) where T : notnull;
     }
 }
```

## 検討した代替案

- AcquireReferenceとAcquireLease: 戻り値の型は分かるが、保持と世代固定の操作差を動詞で示せない。
- Load: 初回ロードは表せるが、I/Oなしで所有を追加する操作を表せない。
- 旧名を互換入口として残す: 書き換えを避けられるが、同じ操作の公開名が増える。
- 説明だけを増やす: APIは変わらないが、呼び出し箇所から所有の違いを識別できない。

## 結果と影響

呼び出し箇所で所有の違いを区別でき、Try取得成功後のnull抑制が不要になる。ソース互換性は維持せず、既存利用者は新名へ一括移行する。

検証では新名による取得、更新前後の世代、失効したhandle、返却済み所有者の扱いが既存契約に一致することを確認する。Try取得の成功分岐でnull抑制なしに戻り値を利用でき、Snapshot.Pinが取得不能時に例外を返すことも確認する。
