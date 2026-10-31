# ADR-SIGNALS-0002: 非同期関数からのSignal変換

- 状態: 採用（main向け独立package）
- 日付: 2026-10-31

## 背景

UIを使わないアプリでも非同期処理の結果をSignalとして観測し、再取得と終了時のキャンセルを管理したい。

## 決定

独立したSignalsへ`Signal.FromAsync<T>(Func<CancellationToken, Task<T>>)`と`Func<Task<T>>`版を追加する。戻り値は`AsyncSignal<T> : IReadSignal<AsyncSignalState<T>>, IDisposable`とし、UI・Navigation・Compositionを参照しない。

```csharp
using AsyncSignal<string> message = Signal.FromAsync(
    ct => File.ReadAllTextAsync("message.txt", ct));
Computed<bool> loading = Signal.Computed(() => message.Value.IsLoading);
AsyncSignalState<string> first = await message.Completion;
AsyncSignalState<string> next = await message.ReloadAsync();
```

factoryを一回実行し、Pending/Succeeded/Failed/CanceledとResult/Errorをimmutableな単一snapshotで公開する。成功のnull/defaultはHasValueで未完了と区別する。読み取りは通常のSignalへ委譲し、既存Computed・Effect・schedulerへ接続する。同期完了も許容し、factoryをTask.Runへ移すことはしない。

ReloadAsyncは古い処理の公開権を取り消してから新しい処理を開始する。処理ごとのidentityを照合し、キャンセルを無視して遅れて完了した結果も捨てる。旧CompletionはCanceledとして完了し、旧Taskそのものの停止を完了条件にしない。新しいPendingには旧Resultを保持しない。CancelはCanceledを公開し、Disposeは最後のsnapshotを保ったまま以降の公開を失効させる。

loaderの例外はFailed、OperationCanceledExceptionはCanceledとしてCompletionから返す。observerの配送例外をloader失敗へ混ぜず、その処理のCompletionをfaultさせる。キャンセルcallback自体の例外は呼出元へ返す。SignalScopeは作成した非同期adapterも所有し、gate外で逆順に清掃する。清掃の途中失敗も集約して残りを解放する。共有値やUIをscopeへ所有させない。

外側の構築処理やComputedへloaderのSignal読出しが漏れないよう、factoryの同期呼出しもUntrackedで実行する。awaitをまたいだ暗黙の依存追跡・非同期Computedは追加せず、再取得はReloadAsyncで明示する。呼出元がDisposeするか、独立したSignalScopeの寿命で所有する。

## 検証方針

検証は同期/非同期完了、null/default、loader失敗、キャンセル、再取得後の古い完了、Dispose後の通知なし、scopeの途中失敗、キャンセルcallbackの再入、observer例外、依存読出しの分離を独立Signalsのテストで行う。

## 結果と影響

呼出元はruntimeやUIを用意せず結果と状態を観測できる。awaitをまたいだ暗黙の依存追跡、旧結果の保持、入力変更による自動再取得は含めない。既存の同期Computedをasync対応に変える案は、読出しと完了の契約が異なるため採用しない。
