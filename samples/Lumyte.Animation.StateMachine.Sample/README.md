# アニメーション状態機械のサンプル

Composition の状態機械ノードに `Build(clock, context)` を呼び、内部制御とネストした Timeline の構築から初期入場・再生開始までを一度に行う。トリガーで有限 Repeat／Reverse のアクションを開始し、完了時のマーカーを収集して待機へ戻る。値の取得・適用は消費側で行う。

```sh
dotnet run --project samples/Lumyte.Animation.StateMachine.Sample
```

この例ではマーカーが 0.8 秒で発生し、1 秒の Update で完了遷移を観測する。`events[0].OccurredAt` と `transitions[0].ObservedAt` の差を取得でき、配送側で内部の時計基準を推定する必要はない。

通常の Builder も開始済みの実行者を直接返す。

```csharp
var builder = new AnimationStateMachineBuilder<Motion, MotionInput>();
builder.AddState(Motion.Idle, Timeline()[Track<float>(amount, idle)], AnimationWrapMode.Loop);
var machine = builder.Build(clock, Motion.Idle, new MotionInput(Enabled: true));
```

構築だけを先に行う場合は `new AnimationStateMachine<Motion, MotionInput>(clock, definition)` を使用し、必要な時点で `Start(context)` を呼ぶ。子タイムラインを個別に Build する必要はない。詳しい契約と使用例は [ADR-ANIMATION-0003](../../docs/adr/animation/ANIMATION-0003-animation-execution-and-event-time.md) を参照。
