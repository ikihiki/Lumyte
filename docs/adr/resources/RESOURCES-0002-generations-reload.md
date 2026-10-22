# ADR-RESOURCES-0002: 論理ハンドルとグラフの世代切替

- 状態: 提案
- 日付: 2026-10-20

## 背景

読み込み済みデータを差し替えてもECSのunmanaged値から参照を継続したい。
複数の依存の新旧が混在する公開や、失敗した再構築で既存データが失われることを避ける。

## 決定

論理所有者ResourceReference、非所有の16 byte ResourceHandle、内容世代を保持するResourceLeaseを分ける。
handleはmanager識別子・slot・slot世代を持ち、manager違いとslot再利用を検出する。
ManagerのAcquireAsyncとhandleからのTryAcquireは常にResourceReferenceを返す。
Referenceは論理リソースを保持し、描画開始など値を固定したい時点でReference.AcquireからLeaseを取得する。
Leaseは具体的な値とRevisionを固定し、Referenceを返した後も独立して有効である。
HotReloadは新世代を別に構築・公開し、既存Leaseの参照先は切り替えない。
公開済みの出力をStageや利用側が直接書き換えないことを前提とし、Lease保持はwriterを待機させるlockではない。
read scopeは公開mapを固定し、複数ReferenceのAcquire(scope)で同じ公開世代を使用できる。
ReferenceのAcquire/TryAcquireはI/Oや構築を行わず、未公開・失効した値は取得できない。

変更したSourceのRevisionを確認し、逆依存を含めた候補グラフを構築してから一括公開する。
再構築範囲は、Revisionが変化した入力Sourceを直接利用するリソースと、その親への推移的な依存範囲に限定する。
影響を受けない別の枝や、影響範囲外の公開済み依存先は再利用し、強制再構築しない。
重複通知はまとめ、Revisionが同じ通知と未追跡の入力は再構築を起こさない。
回収済みの依存先が再構築に必要になった場合は再取得する。
粒度はResourceIdで識別する入力ファイルと記録されたリソース依存であり、
同一ファイル内の内容比較や、再構築後の出力の同値判定による伝播停止は行わない。
失敗は例外、構築中の入力・公開の競合はConflictで旧公開mapを保持する。
旧leaseとread scopeは旧内容を保持する。Build依存の値を回収した後も入力の来歴を保持する。

StreamとRevisionを提供するIResourceSourceが、同じ取得元のIResourceChangeSourceをChangesとして提供する。
変更通知を持たないSourceの既定値はnullとする。ResourceManagerはSource.Changesを取得し、監視workerを自動開始する。
取得元と独立した監視元をManagerへ渡すAPIは設けず、IDの意味とbacking storeの対応をSourceが保証する。
複数の取得元を振り分けるSourceは、同じ規則で監視対象も振り分ける。
監視側にはIResourceWatchTargetsを渡す。Resourcesはロード中・公開中のグラフが使う入力IDの読み取り専用集合で、
各取得結果は不変、次回取得時に最新の集合となる。Build依存の実体を回収しても親の入力来歴は監視対象に残る。
入力を開く前に対象へ加え、新しい入力の公開後にもRevisionを再確認し、監視開始との隙間を補う。
workerは通知の重複排除、debounce、最大集約時間、保留数上限、有限再試行を担当する。
Rescanは追跡中の入力のみを再確認する。通知はhintであり、公開前にSourceのRevisionを検証する。
Managerは終了時に監視と進行中の自動再構築をキャンセルしてjoinした後、リソースを解放する。
Source・Stageは借用し、呼び出し側またはDIが所有する。通知元はSourceが所有し、Managerは購読の寿命だけを管理する。
HotReloadプロパティで結果・失敗・workerのCompletionを観測できる。通常利用側のStart/Stopは不要である。
ReloadAsync/ReloadAllAsyncは内部のデバッグ用コマンドとし、公開APIに含めない。
DIはUseSourceで生成した同じSourceから監視を取得する。別の監視元の登録は不要である。
ConfigureHotReloadで集約・再試行を設定でき、Enabled=falseで購読を無効にできる。
実ファイルの監視adapterとruntime依存・失効・復旧は後続PRで扱う。

## APIと契約

比較元はこのPRの依存ブランチ。以下は判断対象の追加・変更APIの抜粋。

```diff
+namespace Lumyte.Resources
+{
+    public interface IResourceSource
+    {
+        IResourceChangeSource? Changes => null;
+    }
+    public interface IResourceWatchTargets { IReadOnlySet<ResourceId> Resources { get; } }
+    public interface IResourceChangeSource
+    {
+        IAsyncEnumerable<ResourceSourceChange> WatchAsync(CancellationToken cancellationToken = default);
+        IAsyncEnumerable<ResourceSourceChange> WatchAsync(IResourceWatchTargets targets, CancellationToken cancellationToken = default);
+    }
+    public sealed class ResourceReloadService : IAsyncDisposable
+    {
+        public Task Completion { get; }
+        public Exception? LastError { get; }
+        public ResourceReloadResult? LastResult { get; }
+        public event EventHandler<ResourceReloadEventArgs>? Reloaded;
+        public event EventHandler<ResourceReloadFailureEventArgs>? ReloadFailed;
+    }
+    public readonly struct ResourceHandle<T> where T : class { public bool IsDefault { get; } }
+    public sealed class ResourceReference<T> : IDisposable where T : class
+    {
+        public ResourceKey<T> Key { get; }
+        public ResourceHandle<T> Handle { get; }
+        public bool IsAvailable { get; }
+        public ResourceLease<T> Acquire(ResourceReadScope? scope = null);
+        public bool TryAcquire(out ResourceLease<T>? lease, ResourceReadScope? scope = null);
+        public void Dispose();
+    }
+    public sealed class ResourceReadScope : IDisposable
+    {
+        public ulong PublicationVersion { get; }
+        public bool TryAcquire<T>(ResourceHandle<T> handle, out ResourceLease<T>? lease) where T : class;
+        public void Dispose();
+    }
+    public sealed class ResourceManager
+    {
+        public ulong ManagerId { get; }
+        public ulong PublicationVersion { get; }
-        public ValueTask<ResourceLease<T>> AcquireAsync<T>(ResourceKey<T> key, CancellationToken cancellationToken = default) where T : class;
+        public ValueTask<ResourceReference<T>> AcquireAsync<T>(ResourceKey<T> key, CancellationToken cancellationToken = default) where T : class;
+        public bool TryAcquire<T>(ResourceHandle<T> handle, out ResourceReference<T>? reference) where T : class;
+        public ResourceReadScope BeginRead();
-        public ResourceManager(string name, IResourceSource source, IEnumerable<IResourceStage> stages, IMemoryManager memory);
+        public ResourceManager(string name, IResourceSource source, IEnumerable<IResourceStage> stages, IMemoryManager memory, ResourceReloadOptions? reloadOptions = null);
+        public ResourceReloadService? HotReload { get; }
+    }
+}
```

## 検討した代替案

生ポインターはECSでは小さいが、差し替え・失効・slot再利用を扱えない。
参照先をその場で書き換える方式は旧leaseと複数要素の整合した読み取りを保証できない。

## 結果と影響

利用側は論理所有と内容利用を選べる一方、旧世代を保持する間はメモリ使用量が増える。
非所有handleだけでは常駐を保証しない。

## 検証方針

旧lease/read scopeの保持、失敗・競合時のrollback、回収済みBuild入力の来歴、
slot再利用、別managerのhandleを検証する。
Sourceに付随する通知の自動取得・開始、監視のないSourceと無効化設定、対象集合の更新・回収、停止順、
通知過多、再構築中の追加通知、
有限再試行、未追跡の入力を読み込まないことを検証する。
Referenceからのフレーム用Lease取得、Reference返却後のLease保持、同じReferenceで次世代を取得できること、
handleから取得したReferenceの独立所有、捕捉済み公開mapに存在しない値の取得拒否を検証する。
個別画像の変更で画像とその親だけが更新され、別シーンと共有ジオメトリの実体・Revision・構築回数が
変わらないこと、入力の再取得が変更画像の一回だけであること、重複した無変更通知で再構築しないことを検証する。

## 関連ADR

- [ADR-RESOURCES-0001](RESOURCES-0001-typed-pipelines.md)
