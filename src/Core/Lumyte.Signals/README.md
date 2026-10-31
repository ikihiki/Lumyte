# Lumyte.Signals

.NET 10向けの、UIやCompositionに依存しない状態・導出値・同期Effectです。`Signal.Create`、`Signal.Computed`、`Signal.Effect`、`Signal.Batch`だけで動作し、runtimeや手動pumpは不要です。

```csharp
var count = Signal.Create(0);
var doubled = Signal.Computed(() => count.Value * 2);
using var effect = Signal.Effect(() => Console.WriteLine(doubled.Value));
Signal.Batch(() => { count.Value = 1; count.Value = 2; });
```

グラフ操作は内部で直列化し、生成threadへの固定を要求しません。通常Effectは変更したcaller上で実行します。UIなど配送先を持つhostはISignalSchedulerを内部接続します。Effect/Computed/comparerは短い同期評価とし、その内部からの状態書込み・Effect/scope/batch操作を拒否します。

EffectのDisposeまたは任意のSignalScopeが購読を返します。Signal/ComputedはGCで管理し、共有値の寿命を購読へ従属させません。CaptureReads/SignalReadSetはhostが構築と購読準備を別々に行うための受動的な接続APIです。

依存辞書の再利用に`Microsoft.Extensions.ObjectPool` 10.0.0を使用します。DIコンテナやサービス登録は不要です。返却時に参照を消去し、容量1,024以下の辞書を最大64個保持します。この上限は未使用辞書の保持数であり、グラフの大きさや同時に使用する辞書数を制限しません。外部へ渡す`SignalReadSet`の辞書はプールへ返しません。測定方法と結果は[ベンチマーク](../../../benchmarks/Lumyte.Signals.Benchmarks/README.md)を参照してください。

## async関数からの変換

`Signal.FromAsync`は処理を一回開始し、待機・成功・失敗・キャンセルを一つの読み取り専用Signalとして公開します。

```csharp
using AsyncSignal<string> text = Signal.FromAsync(
    async ct => await File.ReadAllTextAsync("message.txt", ct));

using SignalEffect effect = Signal.Effect(() =>
{
    AsyncSignalState<string> state = text.Value;
    Console.WriteLine(state.Status switch
    {
        AsyncSignalStatus.Pending => "Loading...",
        AsyncSignalStatus.Succeeded => state.Result,
        AsyncSignalStatus.Failed => state.Error!.Message,
        AsyncSignalStatus.Canceled => "Canceled",
        _ => throw new InvalidOperationException(),
    });
});

AsyncSignalState<string> first = await text.Completion;
AsyncSignalState<string> refreshed = await text.ReloadAsync();
```

`Func<Task<T>>`も直接渡せます。ValueTaskを返す関数は`ct => LoadAsync(ct).AsTask()`の形で渡せます。`Value`は依存追跡され、`Peek()`は追跡しません。`Signal.Computed`で結果や表示用の文字列を導出でき、UIへの依存はありません。成功したnull/defaultは`HasValue == true`になり、未完了と区別できます。

- 同期完了ならFromAsyncから戻る時点で完了状態になり得ます。factoryはcaller上で開始し、Task.Runへ強制的に移しません。await後の通知は既存のEffect schedulerで配送します。
- `ReloadAsync()`はPendingへ戻し、新しい処理を開始します。旧処理のtokenをキャンセルし、旧CompletionをCanceledで完了させます。キャンセルを無視した旧処理の成功・失敗も現在値へ反映しません。Pendingへ戻る際に以前の結果は保持しません。
- `Cancel()`は現在の処理を取り消してCanceledを通知し、再取得は引き続き可能です。現在の処理がなければ何もしません。
- loaderの例外はFailed/Error、OperationCanceledExceptionはCanceledとしてCompletionから返します。状態通知中のobserverエラーはloaderのエラーへ変換せず、Completionのfaultとして報告します。キャンセルcallback自体の例外はCancel/ReloadAsync/Disposeの呼出元へ伝播します。
- Disposeは公開を失効させ、処理をキャンセルします。以後は通知せず、最後のValue/Peekは読み取れます。実行中だったCompletionはCanceledで完了しますが、表示値を破棄時に書き換えません。既に開始された処理そのものの停止はloaderのキャンセル対応に依存します。
- activeな`SignalScope.Run`内で作ったAsyncSignalは、そのscopeが所有します。scopeはEffectと非同期処理を逆順で解放し、キャンセルcallbackが失敗しても残りを清掃します。共有のSignal/Computedは所有しません。
- factory内のSignal読み取りによる自動再実行は行いません。同期部分の読み取りも外側の依存追跡へ漏らさず、awaitをまたいだ追跡も追加しません。入力を変えた後の再取得はReloadAsyncで明示します。非同期関数をComputedや同期Effectへ渡すAPIではありません。
