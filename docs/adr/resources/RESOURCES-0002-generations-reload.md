# ADR-RESOURCES-0002: 論理ハンドルと内容世代

- 状態: 提案
- 日付: 2026-10-23

## 背景

ECSのunmanaged値からリソースを参照し、描画中の具体的な値は独立して保持したい。
論理リソースの所有と、一回の処理で使う内容世代の所有を分ける必要がある。

## 決定

ResourceReferenceは論理リソースを所有し、16 byteのResourceHandleは非所有で参照する。
handleはmanager識別子・slot・slot世代を持ち、別managerとslot再利用を検出する。
Manager.AcquireAsyncとhandleからのTryAcquireはResourceReferenceを返す。
Reference.Acquire/TryAcquireはI/Oや構築を行わず、現在の値とRevisionを固定したResourceLeaseを返す。
取得済みLeaseはReferenceを返した後も独立して有効である。
ResourceReadScopeは公開mapを保持し、複数Referenceを同じ公開状態から取得できる。
公開済みの値を直接書き換えないことを前提とし、Leaseはwriterを待機させるmutexではない。
最後のReferenceを返すとhandleは失効し、LeaseとReadScopeを返すまでは保持した値を回収しない。

変更通知、自動監視、候補グラフの再構築と差し替えは、後続の独立したHotReload PRで扱う。
この段階ではManagerは監視処理を起動せず、SourceやDIに監視設定を追加しない。

## APIと契約

```csharp
ResourceReference<T> reference = await manager.AcquireAsync(key);
ResourceHandle<T> handle = reference.Handle;
using ResourceReadScope frame = manager.BeginRead();
using ResourceLease<T> lease = reference.Acquire(frame);
reference.Dispose();
// leaseは独立して値とRevisionを保持する。
```

Manager.TryAcquire(handle, out reference)も論理所有権を取得する。
失効したhandle、別manager、異なる型、捕捉済み公開mapにない値は取得できない。
Manager終了前にReference・Lease・ReadScopeをすべて返す。

## 検討した代替案

生ポインターではslot再利用と失効を検出できない。
Leaseだけを長期間保持する方式では、論理参照と内容の利用期間を分離できない。

## 結果と影響

利用側は論理所有と内容利用を分けられる。非所有handleだけでは常駐を保証しない。
LeaseやReadScopeで固定した値を保持している間、そのメモリを回収できない。

## 検証方針

handleのunmanaged性、slot再利用、別managerと型の拒否、Referenceの独立所有、
Reference返却後のLease保持、捕捉した公開mapに存在しない値の拒否、終了時の所有権を検証する。
再読み込みと自動監視の検証はHotReload PRへ分離する。

## 関連ADR

- [ADR-RESOURCES-0001](RESOURCES-0001-typed-pipelines.md)
