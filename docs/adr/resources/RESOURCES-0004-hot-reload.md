# ADR-RESOURCES-0004: 入力変更によるHotReload

- 状態: 提案
- 日付: 2026-10-23

## 背景

編集された入力からリソースを更新する際、複合リソースの一部分だけを先に公開すると、異なる入力Revisionの値が混在する。ファイル通知は重複・欠落し、読み込み中にも追加の変更が起こるため、通知をそのまま更新の確定情報にはできない。

入力変更を有界に集約し、初回ロードと同じStageで候補を構築した上で一括公開する。既存の利用者が保持する内容は維持し、失敗した更新の候補と監視処理の寿命を管理する。

## 決定

### Source、通知、再構築の責務

`IResourceSource.Changes` から、そのSourceと同じアドレス体系・取得元の変更通知を借りる。Sourceは通知providerを所有し、各列挙が独立した購読を所有する。`ResourceManager` は自身の購読と `ResourceReloadService` を所有する。

ResourceReloadServiceは通知の重複排除、待機時間、保留数、再試行だけを調整する。ResourceManagerが入力Revisionを比較し、変更された入力の出力と依存する親を再構築する。ファイル監視APIやbackend固有の生成・制御を共通サービスに持ち込まない。

`ResourceSourceChange.Revision` はhintである。値を信頼してキャッシュを書き換えず、Sourceの `GetRevisionAsync` で現在のRevisionを確認する。未追跡の入力は問い合わせ自体を省き、未ロードのリソースを探索・自動ロードしない。

### 監視対象と通知の集約

Managerはロード中・公開中の入力集合を `IResourceWatchTargets.Resources` の不変snapshotとして提供する。構築後にBuild依存を回収しても、親の再構築に必要な入力来歴を保持する。最後の親も回収した時点で対象から除く。初回公開時には再走査を要求し、構築中に届いた変更の取りこぼしを補う。

通知は入力IDで重複排除する。最後の通知からDebounceDelayが経過するか、最初の通知からMaximumBatchDelayが経過すると一つのbatchを開始する。後者により連続編集でも集約待ちが無期限に延びない。ただし、既に実行中の構築や再試行がある場合に実行開始の上限時間を保証するものではない。

異なる保留IDがMaximumPendingChangesを超える場合やSourceがRescanを通知した場合は、ID集合を捨て、追跡中入力全体の再走査へ切り替える。無制限の通知履歴を保持しない。構築中・再試行待機中も追加通知を受け、次のbatchへ残す。

通知の受信とbatch消費は非同期処理として進め、待機中に専用スレッドを占有しない。実装はThreadPoolへ開始を委譲し、空のキューをpollし続けるループを持たない。Stageの継続が実行されるスレッドは固定せず、CPU処理と所有スレッド処理はStageが既存の実行先へ配送する。

### 候補の構築と公開

1. 現在の公開map、入力来歴、依存関係を構築viewとして捕捉する。
2. 通知対象のRevisionを確認し、変更された出力から親へ影響範囲を広げる。無変更ならUnchangedとする。
3. 影響範囲だけを同じStageで再構築し、必要な依存の候補を同じviewで共有する。
4. 入力Revision、実際に使ったRuntimeのversion・可用性・失効状態、公開versionを再検証する。
5. 検証が成立した候補を一括公開する。競合時はConflictを返し、候補を解放する。

構築中に観測した共有入力のRevisionが、再構築されていない公開出力と食い違う場合も公開を拒否する。構築失敗やキャンセル時は旧公開mapを維持し、開始済み候補の終了とcleanupを待つ。公開後のcleanup失敗は公開済みの内容を巻き戻さず、未完了の解放責任を保持する。

既存のResourceLeaseとResourceSnapshotは取得時の旧内容を保持する。ResourceReferenceとHandleは新しい公開から次のLeaseを取得できる。通常の入力変更で旧値を失効させず、物理Runtimeのlossによる使用不能は別の契約として適用する。

### 再試行、観測、終了

Conflictまたは構築例外のとき、初回に加えてMaximumRetryCount回までRetryDelayを挟んで再試行する。再試行では最新のRevisionを調べ直す。上限到達でそのbatchを終えても、次の通知は処理できる。

`Reloaded` はPublished・Unchanged・Conflictの各結果を通知する。`ReloadFailed` は構築例外と試行回数・再試行予定を通知する。通知は同期ロックの外側でサービスの非同期処理から呼ぶ。handlerは終了を同期的に待ってはならない。handlerの例外とSource列挙の失敗はCompletionを失敗させ、自動監視を停止する。構築例外の有限再試行と、購読自体の障害は区別する。

Manager生成時にChangesがありEnabled=trueなら一度開始する。Changesがnull、またはEnabled=falseならサービスを作らず、監視タスクも起動しない。手動構築するResourceReloadServiceはManagerとproviderを借用し、両者の解放前に呼び出し側が停止する。

StopAsyncは通知の列挙と実行中の再構築をキャンセルして両方の終了を待ち、終端例外を伝える。DisposeAsyncも終了を待つが、その終端例外でDIの後始末を止めず、CompletionとLastErrorへ残す。停止済みserviceは再開しない。Manager終了時は自動処理を停止・joinしてから出力を解放し、Source・Stageなど借用先の寿命を越えない。

## 公開APIと契約

比較元は `split/resources-runtime` の `2e2acb5`。既存の構築・公開APIを使い、通知と自動運転の境界を追加する。

```diff
 namespace Lumyte.Resources
 {
     public interface IResourceSource
     {
+        // nullは通知なし。既存Sourceは変更せずに使える。
+        IResourceChangeSource? Changes => null;
     }
+    public interface IResourceChangeSource
+    {
+        // 各列挙が独立した購読。取消時は入力側の待機も解除する。
+        IAsyncEnumerable<ResourceSourceChange> WatchAsync(CancellationToken cancellationToken = default);
+        // 既定実装は上のoverloadへ転送。adapterは対象集合で通知を絞り込める。
+        IAsyncEnumerable<ResourceSourceChange> WatchAsync(
+            IResourceWatchTargets targets, CancellationToken cancellationToken = default);
+    }
+    public interface IResourceWatchTargets
+    {
+        IReadOnlySet<ResourceId> Resources { get; }
+    }
+    public readonly record struct ResourceSourceChange(ResourceId Id, string? Revision)
+    {
+        // RescanではIdとRevisionを使わない。
+        public static ResourceSourceChange Rescan { get; }
+        public bool RequiresRescan { get; init; }
+    }
+    public sealed record ResourceReloadOptions
+    {
+        public bool Enabled { get; init; } = true;
+        public TimeSpan DebounceDelay { get; init; } = TimeSpan.FromMilliseconds(100);
+        public TimeSpan MaximumBatchDelay { get; init; } = TimeSpan.FromSeconds(1);
+        public int MaximumPendingChanges { get; init; } = 1024;
+        public int MaximumRetryCount { get; init; } = 3;
+        public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(100);
+        // delayは0～1日。MaximumBatchDelayは正かつDebounceDelay以上。
+        // 保留数は正、再試行数は非負。違反は構築時に引数例外。
+    }
     public sealed partial class ResourceManager
     {
-        public ResourceManager(string name, IResourceSource source,
-            IEnumerable<IResourceStage> stages, IMemoryManager memory,
-            IEnumerable<IResourceRuntimeDependency>? runtimeDependencies = null);
+        // ChangesがありEnabledなら自動開始。nullは既定optionsを使用する。
+        public ResourceManager(string name, IResourceSource source,
+            IEnumerable<IResourceStage> stages, IMemoryManager memory,
+            IEnumerable<IResourceRuntimeDependency>? runtimeDependencies = null,
+            ResourceReloadOptions? reloadOptions = null);
+        public ResourceReloadService? HotReload { get; }
     }
+    public sealed class ResourceReloadService : IAsyncDisposable
+    {
+        public ResourceReloadService(ResourceManager manager, IResourceChangeSource source,
+            ResourceReloadOptions? options = null);
+        public event EventHandler<ResourceReloadEventArgs>? Reloaded;
+        public event EventHandler<ResourceReloadFailureEventArgs>? ReloadFailed;
+        public Task Completion { get; }
+        public ResourceReloadResult? LastResult { get; }
+        public Exception? LastError { get; }
+        // 稼働中の再呼び出しは無操作。停止後の再開は拒否する。
+        public void Start();
+        public Task StopAsync();
+        public ValueTask DisposeAsync();
+    }
+    public sealed class ResourceReloadEventArgs : EventArgs
+    {
+        public ResourceReloadEventArgs(ResourceReloadResult result);
+        public ResourceReloadResult Result { get; }
+    }
+    public sealed class ResourceReloadFailureEventArgs : EventArgs
+    {
+        public ResourceReloadFailureEventArgs(Exception exception, int attempt, bool willRetry);
+        public Exception Error { get; }
+        // 1始まりの試行回数。
+        public int Attempt { get; }
+        public bool WillRetry { get; }
+    }
 }
 namespace Lumyte.Resources.DependencyInjection
 {
     public sealed class ResourceRegistrationBuilder
     {
+        // 同じ登録への重複指定や構成確定後の変更を拒否する。
+        public ResourceRegistrationBuilder ConfigureHotReload(ResourceReloadOptions options);
     }
 }
```

`ReloadAsync` と `ReloadAllAsync` は内部の検証用入口とし、公開APIに含めない。アプリはSourceの通知と結果観測を使い、物理Runtimeの復旧要求には `RebuildRuntimeAsync` を使う。

## 検討した代替案

- 通知callback内で即座に構築する: 監視元を長時間占有し、連続編集で同じ処理を繰り返す。通知受信と有限の再構築batchを分ける。
- 最後の通知から一定時間だけ待つ: 連続通知で永久に更新が始まらない。最大集約時間も持つ。
- 変更ごとに全ロード済み出力を作り直す: 独立したアセットまでCPU処理やNative生成をやり直す。入力来歴と依存辺で範囲を限定する。
- 出来上がった候補から順次公開する: 親と子のRevisionが混在する。構築view全体を検証して公開する。
- 失敗時に無期限で再試行する: 壊れた入力が継続的にI/Oとデコードを消費する。有限再試行後は新しい通知を待つ。

## 結果と影響

ロードと更新でStageを共用でき、更新失敗中も旧内容を使用できる。旧Leaseを長く保持するほど新旧の出力とメモリが併存する。原子的公開のため、候補を完成させる一時的な余裕が必要になる。

保留通知のID数は有界だが、再走査の負荷は追跡中入力数に比例する。変更された子から親への影響計算と候補構築は、依存グラフの大きさに応じて増える。最大集約時間は処理時間やリアルタイムの締め切りを保証せず、SourceのRevision検出能力を超える変更も検出できない。

## 検証方針

偽の通知Sourceと制御可能なStageで、重複通知、連続通知の最大待機、保留数超過の再走査、構築中の追加通知、初回公開との競合を検証する。Build入力回収後の来歴保持、未追跡入力の無視、親解放後の監視対象除去を確認する。

旧Lease・Snapshotの保持、複数出力の一括公開、入力・Runtime・公開versionの競合、失敗と再試行上限、新通知による回復を確認する。Sourceやhandlerの例外、取消callbackの例外があっても両方の非同期処理をjoinし、SourceやStageの破棄より先に停止することを検証する。

## 関連ADR

- [Runtimeと実行先](RESOURCES-0003-runtime-execution.md)
- [ハンドルと内容世代](RESOURCES-0002-generations-reload.md)
