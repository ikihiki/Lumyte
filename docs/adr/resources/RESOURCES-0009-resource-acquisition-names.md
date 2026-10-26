# ADR-RESOURCES-0009: 取得する所有権を明示するAPI名

- 状態: 採用
- 日付: 2026-10-26

## 背景

ResourceManagerのAcquireAsyncは論理リソースのReferenceを返し、ResourceReferenceのAcquireは値とRevisionを固定するLeaseを返す。
同じ取得名が異なる所有権を表すため、呼び出し箇所だけではHotReloadを追従する所有と特定世代の保持を判別しにくい。

## 決定

[ADR-RESOURCES-0002](RESOURCES-0002-generations-reload.md)の所有権・失効・スレッド契約を維持し、取得する型を明示する名前を追加する。
既存のAcquireAsync、Acquire、TryAcquireは互換性のため残し、非推奨にはしない。新しい利用例では明示的な名前を使う。
Snapshotにも、取得できなければ例外を返すAcquireLeaseと、falseを返すTryAcquireLeaseをそろえる。
成功時のTry取得の出力は非nullであることを、新旧両方の公開メソッドのNotNullWhen属性で表す。

### 公開APIと契約

比較元はmainの`16d856a`。既存メソッドは同じ契約のまま残し、Try取得には成功時のnullability注釈を追加する。

```diff
 namespace Lumyte.Resources
 {
     public sealed partial class ResourceManager
     {
+        // 初回ロードを待ち、HotReloadを追従する論理所有を返す。取消はこの待機に適用する。
+        public ValueTask<ResourceReference<T>> AcquireReferenceAsync<T>(ResourceKey<T> key, CancellationToken cancellationToken = default) where T : notnull;
+        // I/Oを行わず論理所有を追加する。無効・失効したhandleはfalseとnull。
+        public bool TryAcquireReference<T>(ResourceHandle<T> handle, [NotNullWhen(true)] out ResourceReference<T>? reference) where T : notnull;
-        public bool TryAcquire<T>(ResourceHandle<T> handle, out ResourceReference<T>? reference) where T : notnull;
+        public bool TryAcquire<T>(ResourceHandle<T> handle, [NotNullWhen(true)] out ResourceReference<T>? reference) where T : notnull;
     }
     public sealed class ResourceReference<T> where T : notnull
     {
+        // 現在値、またはsnapshot内の値とRevisionを独立して保持する。取得不能はInvalidOperationException。
+        public ResourceLease<T> AcquireLease(ResourceSnapshot? snapshot = null);
+        // I/OやStage実行を行わない。取得不能はfalseとnull。
+        public bool TryAcquireLease([NotNullWhen(true)] out ResourceLease<T>? lease, ResourceSnapshot? snapshot = null);
-        public bool TryAcquire(out ResourceLease<T>? lease, ResourceSnapshot? snapshot = null);
+        public bool TryAcquire([NotNullWhen(true)] out ResourceLease<T>? lease, ResourceSnapshot? snapshot = null);
     }
     public sealed class ResourceSnapshot
     {
+        // 捕捉した公開状態から取得する。無効なhandleや取得不能な値はInvalidOperationException。
+        public ResourceLease<T> AcquireLease<T>(ResourceHandle<T> handle) where T : notnull;
+        // 捕捉した公開状態から取得する。無効なhandleや取得不能な値はfalseとnull。
+        public bool TryAcquireLease<T>(ResourceHandle<T> handle, [NotNullWhen(true)] out ResourceLease<T>? lease) where T : notnull;
-        public bool TryAcquire<T>(ResourceHandle<T> handle, out ResourceLease<T>? lease) where T : notnull;
+        public bool TryAcquire<T>(ResourceHandle<T> handle, [NotNullWhen(true)] out ResourceLease<T>? lease) where T : notnull;
     }
 }
```

NotNullWhenはSystem.Diagnostics.CodeAnalysisの属性とする。
返却済みReference・Snapshotへの取得はObjectDisposedExceptionとなる。各所有物のDispose責任は呼び出し側にあり、取得済みLeaseは元のReference・Snapshotを返した後も保持される。
新旧APIは同じ実装経路を使い、所有数、キャンセル、公開世代の選択、例外を変えない。

## 検討した代替案

既存名の一括置換は利用側のソース互換性を失うため採用しない。
ドキュメントの説明だけを増やす方法では、呼び出し箇所から所有権を識別できないため、名前を追加する。

## 結果と影響

論理所有と内容世代の取得を呼び出し箇所で区別でき、Try取得成功後のnull抑制が不要になる。
同義の公開メソッドが増えるが、実装経路を共有し、既存利用者の移行を必須にしない。
検証では新旧APIを混在させた取得、HotReload前後の世代、失効したhandle、返却済み所有者の扱いを確認する。
