# Lumyte.Animation

単調時計を使って汎用の型付き値を計算するライブラリです。カーブ・Tween、順次・並列・遅延、有限 Repeat、Reverse、Loop、マーカーと、条件・トリガーでタイムラインを切り替える状態機械を扱います。

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

状態機械の Composition ノードは `definition.Build(clock, context)`、Builder は `builder.Build(clock, initialState, context)` で開始済みの実行者を返します。子の Timeline と汎用制御を一度だけまとめて構築するため、子ごとの Build や追加の Start は不要です。

構築と開始を分ける場合は `new AnimationStateMachine<TState, TContext>(clock, definition)` で停止中の実行者を作り、`Start(context)` で初期入場と再生を開始します。状態マーカーの `OccurredAt` と遷移の `ObservedAt` は同じ時計上の絶対時点なので、消費側で時系列に並べられます。異なる時計の時点は比較しないでください。[構築とイベント時刻の設計](../../../docs/adr/animation/ANIMATION-0003-animation-execution-and-event-time.md)と[実行可能な例](../../../samples/Lumyte.Animation.StateMachine.Sample/README.md)を参照してください。

区間ごとの Hold、時間ベジェ、空間ベジェ、秒単位の接線付き Hermite、時間再マッピングと二入力ブレンドも値ソースとして使用できます。これらはフォーマットに依存せず、読み込みと描画・ボーンへの適用は利用側が担当します。Lottie／Rive 等の完全な再生対応を意味するものではありません。

```csharp
var duration = Duration.FromSeconds(2);
var channel = AnimationChannel<float>.Create();
var curve = Curve<float>(duration, AnimationInterpolators.Float)[
    new AnimationKey<float>(Duration.Zero, 0)
    {
        Timing = AnimationTimings.CubicBezier(0.42, 0, 0.58, 1),
    },
    new AnimationKey<float>(duration, 100)];
var time = Curve<Duration>(duration, AnimationInterpolators.Duration)[
    new AnimationKey<Duration>(Duration.Zero, duration),
    new AnimationKey<Duration>(duration, Duration.Zero)];
var timeline = Timeline()[SourceTrack<float>(channel,
    TimeRemap<float>(value: curve, timeMap: time))].Build();
```

子の Curve／TimeRemap／Blend ごとの Build は不要です。単独の値計算を使う場合は Source 定義の Build から `IAnimationSource<T>` を得られます。時間ベジェは x を逆算し、y のオーバーシュートを維持します。離散値にはキーの `Hold = true` を使用してください。時間写像の範囲外や不正な重みは評価時に拒否します。キー配列はコピーしますが、独自ソース・補間器や参照型の値は利用側で不変に保ってください。

[API 差分と契約・合成例](../../../docs/adr/animation/ANIMATION-0004-format-independent-value-sources.md)を参照してください。

汎用のベジェ・Hermite・Quaternion・イージング計算は依存パッケージ[Lumyte.Mathematics](../../Core/Lumyte.Mathematics/README.md)が提供します。AnimationはDurationやキーを扱うアダプターです。

同じSource定義を共有する場合、Build内で構築結果を再利用します。共有グラフのSampleでは同じ子・同じ局所時刻の値を一回の評価内で再利用し、時間写像による異なる時刻の呼び出しを区別します。作業領域は評価間でクリアし、並行・再入評価には別の領域を使います。
