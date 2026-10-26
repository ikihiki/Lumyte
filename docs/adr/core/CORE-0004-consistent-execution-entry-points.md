# ADR-CORE-0004: 実行基盤の非同期名と受付結果の統一

- 状態: 採用
- 日付: 2026-10-26
- 置換元: [ADR-CORE-0003](CORE-0003-execution-and-jobs.md) の公開送信名と受付API。その他の実行・所有権契約は継続する。

## 背景

command・workflow・batch・graphの投入では、非同期名、取消引数の位置、受付失敗の表現が異なっていた。利用側が共通の受付処理を書ける入口を追加し、既存コードの名前付き引数、例外処理、生成された補助型を維持する。

## 決定

実完了を返すworkflowと生成senderへAsync名を追加する。詳細な受付は既存のCommandEnqueueStatusを共用するEnqueue／Submit／Scheduleへ揃える。型名は既存の公開enumを維持し、command以外にも同じ意味で使用する。既存TryEnqueue・TrySubmit・TrySchedule・Executeと生成senderは互換APIとして残す。

### 公開API

比較元はmain `16d856a`。次は変更対象の抜粋。

```diff
 namespace Lumyte.Execution
 {
     public sealed class CommandPort<TCommand, TResult> where TCommand : struct
     {
+        // Acceptedの場合だけcompletionを返す。取消引数は末尾・省略可能。
+        public CommandEnqueueStatus Enqueue(in TCommand command, out IValueTaskSource<TResult>? completion, CancellationToken cancellationToken = default);
     }
     public sealed class WorkflowPort<TCommand, TResult> where TCommand : struct
     {
+        public CommandEnqueueStatus Enqueue(in TCommand command, out IValueTaskSource<TResult>? completion, CancellationToken cancellationToken = default);
+        // 実operationを一度だけ観測する。既存Executeと同じ契約。
+        public ValueTask<TResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken = default);
     }
     public sealed class ModuleContext
     {
+        public CommandEnqueueStatus Enqueue<TCommand, TResult>(WorkKey<TCommand, TResult> key, in TCommand command, out IValueTaskSource<TResult>? completion, CancellationToken cancellationToken = default) where TCommand : struct;
     }
     public sealed partial class ExecutionRuntime
     {
+        // 取消・停止・容量不足はそれぞれCanceled・NotRunning・QueueFull。拒否時はrun=null。
+        public CommandEnqueueStatus Submit(CommandBatch batch, out CommandRun? run, CancellationToken cancellationToken = default);
+        public CommandEnqueueStatus Schedule<TContext>(JobGraph<TContext> graph, in TContext context, out JobRun? run, CancellationToken cancellationToken = default) where TContext : struct;
         // 既存bool APIは取消・停止の例外契約を維持し、成功時の非null注釈だけ追加する。
-        public bool TrySubmit(CommandBatch batch, out CommandRun? run, CancellationToken token = default);
+        public bool TrySubmit(CommandBatch batch, [NotNullWhen(true)] out CommandRun? run, CancellationToken token = default);
-        public bool TrySchedule<TContext>(JobGraph<TContext> graph, in TContext context, out JobRun? run, CancellationToken token = default) where TContext : struct;
+        public bool TrySchedule<TContext>(JobGraph<TContext> graph, in TContext context, [NotNullWhen(true)] out JobRun? run, CancellationToken token = default) where TContext : struct;
     }
 }
```

詳細受付APIの有効な入力は、受付停止をNotRunning、受付中の事前取消をCanceled、予算不足をQueueFullとする。Acceptedの後は実行結果を既存のValueTaskまたはrunで観測し、同じ所有権と終了順を守る。不正な所属・空batch・無効なrange長・禁止されたhandler内操作は引き続き例外である。受付は同じ同期区間で状態と予算を検査し、拒否時にbatchを消費しない。既存Try系は取消を先に検査して例外を投げ、停止も例外となる従来の順序を保つ。

生成器はPlayCoreから既存Playに加えてPlayAsyncを生成する。PlayWork／DeclarePlay／AddPlayToなどの名前は変えず、双方のsenderへ受付コードを展開する。NameがAsyncで終わる場合は追加しない。既存の利用者メンバーまたは別の生成メンバーとAsync名が衝突する場合は追加aliasだけを省略する。これにより従来有効だった宣言を無効にしない。

## 検討した代替案

- 既存名・戻り値・token名を変更する案は、ソース互換性と例外処理を壊すため採らない。
- Try系を一律boolに変更すると取消・停止・容量不足を区別できなくなるため、詳細結果を返す新しい入口を追加する。
- 別の結果enumを追加すると同じ四状態の変換が必要になるため、既存enumを共用する。

## 結果と影響

呼び出し側はAsync名と末尾のcancellationTokenを使い、共通の四状態で受付を扱える。互換APIの分だけ公開面は増える。ValueTaskの一回観測、runのDispose、CPUとownerの実行規則は変わらない。回帰テストで全受付状態、既存Try系の例外、容量返却、生成名衝突とコンパイルを確認する。
