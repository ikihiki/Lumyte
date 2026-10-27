# ADR-CORE-0004: 実行基盤の非同期名と受付結果の統一

- 状態: 採用
- 日付: 2026-10-26
- 置換元: [ADR-CORE-0003](CORE-0003-execution-and-jobs.md) の公開送信名と受付API。その他の実行・所有権契約は継続する。

## 背景

command・workflow・batch・graphで、非同期名、取消引数の位置、受付失敗の表現が異なっていた。同じ操作を表す互換入口を残さず、利用側を統一したAPIへ移行する。

## 決定

実完了を返す送信メソッドはAsync名だけを公開する。詳細な受付はCommandEnqueueStatusを返すEnqueue／Submit／Scheduleへ統一し、旧TryEnqueue・TrySubmit・TryScheduleとWorkflowPort.Executeを削除する。

比較元はmainの`16d856a`。変更対象の公開APIは以下のとおり。

```diff
 namespace Lumyte.Execution
 {
     public sealed class CommandPort<TCommand, TResult> where TCommand : struct
     {
-        public CommandEnqueueStatus TryEnqueue(in TCommand command, CancellationToken token, out IValueTaskSource<TResult>? completion);
+        public CommandEnqueueStatus Enqueue(in TCommand command, out IValueTaskSource<TResult>? completion, CancellationToken cancellationToken = default);
     }
     public sealed class WorkflowPort<TCommand, TResult> where TCommand : struct
     {
-        public CommandEnqueueStatus TryEnqueue(in TCommand command, CancellationToken token, out IValueTaskSource<TResult>? completion);
+        public CommandEnqueueStatus Enqueue(in TCommand command, out IValueTaskSource<TResult>? completion, CancellationToken cancellationToken = default);
-        public ValueTask<TResult> Execute(TCommand command, CancellationToken token = default);
+        public ValueTask<TResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken = default);
     }
     public sealed class ModuleContext
     {
-        public CommandEnqueueStatus TryEnqueue<TCommand, TResult>(WorkKey<TCommand, TResult> key, in TCommand command, CancellationToken token, out IValueTaskSource<TResult>? completion) where TCommand : struct;
+        public CommandEnqueueStatus Enqueue<TCommand, TResult>(WorkKey<TCommand, TResult> key, in TCommand command, out IValueTaskSource<TResult>? completion, CancellationToken cancellationToken = default) where TCommand : struct;
     }
     public sealed partial class ExecutionRuntime
     {
-        public bool TrySubmit(CommandBatch batch, out CommandRun? run, CancellationToken token = default);
+        public CommandEnqueueStatus Submit(CommandBatch batch, out CommandRun? run, CancellationToken cancellationToken = default);
-        public bool TrySchedule<TContext>(JobGraph<TContext> graph, in TContext context, out JobRun? run, CancellationToken token = default) where TContext : struct;
+        public CommandEnqueueStatus Schedule<TContext>(JobGraph<TContext> graph, in TContext context, out JobRun? run, CancellationToken cancellationToken = default) where TContext : struct;
     }
 }
```

受付停止はNotRunning、受付中の事前取消はCanceled、容量不足はQueueFullとなり、拒否時の出力はnullとなる。Accepted後は実行結果をValueTaskまたはrunで観測する。不正な所属・空batch・無効なrange長・禁止されたhandler内操作は例外とする。受付拒否でbatchは消費しない。

生成器はPlayCoreからPlayAsyncだけを生成する。NameがAsyncで終わる場合は重ねて付けない。PlayWork／DeclarePlay／AddPlayToなどの補助名は維持する。Async名が既存メンバーや別の生成メンバーと衝突する場合はLMEXEC001を報告し、そのコンテナーの生成を止める。

## 検討した代替案

- 旧名やboolの受付APIを残す案は、同じ操作の公開面と失敗規約が増えるため採用しない。
- 受付をすべてboolにすると取消・停止・容量不足を区別できなくなるため、共通の結果enumを使う。

## 結果と影響

旧APIの利用側は移行が必要となる。ValueTaskの一回観測、runのDispose、CPUとownerの実行規則は変えない。受付の四状態、容量返却、生成名の衝突、Async送信のコンパイルを回帰テストで確認する。
