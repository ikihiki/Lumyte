# Lumyte.Animation

単調時計を使って汎用の型付き値を計算するライブラリです。カーブ・Tween、順次・並列・遅延、有限 Repeat、Reverse、Loop、マーカーを扱います。状態機械は別 PR の対象です。

設定は既存の Lumyte.Composition と Generator で構築します。生成済み API を公開するため、利用側で Generator を参照する必要はありません。

```csharp
using Lumyte.Animation;
using Lumyte.Core.Time;
using static Lumyte.Animation.ComposeAnimation;

var channel = AnimationChannel<float>.Create();
var tween = new Tween<float>(0, 1, Duration.FromSeconds(1),
    AnimationInterpolators.Float, AnimationEasing.Linear);
var timeline = Timeline()[Repeat(3)[Track<float>(channel, tween)]].Build();
var clock = new ManualClock();
var playback = new AnimationPlayback(clock, timeline);
var output = new AnimationOutput();
var events = new System.Collections.Generic.List<AnimationEventOccurrence>();
playback.Play();
clock.Advance(Duration.FromSeconds(0.5));
output.Clear();
events.Clear();
playback.Update(output, events);
output.TryGet(channel, out var value); // 0.5。
// value の適用と events の配送は消費側で行います。
```

実環境の時計には SystemMonotonicClock、シミュレーションやテストには ManualClock を使えます。Pause／Resume／Seek／速度変更は位置を時計上の基準に固定し、適用やイベント配送を呼び出しません。

Output とイベントの格納先は再利用し、更新前に消費側がクリアします。同じ Output へ複数の再生結果を集める場合は後の更新を優先します。各時計・再生者・出力は同時更新しないでください。

- [設計 ADR](../../../docs/adr/animation/ANIMATION-0001-animation-system.md)
- [実行可能なサンプル](../../../samples/Lumyte.Animation.Sample/README.md)

状態機械の Composition 定義は `new AnimationStateMachine<TState, TContext>(clock, definition)` に直接渡す。子の Timeline と汎用制御を一度だけ自動構築し、`Start(context)` で再生を開始する。[状態機械の例](../../../samples/Lumyte.Animation.StateMachine.Sample/README.md)を参照。
