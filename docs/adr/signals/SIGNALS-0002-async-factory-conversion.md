# ADR-SIGNALS-0002: 非同期処理の結果と再取得を Signal で観測する

- 状態: 採用
- 日付: 2026-10-31
- 基準コミット: `fbc81051cbf0f4aa5d996237543765e02b8cfa5c`（既存判断の説明を整理する基準）

## 背景

非同期にデータを読む利用者は、処理中・成功・失敗・取消を観測し、再取得後に古い結果が表示されることを防ぎたい。個別の `Task` からこれらを管理すると、状態の通知と、終了時の取消を呼び出し側で組み立てる必要がある。

## 決定

`Signal.FromAsync` で非同期関数を `AsyncSignal<T>` に変換し、現在の処理の状態と結果を一つの読み取り専用 Signal として公開する。再取得と古い結果の破棄を変換側へまとめ、利用者は通知の購読と寿命を管理する。UI の実行環境は要求せず、UI・Navigation・Composition への参照も追加しない。

### 利用者の変更前・変更後

同じファイルを読み、もう一度取得する例を示す。基準コミットで利用できる直接呼び出しと、採用する変換の使い方を対比する。

変更前:

```csharp
using var cancellation = new CancellationTokenSource();
Task<string> pending = File.ReadAllTextAsync("message.txt", cancellation.Token);
string first = await pending;
string next = await File.ReadAllTextAsync("message.txt", cancellation.Token);
```

この形では、処理中かどうかは `Task` から確認し、読み取り失敗は `await` の例外として扱う。状態変更の通知と、並行して再取得した場合の古い結果の破棄は呼び出し側に残る。

変更後:

```csharp
using Lumyte.Signals;

using AsyncSignal<string> message = Signal.FromAsync(
    ct => File.ReadAllTextAsync("message.txt", ct));
Computed<bool> loading = Signal.Computed(() => message.Value.IsLoading);
AsyncSignalState<string> first = await message.Completion;
AsyncSignalState<string> next = await message.ReloadAsync();
if (next.HasValue)
{
    string? text = next.Result;
    // 読み取った値を使用する。
}
```

`loading` は既存の `Computed` から観測できる。読み取り失敗は `Failed` と `Error`、取消は `Canceled` で確認する。例は逐次取得を示すが、処理中に `ReloadAsync` を呼んでも古い結果は公開しない。呼び出し側は使用を終えるまで `message` を保持し、終了時に破棄する。

### 状態と再取得

作成時に非同期関数を一回呼び、`Pending`・`Succeeded`・`Failed`・`Canceled` と結果・例外を不変の状態値として公開する。成功した `null` や `default` は `HasValue` で未完了と区別する。関数は呼び出し元で開始し、同期完了も許す。

`ReloadAsync` は古い処理の公開権を取り消して、新しい処理を開始する。処理ごとの識別情報で照合し、取消を無視して遅れて完了した結果も捨てる。旧 `Completion` は `Canceled` で完了し、旧処理そのものの停止を待たない。新しい `Pending` に旧結果は保持しない。

`Cancel` は実行中の処理を失効させ、`Canceled` を公開する。`Dispose` は最後の状態を読めるまま残し、以後の公開を失効させて取消を要求する。処理の停止を待つ責務とは区別する。

### 通知・失敗・寿命

読み取りは通常の Signal に委譲し、既存の `Computed`・`Effect`・通知の配送先へ接続する。状態の更新と処理の識別確認は同じ排他区間で行い、購読者への通知はその外で行う。完了・再取得・取消で同じ境界を使い、別スレッドへ同期配送する場合も通知先が状態を読めるようにする。

非同期関数の例外は `Failed`、`OperationCanceledException` は `Canceled` として `Completion` から返す。通知の配送失敗は読み取り失敗へ混ぜず、対応する `Completion` を例外で完了させる。`Cancel` の同期通知が失敗した場合は呼び出し元にも同じ例外を返し、旧処理の完了でその結果を上書きしない。取消コールバックの例外も呼び出し元へ返す。

関数の同期部分は `Untracked` で呼び、外側の `Computed` などへ依存の読み取りを漏らさない。`await` をまたぐ暗黙の依存追跡は行わず、再取得は `ReloadAsync` で明示する。

利用者が直接破棄するほか、`SignalScope.Run` 内で作った変換はそのスコープが所有する。スコープは排他区間の外で逆順に破棄し、途中の失敗を集約して残りも解放する。共有する Signal の値や UI は所有しない。

### 公開 API と参照元

基準コミットの `src/Core/Lumyte.Signals/Signal.Async.cs`、`AsyncSignal.cs`、`AsyncSignalState.cs`、`AsyncSignalStatus.cs`、`SignalScope.cs` にある契約を示す。以下は既存宣言の抜粋であり、基準からの差分はないため無印とする。

```diff
 namespace Lumyte.Signals;
 public static partial class Signal
 {
     // 作成時に開始する。null の load は ArgumentNullException。
     public static AsyncSignal<T> FromAsync<T>(Func<CancellationToken, Task<T>> load);
     // 処理の取消に対応しない関数でも、古い結果の公開は抑止する。
     public static AsyncSignal<T> FromAsync<T>(Func<Task<T>> load);
 }
 public enum AsyncSignalStatus { Pending, Succeeded, Failed, Canceled }
 public sealed record AsyncSignalState<T>(
     AsyncSignalStatus Status, T? Result = default, Exception? Error = null)
 {
     public bool IsLoading { get; }
     // 成功した null/default も true。
     public bool HasValue { get; }
 }
 public sealed class AsyncSignal<T> : IReadSignal<AsyncSignalState<T>>, IDisposable
 {
     public AsyncSignalState<T> Value { get; }
     // 依存を記録せず現在の状態を読む。
     public AsyncSignalState<T> Peek();
     // 最新の処理の完了。取得済みの Task は再取得後も元の処理を表す。
     public Task<AsyncSignalState<T>> Completion { get; }
     // 状態と再取得、通知・失敗・寿命の契約に従う。
     public Task<AsyncSignalState<T>> ReloadAsync();
     public void Cancel();
     public void Dispose();
 }
```

## 検討した代替案

既存の同期 `Computed` を非同期対応にする案は、導出値を作る入口を統一できる。一方、同期の読み取りと非同期処理の完了では契約が異なるため、状態と完了待機を持つ独立した変換を採用する。

## 結果と影響

利用者は UI を用意せず、結果と状態を既存の Signal から観測できる。再取得後に古い結果を捨てる処理を共通化できる一方、変換の寿命、明示的な再取得、通知の配送失敗への対応は利用者に残る。入力変更による自動再取得と、再取得中の旧結果保持は行わない。

検証では同期・非同期完了、成功した `null`・`default`、読み取り失敗、取消、再取得後の古い完了、破棄後の通知抑止を確認する。スコープの途中失敗、取消コールバックの再入、通知例外と `Completion` の一致、依存読み取りの分離、別スレッドへの同期配送も受け入れ条件とする。
