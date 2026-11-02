# UI値アニメーションのCPU測定

2026-10-30、Linux x64、.NET SDK 10.0.401、Release、Xeon Platinum 8573C（割当5 CPU）で測定した。単一process内で20回warm-up後、120回の平均を取る。1個の目標値producerを1/100/1000個のRectangleの幅へ共有し、60Hz相当のManualClockを進める。

| Widget数 | 動作中 ms/frame | 動作中 bytes/frame | 停止後 µs/check | 停止後 bytes/check |
| --- | --- | --- | --- | --- |
| 1 | 0.041 | 12,864 | 0.323 | 0 |
| 100 | 1.527 | 332,128 | 11.281 | 0 |
| 1000 | 3.675 | 3,149,562 | 66.423 | 0 |

測定対象はAdvanceAnimations、dispatcher配送、宣言の再構築、layoutである。Record、GPU、入力、OS提示、独立した多数のproducerは含めない。停止後は完了値を反映し、20回warm-up後にAdvanceAnimationsと二つの状態flagを1000回確認する。経過時間とスレッドごとの確保bytesを測り、frame内でGCを強制しない。

動作中は[UI-0019](../adr/ui/UI-0019-signals-and-reactive-state.md)の宣言再構築を使うため、確保量は変更対象のtreeに比例する。1000要素の毎frame更新は約3MBを確保しており、GPU込みのframe budgetや60fpsを保証する値ではない。停止後は可視性と保留状態を確認し、補間・再構築・timer生成を行わない。JIT tiering、GC、他process、machine差によって時間は変動する。独立producer、大きな画面、GPUを含む測定は別に行う。

## 再実行用プログラム

リポジトリ外へnet10.0のconsole projectを作り、`src/UI/Lumyte.UI/Lumyte.UI.csproj`をProjectReferenceへ指定する。以下をProgram.csへ置き、`dotnet run -c Release`で実行する。ビルドは通常のリポジトリの環境設定に従う。ビルド時間やアプリ開始時間は計測区間に含めない。

```csharp
using System.Diagnostics;
using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Signals;
using Lumyte.UI;
using static Lumyte.UI.Compose;

foreach (int count in new[] { 1, 100, 1000 })
{
    var dispatcher = new Dispatcher();
    var clock = new ManualClock();
    var target = Signal.Create<float?>(10);
    var width = Animate(target, Duration.FromSeconds(1000), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear);
    var root = Panel();
    root.Children.Initialize(Enumerable.Range(0, count).Select(_ => (Definitions.Widget)Rectangle(width: width, height: 10)).ToArray());
    using var surface = new UiSurface(root, dispatcher, clock);
    surface.AdvanceAnimations();
    dispatcher.Pump();
    target.Value = 100;
    for (int i = 0; i < 20; i++) Frame();
    long bytes = GC.GetAllocatedBytesForCurrentThread();
    long start = Stopwatch.GetTimestamp();
    const int frames = 120;
    for (int i = 0; i < frames; i++) Frame();
    double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds / frames;
    double allocated = (GC.GetAllocatedBytesForCurrentThread() - bytes) / (double)frames;
    Console.WriteLine($"{count} widgets shared producer: {ms:F3} ms/frame; {allocated:F0} bytes/frame; CPU animation+rebuild+layout (no Record/GPU)");
    clock.Advance(Duration.FromSeconds(1000));
    surface.AdvanceAnimations();
    dispatcher.Pump();
    if (surface.HasRunningAnimations || surface.HasPendingAnimationUpdates) throw new Exception("Retained animation work");
    for (int i = 0; i < 20; i++) surface.AdvanceAnimations();
    long idleBytes = GC.GetAllocatedBytesForCurrentThread();
    long idleStart = Stopwatch.GetTimestamp();
    for (int i = 0; i < 1000; i++)
    {
        surface.AdvanceAnimations();
        if (surface.HasRunningAnimations || surface.HasPendingAnimationUpdates) throw new Exception("Idle work");
    }
    Console.WriteLine($"{count} widgets stopped: {Stopwatch.GetElapsedTime(idleStart).TotalMicroseconds / 1000:F3} us/check; {(GC.GetAllocatedBytesForCurrentThread() - idleBytes) / 1000.0:F0} bytes/check");
    void Frame()
    {
        clock.Advance(Duration.FromSeconds(1.0 / 60));
        surface.AdvanceAnimations();
        dispatcher.Pump();
        surface.UpdateLayout();
    }
}
sealed class Dispatcher : IUiDispatcher
{
    readonly Queue<Action> queue = new();
    public bool CheckAccess() => true;
    public ValueTask InvokeAsync(Action action, CancellationToken token = default)
    {
        var done = new TaskCompletionSource();
        queue.Enqueue(() => { action(); done.SetResult(); });
        return new(done.Task);
    }
    public ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken token = default)
    {
        var done = new TaskCompletionSource<T>();
        queue.Enqueue(() => { done.SetResult(action()); });
        return new(done.Task);
    }
    public void Pump() { while (queue.TryDequeue(out var work)) work(); }
}
```
