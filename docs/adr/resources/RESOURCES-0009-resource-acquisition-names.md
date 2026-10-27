# ADR-RESOURCES-0009: RetainとPinによる所有権操作の区別

- 状態: 採用
- 日付: 2026-10-26

## 背景

ResourceManagerのAcquireAsyncは論理リソースのReferenceを返し、ResourceReferenceのAcquireは値とRevisionを固定するLeaseを返す。
同じ取得名が異なる所有権を表すため、呼び出し箇所だけではHotReloadを追従する所有と特定世代の保持を判別しにくい。

## 決定

[ADR-RESOURCES-0002](RESOURCES-0002-generations-reload.md)の所有権・失効・スレッド契約を維持し、論理所有を保持するRetainと、内容世代を固定するPinで動詞を分ける。
RetainAsyncは必要なら初回ロードと構築を待ち、HotReloadを追従するReferenceを返す。TryRetainは生存するhandleからI/Oなしで論理所有を追加する。
PinとTryPinは現在、またはSnapshotが捕捉した内容世代のLeaseを返す。GCアドレスの固定や値の複製は行わず、runtimeの失効も防がない。
旧AcquireAsync、Acquire、TryAcquireは削除し、利用側もRetainAsync、TryRetain、Pin、TryPinへ移行する。
Snapshotにも、取得できなければ例外を返すAcquireLeaseと、falseを返すTryAcquireLeaseをそろえる。
成功時のTry取得の出力は非nullであることを、新しい公開メソッドのNotNullWhen属性で表す。

### 公開APIと契約

比較元はmainの`16d856a`。旧名を削除し、Try取得には成功時のnullability注釈を付ける。

```diff
 namespace Lumyte.Resources
 {
     public sealed partial class ResourceManager
     {
+        // 初回ロードを待ち、HotReloadを追従する論理所有を返す。取消はこの待機に適用する。
-        public ValueTask<ResourceReference<T>> AcquireAsync<T>(ResourceKey<T> key, CancellationToken cancellationToken = default) where T : notnull;
+        public ValueTask<ResourceReference<T>> RetainAsync<T>(ResourceKey<T> key, CancellationToken cancellationToken = default) where T : notnull;
+        // I/Oを行わず論理所有を追加する。無効・失効したhandleはfalseとnull。
+        public bool TryRetain<T>(ResourceHandle<T> handle, [NotNullWhen(true)] out ResourceReference<T>? reference) where T : notnull;
-        public bool TryAcquire<T>(ResourceHandle<T> handle, out ResourceReference<T>? reference) where T : notnull;
+        public bool TryAcquire<T>(ResourceHandle<T> handle, [NotNullWhen(true)] out ResourceReference<T>? reference) where T : notnull;
     }
     public sealed class ResourceReference<T> where T : notnull
     {
+        // 現在値、またはsnapshot内の値とRevisionを独立して保持する。取得不能はInvalidOperationException。
-        public ResourceLease<T> Acquire(ResourceSnapshot? snapshot = null);
+        public ResourceLease<T> Pin(ResourceSnapshot? snapshot = null);
+        // I/OやStage実行を行わない。取得不能はfalseとnull。
+        public bool TryPin([NotNullWhen(true)] out ResourceLease<T>? lease, ResourceSnapshot? snapshot = null);
-        public bool TryAcquire(out ResourceLease<T>? lease, ResourceSnapshot? snapshot = null);
+        public bool TryAcquire([NotNullWhen(true)] out ResourceLease<T>? lease, ResourceSnapshot? snapshot = null);
     }
     public sealed class ResourceSnapshot
     {
+        // 捕捉した公開状態から取得する。無効なhandleや取得不能な値はInvalidOperationException。
+        public ResourceLease<T> Pin<T>(ResourceHandle<T> handle) where T : notnull;
+        // 捕捉した公開状態から取得する。無効なhandleや取得不能な値はfalseとnull。
+        public bool TryPin<T>(ResourceHandle<T> handle, [NotNullWhen(true)] out ResourceLease<T>? lease) where T : notnull;
-        public bool TryAcquire<T>(ResourceHandle<T> handle, out ResourceLease<T>? lease) where T : notnull;
+        public bool TryAcquire<T>(ResourceHandle<T> handle, [NotNullWhen(true)] out ResourceLease<T>? lease) where T : notnull;
     }
 }
```

NotNullWhenはSystem.Diagnostics.CodeAnalysisの属性とする。
返却済みReference・Snapshotへの取得はObjectDisposedExceptionとなる。各所有物のDispose責任は呼び出し側にあり、取得済みLeaseは元のReference・Snapshotを返した後も保持される。
改名後も所有数、キャンセル、公開世代の選択、例外を変えない。

## 検討した代替案

AcquireReferenceとAcquireLeaseのように型名だけで区別する方法では、保持と世代固定の操作差が動詞に表れないため採用しない。
Loadだけでは、I/Oを行わず生存handleの所有を増やす操作を同じ語彙で説明できないため、論理所有にはRetainを使用する。
互換入口を残す案は同じ操作の公開名が増えるため採用しない。ソース互換性は要求せず、利用側を一括移行する。
ドキュメントの説明だけを増やす方法では、呼び出し箇所から所有権を識別できないため、名前を追加する。

## 結果と影響

論理所有と内容世代の取得を呼び出し箇所で区別でき、Try取得成功後のnull抑制が不要になる。
同義の互換メソッドを削除するため、既存利用者は新名への移行が必要となる。
検証では新名による取得、HotReload前後の世代、失効したhandle、返却済み所有者の扱いを確認する。
