# ADR-ANIMATION-0001: 単調時計とタイムラインによる汎用値の計算

- 状態: 採用
- 日付: 2026-10-08

## 背景

Lumyte のアニメーションシステムは、ボーン制御から UI の位置・サイズ・色・透明度まで、時間によって値が変わる事象と、その変化の実行タイミングを扱う。値を対象へ適用する責務は消費側にある。ボーン関連のデータ構造とボーンへの適用も別の消費側プロジェクトが管理する。

旧ライブラリの [Lumyte.Animation](https://github.com/ikihiki/Lumyte_old/tree/462e5b3e1e65c37ab2d1df6ba9ddde9f287690ee/src/interaction/Lumyte.Animation) は単調時計、型付き時間、汎用チャネルとタイムラインを備える。その時間管理を参考にし、現在の計算と適用を分ける責務に合わせて公開契約を定める。

今回の PR は値評価と再生・タイムラインの設計を対象とする。状態機械の設計・実装・Composition 定義は別 PR に分離し、今回の公開 API と利用例には含めない。

## 決定

不変の値ソース・タイムラインと、時計に基づく可変の再生状態を分離する。カーブ、Tween、遅延、順次・並列実行、有限回の Repeat、Reverse、ループとイベント収集を提供する。計算結果とイベントの出力までを Animation の責務とし、対象の参照・setter・適用処理を保持しない。

### 配置と依存方向

[ADR-0002](../0002-repository-layout.md) に従い、プロジェクト・NuGet 名を `Lumyte.Animation`、配置を `src/Animation/Lumyte.Animation/` とする。本 PR で Core の時間型と Animation の評価・再生・構築 API を実装する。

- `Lumyte.Animation → Lumyte.Core.Time` の時間契約と `Lumyte.Composition` を参照する。実際の時間型の配置は `Lumyte.Core` とし、名前空間を `Lumyte.Core.Time` とする。
- 既存の `Lumyte.Composition.Generators` をビルド時の Analyzer として使い、生成済み構築 API を公開する。別の Composition 連携プロジェクトや Generator は設けない。
- Animation はボーンのプロジェクト、シーン、ECS、UI、Graphics、OS、Native に依存しない。基本数値型は System.Numerics を使用する。
- 消費側が時計、対象とチャネルの対応、値の適用、イベント配送を所有する。ボーン固有の構造・ポーズ合成・ルートモーションは消費側の責務とする。

```text
消費側が選択した単調時計
          ↓
再生状態 → タイムライン → 型付き値の評価
                              ↓
                      Output とイベント
                              ↓
                  消費側で取得・適用・配送
```

### 時間管理

旧ライブラリの `IMonotonicClock`、`Duration`、`TimePoint` を時間管理の基準とする。実行時に double 秒の差分を毎更新で加算する方式は使用しない。

| 型・機能 | 契約 |
| --- | --- |
| `IMonotonicClock.Now` | カレンダー時刻と無関係で、逆行しない TimePoint |
| `Duration` | 符号付きの時間幅。TimeSpan と同じ 100 ns 単位の long Ticks を保持する |
| `TimePoint` | 時計上の時点。時点の差は Duration、時点に Duration を加えると TimePoint |
| `SystemMonotonicClock` | Stopwatch に基づく時計。壁時計の変更に影響されない |
| `ManualClock` | Advance により非負の Duration だけ進めるテスト・シミュレーション用時計 |

これらの Core 時間型を本 PR で共通契約として導入し、Animation 独自の時間型は作らない。Duration は一般の時間幅として負値も表せるが、アニメーションの長さ・開始時刻・Seek の位置は非負に制限する。秒からの変換、加減算、時間倍率、Repeat の長さ計算は範囲を検証し、オーバーフローを検出する。

再生者は時計をコンストラクターで受け取り、再生中の Update が一度だけ Now を読む。再生位置は、基準位置と基準時点からの経過時間を使って `anchorPosition + (now - anchorClock) * speed` として求める。毎更新の丸め誤差を累積させない。時間の格納単位は 100 ns の整数 Tick とし、速度倍率の適用、マーカー通過時刻の逆算、補間率とイージングの計算には浮動小数点を使用する。秒・倍率から Tick へ戻す際は小数部をゼロ方向に切り捨て、範囲外は例外とする。整数 Tick での格納は、任意の速度倍率で 100 ns 精度の計算を保証するものではない。

Pause は停止時の位置を固定し、Resume は再開時点を新しい基準にする。Seek と Speed の変更も、その操作時点の位置を基準として更新する。操作は値の適用やイベント配送を行わず、次の Update が結果を出力する。ゲーム用の ManualClock と UI 用の SystemMonotonicClock など、時計の選択は消費側が行う。一時停止によるゲーム時計の停止は UI の再生に影響しない。

### 値評価とチャネル

`IAnimationSource<T>` は `[Duration.Zero, Duration]` の時刻から T を返す純粋な値ソースとする。キーフレームカーブと Tween は同じ契約を実装する。カーブのキーは最低一つ、時刻は厳密に昇順で長さの範囲内とし、端のキー以前・以後は端の値を保持する。

標準補間は float、Vector2／3／4 の Lerp、Quaternion の最短経路の Slerp と正規化、離散値の Step とする。Step は区間の途中では始値を保持し、終端で終値へ切り替える。有限な float の始値・終値を `[0, 1]` で補間する場合は、中間演算のオーバーフローによって結果を非有限にしない。独自型は `IAnimationInterpolator<T>` で拡張する。Tween の始値・終値は明示的に渡し、現在値を使いたい場合は消費側が先に取得する。イージングは補間率を変換し、標準では `[0, 1]` と端点を保持する。

`AnimationChannel<T>` は出力を区別する不透明な識別子とし、対象・プロパティとの対応は消費側だけが保持する。AnimationOutput は型付き結果を格納し、TryGet で取得する。異なる T は暗黙変換しない。複数の再生結果を同じ Output に集める場合、後に評価した寄与を優先する。各更新前の Clear と評価順は消費側が制御する。

### タイムライン、Repeat、Reverse

AnimationTimeline は値項目とマーカーの不変定義とし、Builder と Composition の両方で構築できる。子を同じ開始時刻へ置くと並列、直前の子の終了時刻へ置くと順次実行になる。ネストは登録順を保持して評価する。

開始前の値項目は出力しない。Hold は終了後も終値を出力し、Release は終端へ到達した更新で終値を一度出力した後に寄与を停止する。大きな時計差で項目を飛び越しても終値を出力する。解放は対象の元値への復元を意味しない。Seek は移動先の有効な値だけを再評価し、通過したマーカーを収集しない。同じチャネルの項目が重なる場合は後の登録を優先する。

| 演算 | 長さと時刻の規則 |
| --- | --- |
| `Repeat(count)` | 正の回数だけ子全体を繰り返す。長さは `checked(child.Duration.Ticks * count)`。途中の周境界は次周の時刻 0、最終終端だけは子の終端を評価する |
| `Reverse()` | 子の長さを保持し、区間内の時刻 t を `child.Duration - t` へ写像する。速度を負にせず、単調時計を逆行させない |
| 再生の Loop | タイムライン全体を無期限に繰り返す。有限長を構成する Repeat と区別する |

Repeat は登録数を count 倍に複製せず、子と回数を保持する。周をまたぐ際、前周の Hold が次周の待機区間へ漏れないよう、子の評価状態を周ごとに扱う。Repeat／Loop の新しい周で同じチャネルに有効な値があれば、通過した前周の Release 終値より新しい周の値を優先する。同じ周の寄与同士は登録順で決める。Reverse は子の元時刻で有効な値を評価するため、子の終了後の Hold は反転後の先頭側に現れる。子に Release がある場合は逆向きに区間を離れる更新でも最後の値を一度出力し、その後寄与を停止する。イベントは値評価と独立して反転後の前進区間から収集する。

Reverse のマーカー時刻は `child.Duration - marker.Time` とする。Repeat では各周の開始時刻を加える。同じ変換後時刻のマーカーは構造上の登録順を保持する。周境界では前周の終端マーカーを先に、次周の 0 のマーカーを後に出す。有限 Repeat の最終終端には存在しない次周の 0 を出さない。Repeat／Reverse をネストしても最終的な再生時刻順で収集する。

### 公開 API の追加差分

以下は実装本体を省略した設計宣言とする。今回の範囲に状態機械の API は含めない。

```diff
+using System.Collections.Generic;
+using System.Numerics;
+using Lumyte.Core.Time;
+
+namespace Lumyte.Animation;
+
+public enum AnimationWrapMode { Once, Loop }
+public enum AnimationFillMode { Hold, Release }
+public enum AnimationPlaybackState { Stopped, Playing, Paused, Completed, Cancelled }
+public enum AnimationEasing { Linear, EaseIn, EaseOut, EaseInOut }
+
+public interface IAnimationSource<T>
+{
+    Duration Duration { get; }
+    T Sample(Duration time);
+}
+public readonly record struct AnimationKey<T>(Duration Time, T Value);
+public interface IAnimationInterpolator<T>
+{
+    T Interpolate(T from, T to, float amount);
+}
+public static class AnimationInterpolators
+{
+    public static IAnimationInterpolator<float> Float { get; }
+    public static IAnimationInterpolator<Vector2> Vector2 { get; }
+    public static IAnimationInterpolator<Vector3> Vector3 { get; }
+    public static IAnimationInterpolator<Vector4> Vector4 { get; }
+    public static IAnimationInterpolator<Quaternion> Quaternion { get; }
+    public static IAnimationInterpolator<T> Step<T>();
+}
+public sealed class AnimationCurve<T> : IAnimationSource<T>
+{
+    public AnimationCurve(Duration duration, IReadOnlyList<AnimationKey<T>> keys,
+        IAnimationInterpolator<T> interpolator);
+    public Duration Duration { get; }
+    public T Sample(Duration time);
+}
+public sealed class Tween<T> : IAnimationSource<T>
+{
+    public Tween(T from, T to, Duration duration,
+        IAnimationInterpolator<T> interpolator, AnimationEasing easing);
+    public Duration Duration { get; }
+    public T Sample(Duration time);
+}
+public sealed class AnimationChannel<T>
+{
+    private AnimationChannel();
+    public static AnimationChannel<T> Create();
+}
+public sealed class AnimationTimeline
+{
+    internal AnimationTimeline();
+    public Duration Duration { get; }
+    public AnimationTimeline Repeat(int count);
+    public AnimationTimeline Reverse();
+}
+public sealed class AnimationTimelineBuilder
+{
+    public AnimationTimelineBuilder();
+    public void Add<T>(Duration start, IAnimationSource<T> source,
+        AnimationChannel<T> channel, AnimationFillMode fill = AnimationFillMode.Hold);
+    public void AddTimeline(Duration start, AnimationTimeline timeline);
+    public void AddEvent(Duration time, string name, string? payload = null);
+    public void SetDuration(Duration duration);
+    public AnimationTimeline Build();
+}
+public sealed class AnimationOutput
+{
+    public AnimationOutput();
+    public void Clear();
+    public bool TryGet<T>(AnimationChannel<T> channel, out T value);
+}
+public readonly record struct AnimationEvent(Duration Time, string Name, string? Payload);
+public readonly record struct AnimationEventOccurrence(
+    AnimationEvent Event, long LoopIndex, Duration UpdateOffset);
+public sealed class AnimationPlayback
+{
+    public AnimationPlayback(IMonotonicClock clock, AnimationTimeline timeline,
+        AnimationWrapMode wrapMode = AnimationWrapMode.Once);
+    public AnimationPlaybackState State { get; }
+    public Duration Position { get; }
+    // 有限かつ 0 以上、既定 1。変更時に現在位置を維持する。
+    public double Speed { get; set; }
+    public void Play();
+    public void Pause();
+    public void Resume();
+    public void Stop();
+    public void Seek(Duration position);
+    public void Cancel();
+    // 単調時計を一度読み、結果を追記する。今回完了した場合だけ true。
+    public bool Update(AnimationOutput output, ICollection<AnimationEventOccurrence> events);
+}
```

SetDuration は正の長さを指定する。項目・イベントの最大終了時刻より短い明示長は Build 時に拒否する。未指定なら最大終了時刻で決める。値ソースと最終タイムラインは正の長さを要求し、Delay だけで長さを持つタイムラインも許容する。

### 再生状態とイベント

- 初期状態は Stopped、位置は 0。Play は開始し、Playing では何もしない。Paused では Resume と同じ動作、Completed／Cancelled では 0 から再開する。
- Pause は Playing だけを停止する。Resume は Paused だけを再開する。Stop はどの状態からも Stopped にして位置を 0 に戻し、Cancel は位置を保持して Cancelled にする。いずれも対象の値を変更しない。
- Stopped／Cancelled の Update は出力しない。Paused は停止位置の有効な値を、Completed は Hold 項目だけを評価する。Paused の Update はマーカーと Release 終端の走査基準を進めず、Pause 前の未評価区間を Resume 後の Update で収集する。時計が進まない場合はイベントを再収集しない。Once の終端更新だけが完了を返す。
- Loop は折り返し前の累積位置を内部で保持する。Position は `[0, Duration)`、Seek(Duration) は次周の 0 と同等に扱う。Seek は `[0, Duration]` だけを許容し、イベント収集の基準も更新する。
- イベントは前回位置から今回位置への `(前回, 今回]` の前進区間で収集する。初回の正の前進と各周の開始では時刻 0 のマーカーを一度収集する。Seek の移動先の始点イベントは収集しない。
- 複数周を飛び越す更新も全マーカーを収集する。UpdateOffset は前回のイベント走査基準から見た時計上の Duration とし、速度を反映したマーカー通過位置から算出する。Play／Seek は基準をその操作時点に設定し、Paused の Update は基準を保持する。LoopIndex は再生全体の外側の周番号であり、有限 Repeat の内部周は構造から区別する。
- 評価中に消費側のコールバックを呼ばない。events への追記先は再入しないコレクションとし、消費側が全評価後に配送する。

### Lumyte.Composition による構築

[ADR-COMPOSITION-0001](../composition/COMPOSITION-0001-declarative-composition.md) の既存属性と Generator を使う。構築用ノードは Lumyte.Animation に置き、ComposeAnimation のファクトリから生成する。Timeline.Build が設定を検証して不変の実行定義へ変換し、構築だけで Play や値適用を行わない。

| ノード | 構築規則 |
| --- | --- |
| Timeline / Parallel | 子を並列に置き、最大長を持つ |
| Sequence | 子を登録順に置き、長さの合計を持つ |
| Delay | 指定した Duration の長さだけを持ち、出力しない |
| Track&lt;T&gt; | ソースとチャネルを型付きで登録する |
| Marker | 配置時刻へイベントを登録し、自身の長さは 0 |
| Repeat | 子を一つだけ受け取り、そのタイムラインの Repeat(count) を構築する |
| Reverse | 子を一つだけ受け取り、そのタイムラインの Reverse() を構築する |

Repeat／Reverse の複数の子をまとめたい場合は Sequence／Parallel を一つの子にする。子のないグループは長さ 0 とし、最終定義の正の長さを Build で検証する。負の Delay、Repeat の count が 0 以下、null、循環参照、時刻のオーバーフローは拒否する。同じノードの複数配置は許容し、共有そのものを循環とはしない。

ノードの子配列は Composition の契約どおり参照を保持できるが、Build は定義をコピーする。Build 後のノード変更は既存のタイムラインに影響しない。構築中の同時変更は禁止し、不変ソースは共有する。

```diff
+using System.Collections.Generic;
+using Lumyte.Composition;
+using Lumyte.Core.Time;
+
+namespace Lumyte.Animation;
+
+public static partial class ComposeAnimation
+{
+    public static partial class Definitions
+    {
+        public abstract class TimelineItem { }
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class Timeline : TimelineItem
+        {
+            [ComposeContent]
+            public IReadOnlyList<TimelineItem> Children { get; set; } = [];
+            public AnimationTimeline Build();
+        }
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class Sequence : TimelineItem
+        {
+            [ComposeContent]
+            public IReadOnlyList<TimelineItem> Children { get; set; } = [];
+        }
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class Parallel : TimelineItem
+        {
+            [ComposeContent]
+            public IReadOnlyList<TimelineItem> Children { get; set; } = [];
+        }
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class Delay : TimelineItem
+        {
+            [ComposeParameter]
+            public required Duration Duration { get; init; }
+        }
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class Track<T> : TimelineItem
+        {
+            [ComposeParameter]
+            public required AnimationChannel<T> Channel { get; init; }
+            [ComposeParameter]
+            public required IAnimationSource<T> Source { get; init; }
+            [ComposeParameter]
+            public AnimationFillMode Fill { get; init; } = AnimationFillMode.Hold;
+        }
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class Marker : TimelineItem
+        {
+            [ComposeParameter]
+            public required string Name { get; init; }
+            [ComposeParameter]
+            public string? Payload { get; init; }
+        }
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class Repeat : TimelineItem
+        {
+            [ComposeParameter]
+            public required int Count { get; init; }
+            [ComposeContent]
+            public IReadOnlyList<TimelineItem> Children { get; set; } = [];
+        }
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class Reverse : TimelineItem
+        {
+            [ComposeContent]
+            public IReadOnlyList<TimelineItem> Children { get; set; } = [];
+        }
+    }
+}
```

生成する主な入口を以下に示す。Optional、with、子の置換とインデクサーの契約は既存 Composition の規則に従う。状態機械用のノード・スロットは今回追加しない。

```diff
+using System;
+using System.Collections.Generic;
+using Lumyte.Composition;
+using Lumyte.Core.Time;
+
+namespace Lumyte.Animation;
+
+public static partial class ComposeAnimation
+{
+    public static Definitions.TimelineFactory Timeline { get; }
+    public static Definitions.SequenceFactory Sequence { get; }
+    public static Definitions.ParallelFactory Parallel { get; }
+    public static Definitions.DelayFactory Delay { get; }
+    public static Definitions.MarkerFactory Marker { get; }
+    public static Definitions.RepeatFactory Repeat { get; }
+    public static Definitions.ReverseFactory Reverse { get; }
+    public static Definitions.Track<T> Track<T>(AnimationChannel<T> channel,
+        IAnimationSource<T> source, Optional<AnimationFillMode> fill = default,
+        IReadOnlyList<Action<Definitions.Track<T>>>? with = null);
+    public static Definitions.TrackFactory<T> TrackFactory<T>();
+    public static partial class Definitions
+    {
+        public delegate Timeline TimelineFactory(IReadOnlyList<Action<Timeline>>? with = null);
+        public delegate Sequence SequenceFactory(IReadOnlyList<Action<Sequence>>? with = null);
+        public delegate Parallel ParallelFactory(IReadOnlyList<Action<Parallel>>? with = null);
+        public delegate Delay DelayFactory(Duration duration, IReadOnlyList<Action<Delay>>? with = null);
+        public delegate Marker MarkerFactory(string name, Optional<string?> payload = default,
+            IReadOnlyList<Action<Marker>>? with = null);
+        public delegate Repeat RepeatFactory(int count, IReadOnlyList<Action<Repeat>>? with = null);
+        public delegate Reverse ReverseFactory(IReadOnlyList<Action<Reverse>>? with = null);
+        public delegate Track<T> TrackFactory<T>(AnimationChannel<T> channel,
+            IAnimationSource<T> source, Optional<AnimationFillMode> fill = default,
+            IReadOnlyList<Action<Track<T>>>? with = null);
+        public partial class Timeline { public Timeline this[params TimelineItem[] content] { get; } }
+        public partial class Sequence { public Sequence this[params TimelineItem[] content] { get; } }
+        public partial class Parallel { public Parallel this[params TimelineItem[] content] { get; } }
+        public partial class Repeat { public Repeat this[params TimelineItem[] content] { get; } }
+        public partial class Reverse { public Reverse this[params TimelineItem[] content] { get; } }
+    }
+}
```

### 利用例

以下は公開 API の利用例である。生成 API を別アセンブリから使用するテストと、[実行可能なサンプル](../../../samples/Lumyte.Animation.Sample/README.md)で主要な構築・再生経路を検証する。

#### 指定時刻の値だけを計算する

```csharp
using Lumyte.Animation;
using Lumyte.Core.Time;

var opacity = new Tween<float>(0f, 1f, Duration.FromSeconds(0.2),
    AnimationInterpolators.Float, AnimationEasing.Linear);
float value = opacity.Sample(Duration.FromSeconds(0.1)); // 0.5。適用は行わない。
```

#### UI の順次・並列実行と単調時計

パネルのフェードと、遅延後のボタンのフェードを並列に構成する。消費側が時計を進めて評価結果を取得し、自身の setter に適用する。

```csharp
using System;
using System.Collections.Generic;
using Lumyte.Animation;
using Lumyte.Core.Time;
using static Lumyte.Animation.ComposeAnimation;

var opacity = AnimationChannel<float>.Create();
var buttonOpacity = AnimationChannel<float>.Create();
var opening = Timeline()[
    Sequence()[
        Parallel()[
            Track<float>(channel: opacity, source: new Tween<float>(
                0f, 1f, Duration.FromSeconds(0.2),
                AnimationInterpolators.Float, AnimationEasing.Linear)),
            Sequence()[
                Delay(duration: Duration.FromSeconds(0.2)),
                Track<float>(channel: buttonOpacity, source: new Tween<float>(
                    0f, 1f, Duration.FromSeconds(0.1),
                    AnimationInterpolators.Float, AnimationEasing.Linear))
            ]
        ],
        Marker(name: "PanelOpened")
    ]
].Build();

var clock = new ManualClock();
var playback = new AnimationPlayback(clock, opening);
var output = new AnimationOutput();
var events = new List<AnimationEventOccurrence>();
playback.Play();
clock.Advance(Duration.FromSeconds(0.25));
output.Clear();
events.Clear();
playback.Update(output, events); // deltaSeconds を渡さない。
output.TryGet(opacity, out var panelValue);        // 1。
output.TryGet(buttonOpacity, out var buttonValue); // 約 0.5。

// 消費側だけが適用処理を保持する。実際の UI setter を引数として渡す。
void ApplyUi(Action<float> setPanel, Action<float> setButton)
{
    if (output.TryGet(opacity, out var p)) setPanel(p);
    if (output.TryGet(buttonOpacity, out var b)) setButton(b);
}
```

実際の UI では SystemMonotonicClock を渡し、各フレームで同じ再生者を一度 Update する。ManualClock はテストやシミュレーションで使う。イベント配送は全評価と適用の後に消費側で行う。

#### Repeat／Reverse とボーン消費側

回転カーブの正方向と逆方向を順次再生し、その全体を 3 回繰り返す。ボーン型を Animation に持ち込まず、消費側に回転を渡す。

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using Lumyte.Animation;
using Lumyte.Core.Time;
using static Lumyte.Animation.ComposeAnimation;

var rotation = AnimationChannel<Quaternion>.Create();
var curve = new AnimationCurve<Quaternion>(Duration.FromSeconds(1), new[]
{
    new AnimationKey<Quaternion>(Duration.Zero, Quaternion.Identity),
    new AnimationKey<Quaternion>(Duration.FromSeconds(1),
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)),
}, AnimationInterpolators.Quaternion);
var forward = Timeline()[Track<Quaternion>(channel: rotation, source: curve)];
var repeated = Timeline()[
    Repeat(count: 3)[
        Sequence()[forward, Reverse()[forward]]
    ]
].Build(); // 2 秒の往復を 3 回、合計 6 秒。

// 通常の API でも同じ演算を構成できる。
AnimationTimeline backwards = forward.Build().Reverse();
AnimationTimeline threeForwardRuns = forward.Build().Repeat(3);

var simulationClock = new ManualClock();
var playback = new AnimationPlayback(simulationClock, repeated);
var output = new AnimationOutput();
var events = new List<AnimationEventOccurrence>();
playback.Play();
simulationClock.Advance(Duration.FromSeconds(1.5));
output.Clear();
events.Clear();
playback.Update(output, events);

void ApplyBone(Action<Quaternion> applyRotation)
{
    if (output.TryGet(rotation, out var value)) applyRotation(value);
}
```

UI とボーンに別の時計を渡せば独立して進められる。設定ノード forward の共有は許容するが、Build 後の再生は可変ノードを参照しない。

### 所有権、エラー、性能

不変定義はスレッド間で共有できる。再生者、出力、イベント追記先は一更新に一つのスレッドが操作し、時計もそのスレッド規約に従う。消費側の適用先のスレッド制約は消費側が定める。異なる CPU 間の浮動小数点のビット一致は保証しない。

カーブと定義コレクションは検証してコピーする。独自 T が参照型の場合、その不変性と寿命は消費側が保証する。深いコピーや独自型の破棄は行わない。再生者と出力は通常の Managed オブジェクトであり、Dispose や Native ハンドルを要求しない。

負の長さ・時刻、0 以下の Repeat 回数、非有限の速度、範囲外の Seek は ArgumentOutOfRangeException、定義の不整合は ArgumentException、必須の null は ArgumentNullException、時間演算のオーバーフローは OverflowException とする。時計の逆行は契約違反として InvalidOperationException で拒否する。通常の引数エラーは状態・出力変更前に検出する。独自ソースの例外時はその更新の結果を消費側が適用しない。内部状態の自動ロールバックは保証しない。

キー探索は二分探索を基準とし、Output とイベントの容量は再利用する。初期化・容量拡張を除く標準数値型の定常更新の Managed 割り当てゼロを検証目標とする。Repeat は有限回の定義を展開してメモリを増やさないが、通過イベントの数は回数に比例する。独自ソースの性能と長い停止後のイベント出力量は消費側も考慮する。

Output は現在の寄与を持つスロットの一覧を保持し、Clear と内部の CopyTo はその一覧だけを走査する。過去に使ったチャネル数が多くても、これらの処理で未使用スロットを走査しない。Clear は値への参照を解放する一方、チャネルと型付きスロットの対応は再利用のため保持する。このため、チャネル対応表に加え、有効スロットの一覧と各スロットのチャネル参照を持つメモリ負担があり、Clear はそれらの記憶域を縮小しない。

周をまたぐ値の優先順位は、Repeat を展開した場合の登録順に相当する内部の番号で比較する。並列の値項目数はタイムラインの長さに比例しないため、この番号を時間の long Ticks と同じ幅に制限しない。例えば、長さ 1 Tick の値項目を 5 個並列に置き、その全体に Repeat(int.MaxValue) を二重に適用すると、長さは 4,611,686,014,132,420,609 Ticks で long に収まるが、仮想の登録数は 23,058,430,070,662,103,045 となり ulong も超える。現在の実装は UInt128 を用い、こうした有効な長さの定義を内部番号の不足で拒否しない。番号の型とビット幅は公開 API の契約に含めず、登録順と周境界での優先規則を維持する。

Windows、Linux、Browser で Managed 評価を使用し、単一スレッドでも動作できるようにする。Native、描画バックエンド、実行時リフレクション・動的コード生成を要求しない。Linux の限定的な更新性能の測定は [ADR-ANIMATION-0003 の検証記録](ANIMATION-0003-animation-execution-and-event-time.md) に記す。Windows／Browser と実時計別の評価性能は未検証である。

## 検討した代替案

### double 秒の時間差を毎更新で渡す

時計の差し替えは容易だが、時間単位と位置の区別が弱く、更新ごとの累積計算を利用側へ分散させる。旧ライブラリの単調時計と型付き時間、基準時点からの位置計算を採用する。

### 旧 AnimationTarget による適用を再利用する

再生と適用をまとめられるが、今回の消費側が適用する責務に合わない。時間管理とタイムラインの演算を参考にし、適用コールバックは Animation に保持しない。

### 状態機械まで同じ PR に含める

条件判定、トリガー型、遷移優先度、クロスフェードと独立した状態機械の再利用を同時に決める必要がある。値評価と時間制御を先に確認し、状態機械は別 PR に分離する。

### ボーン用と UI 用に再生を分ける

時間・ループ・中断・タイムラインの仕様が重複する。汎用の計算と時計を共通化し、データ構造と適用を消費側に残す。

## 結果と影響

- 旧ライブラリの時計設計を活かし、時間幅と時点を区別できる。Update に double の時間差を渡す必要がなくなる。
- Repeat／Reverse を通常 API と Lumyte.Composition の両方で構築できる。
- ボーンや UI の構造・適用と独立して計算を検証できる。
- Core 時間型と Composition 契約・Generator の参照が実装の前提になる。
- 状態機械は今回の API・実装範囲に含まれず、別 PR の設計が必要になる。
- Output の初期化・クリア、評価順、値の適用とイベント配送は消費側が制御する。
- 採用は設計方針への合意を表す。環境別の検証結果と未検証事項は実装の確認記録で区別する。

## 検証方針

以下を実装と継続検証の受け入れ条件とする。

- ManualClock で時計の停止・前進、基準時点、Pause／Resume／Seek／速度変更、同時刻の再更新と Once の完了を確認する。時計逆行と時間演算のオーバーフローも検証する。
- ゲーム時計を止めたまま UI 時計を進め、独立した結果を得られることを確認する。
- Repeat の中間周と最終終端、Reverse の始端／終端、Reverse 二重適用、Repeat／Reverse のネストを検証する。合計長と値を元の子の期待時刻と比較する。
- Repeat 内の Hold／Release と待機区間、Reverse の有効区間と退出時の最後の値を検証する。
- 0／終端／同時刻マーカー、Repeat の有限終端、Loop の複数周、Reverse のマーカー順、Seek 後を検証する。分割更新と一括更新のイベント列を比較する。
- 実際の Composition Generator を Analyzer として使用し、別アセンブリから generic Track とネストした Repeat／Reverse の利用例をコンパイルする。子が一つという制約、循環、null、ノード共有、Build 後の変更の分離を確認する。
- Composition の定義と等価な Builder・通常 API が、同じ時刻に同じ値とイベントを返すことを確認する。
- float／Vector／Quaternion／離散値、キー境界、端の保持、イージング端点、独自型を検証する。消費側の対象や setter にアクセスしないことを確認する。
- 共通の既知入力を Windows、Linux、Browser で許容誤差内で比較する。
- 100 数値チャネル・100 再生者を測定基準とし、ウォームアップ後の評価時間と割り当て量をイベントの有無別に記録する。合格時間の予算は消費側の更新予算と合わせて別途定める。

## 実装の確認記録

2026-10-08 に Linux・.NET SDK 10.0.401 で確認した。

- Debug／Release のソリューションビルドが警告・エラーともに 0 件。
- Animation の 23 件、既存 Composition の 47 件のテストが成功。時計操作、境界値、イベント順、独立した時計、定常数値更新の割り当てゼロを含む。
- Animation と Composition の実行サンプルが成功。Animation は有限 Repeat／Reverse の 4 秒終端で完了する。
- Core と Animation の NuGet パッケージ生成、整形検査、Markdown lint が成功。
- Windows／Browser の実行と、100 チャネル・100 再生者の性能測定は未実施。上記の定常更新テストは大規模性能測定を代替しない。

## 別途決定する事項

- 状態機械、型付きトリガー・Context、遷移条件・優先度と Composition 定義は、後続の [ADR-ANIMATION-0002](ANIMATION-0002-animation-state-machine.md) で決定する。クロスフェードは引き続き別途決定する。
- タイムラインの編集・保存形式、同期グループ、PingPong と高度なイージング。
- 消費側のボーン構造、ポーズ合成、ルートモーション、UI・シーン・物理への適用とイベント配送。
- アセットのインポート、保存形式・バージョニング、独自型のソースと補間。

## 参考資料

- [ADR-0001: ADR の書き方と運用](../0001-adr-writing-policy.md)
- [ADR-0002: リポジトリのフォルダ構成](../0002-repository-layout.md)
- [ADR-COMPOSITION-0001: デリゲート型ファクトリとノード操作の生成](../composition/COMPOSITION-0001-declarative-composition.md)
- [旧 Core.Time](https://github.com/ikihiki/Lumyte_old/tree/462e5b3e1e65c37ab2d1df6ba9ddde9f287690ee/src/foundation/Lumyte.Core/Time)
- [旧 PlaybackHandle の時間管理](https://github.com/ikihiki/Lumyte_old/blob/462e5b3e1e65c37ab2d1df6ba9ddde9f287690ee/src/interaction/Lumyte.Animation/PlaybackHandle.cs)
- [旧 RepeatTimeline](https://github.com/ikihiki/Lumyte_old/blob/462e5b3e1e65c37ab2d1df6ba9ddde9f287690ee/src/interaction/Lumyte.Animation/RepeatTimeline.cs)
- [旧 ReverseTimeline](https://github.com/ikihiki/Lumyte_old/blob/462e5b3e1e65c37ab2d1df6ba9ddde9f287690ee/src/interaction/Lumyte.Animation/ReverseTimeline.cs)
