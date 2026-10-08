# ADR-ANIMATION-0001: 時間による値の変化と実行タイミングを制御するアニメーション基盤

- 状態: 提案
- 日付: 2026-10-08

## 背景

Lumyte は C# を中心とするゲームエンジンで、Windows、Linux、Browser と複数の描画バックエンドを対象とする。現時点ではアニメーション、シーン、アセットの実装や契約は存在しない。描画 API に依存せず検証できるアニメーションの評価契約を先に定め、後続のエンジン統合と描画実装の境界を明確にする必要がある。

アニメーションシステムは、ボーン制御から UI の位置・サイズ・色・透明度まで、時間によって値が変わる事象と、いつどの変化を起こすかの両方を扱う。代表的な用途は、待機から歩行への切り替え、上半身の動作の重ね合わせ、パネルを表示した後のボタンのフェード、入力に応じた選択状態の変化、複数の演出の同期である。スケルタルアニメーションはこの基盤の利用領域の一つとする。既存コードの移行は発生しない。

## 決定

型付きの値評価、実行タイミングと再生状態の制御、対象への適用を分離する。共通基盤がカーブ・Tween・タイムライン・トリガーによる状態遷移を扱い、ボーンと UI はそれぞれのアダプターで評価結果を利用する。アプリケーションは入力や条件判定をトリガーとして渡し、アニメーション側が定義に従って再生する変化と時刻を決定する。Lumyte.Animation の責務は汎用の値計算と実行制御までとする。ボーン関連のデータ構造は別の消費側プロジェクトが所有し、ボーンや UI への値の適用も消費側で行う。

### 適用範囲と依存方向

実装時の配置は [ADR-0002](../0002-repository-layout.md) の分類ルールに従う `src/Animation/Lumyte.Animation/`、名前空間と NuGet パッケージ名は `Lumyte.Animation` とする。設定型の構築には既存の Lumyte.Composition を使用する。今回の変更ではプロジェクトを作成しない。

```text
入力・ゲームの条件判定 → トリガー
                           ↓
                  再生制御・タイムライン
                           ↓
                    型付きの値評価
                           ↓
                   出力バッチ・イベント
                           ↓
         消費側プロジェクト（ここから適用）
                           ↓
                 物理・レイアウト・描画
```

- `Lumyte.Animation` はボーンのデータ構造を管理するプロジェクト、シーン、ECS、UI フレームワーク、描画、OS、Native ライブラリに依存しない。基本数値型は `System.Numerics` を使用する。
- 共通基盤は値評価、遅延、順次・並列実行、ループ、一時停止、Seek、中断、トリガーによる状態遷移、イベント収集を所有する。
- Engine と UI の統合層は時計の選択、対象とチャネルの対応、評価結果の適用、イベント配送を所有する。Animation から各統合層への依存を作らない。
- ボーン関連の型、スケルトン、ポーズ、ボーンマスク、ボーン固有の合成とルートモーションは消費側プロジェクトが管理する。Animation はそれらの型や意味を認識せず、必要な時刻、重み、位置、回転などの汎用値を返す。
- このプロジェクトは対象オブジェクトへの参照、プロパティ setter、適用アダプターを保持しない。消費側が必要な型付き値を取得し、自身のデータ構造へ適用する。グラフの編集 UI・保存形式は後続判断とする。

### 共通の値評価と出力

`IAnimationSource<T>` はローカル時刻から T の値を返す純粋な評価契約とする。キーフレームカーブと Tween は同じ契約を実装する。`float`、`Vector2`、`Vector3`、`Vector4`、`Quaternion` の補間を標準提供し、色は統合層が定める色空間の Vector4 として扱う。離散値は Step 補間で切り替え、独自型は `IAnimationInterpolator<T>` で拡張する。型の異なる値を暗黙に変換しない。

Tween の始値と終値は明示的に与える。現在値から開始したい場合は、統合層が開始前に値を読み、始値を固定する。評価中に UI やシーンの値を読み取らず、同じ時刻と入力で同じ値を得られるようにする。補間とイージングは区別し、イージングで正規化時間を変換してから型ごとの補間を行う。標準イージングは `[0, 1]` を保ち、端点は厳密に 0／1 とする。

出力先は型付きの `AnimationChannel<T>` で識別する。チャネルは計算結果を区別する不透明な識別子である。対象オブジェクトやプロパティとの対応は消費側だけが保持し、Animation はその対応を参照しない。評価は `AnimationOutput` へ結果を書き、適用は別段階とする。UI の位置・透明度とボーン制御で使用する位置・回転・重みなどを同じタイムラインに配置できる。ボーン構造を表す専用型を Animation に追加する必要はない。消費側が独自型の純粋な値ソースを提供する場合も、その型の所有権と意味は消費側にある。

### 実行タイミングと再生制御

`AnimationTimeline` は開始時刻、値ソース、出力チャネル、終端時の扱いを持つ不変の項目集合とする。開始時刻によって遅延を、前項目の終了時刻への配置によって順次実行を、同じ開始時刻への配置によって並列実行を表す。ネストしたタイムラインは Build 時に時刻を合成して平坦化する。長さは最後の項目またはイベントの終了時刻で決め、全体に正の長さを要求する。

開始前の項目は値を出力しない。`Hold` は終了以降も終値を出力し、`Release` は終端到達の更新で終値を一度出力した後にチャネルを解放する。大きな時間差で開始と終了を飛び越しても終値を評価する。解放は以後その項目が出力に寄与しないことだけを意味する。消費側が値を保持するか元値へ戻すかは消費側の契約であり、Animation は復元を行わない。Seek は通過イベントや途中の項目の終了通知を発生させず、移動先で有効な値だけを再評価する。

同一タイムライン内の同じチャネルに複数の項目が寄与する場合は、後から登録した項目を優先する。複数の再生者の出力を一つの AnimationOutput に集める場合も、更新順で後の寄与を優先する。更新順は統合層が固定する。ブレンドが必要な値は明示的なブレンドソースで一つの値へ合成してから出力し、UI に意図しない加算を適用しない。

`AnimationController` は状態名とタイムライン・ループ設定、現在状態とトリガー名から次状態への遷移を保持する。条件判定は入力・ゲーム側で行い、Trigger を送る。制御層は次の Update の開始時にキュー順でトリガーを処理し、遷移先の再生を時刻 0 から開始する。対応する遷移がないトリガーは消費して無視し、同じ状態・トリガーの重複登録は拒否する。トリガー列は有限とし、イベントからの新しいトリガーは配送後の次更新で処理する。評価中に再帰的に遷移しない。

中断は現在の再生を Cancel して新しい再生を開始する。中断時に消費側の値を変更しない。新しい変化の始値が必要なら消費側がその値を明示的に取得する。クロスフェードの時間依存の重みは汎用カーブで計算でき、ポーズの合成はボーンを所有するプロジェクトで行う。汎用コントローラーの状態遷移は即時切り替えとし、滑らかな値の変化は明示した Tween で構成し、結果の適用は消費側が行う。

### 共通の公開 API

以下は追加する公開 API の設計差分であり、実装本体を省略した C# 宣言を示す。すべて新規 API のため追加行として記載する。補間器、値ソース、タイムラインは不変とし、独自実装にも純粋な評価を要求する。

```diff
+using System.Collections.Generic;
+using System.Numerics;
+
+namespace Lumyte.Animation;
+
+public enum AnimationWrapMode { Once, Loop }
+public enum AnimationFillMode { Hold, Release }
+public enum AnimationPlaybackState { Stopped, Playing, Paused, Completed, Cancelled }
+public enum AnimationEasing { Linear, EaseIn, EaseOut, EaseInOut }
+
+// 正の Duration と [0, Duration] の純粋な値評価。対象への副作用を持たない。
+public interface IAnimationSource<T>
+{
+    double Duration { get; }
+    T Sample(double time);
+}
+
+public readonly record struct AnimationKey<T>(double Time, T Value);
+
+public interface IAnimationInterpolator<T>
+{
+    T Interpolate(T from, T to, float amount);
+}
+
+// 標準数値型の補間。回転は最短経路の Slerp、離散値は Step を使う。
+public static class AnimationInterpolators
+{
+    public static IAnimationInterpolator<float> Float { get; }
+    public static IAnimationInterpolator<Vector2> Vector2 { get; }
+    public static IAnimationInterpolator<Vector3> Vector3 { get; }
+    public static IAnimationInterpolator<Vector4> Vector4 { get; }
+    public static IAnimationInterpolator<Quaternion> Quaternion { get; }
+    public static IAnimationInterpolator<T> Step<T>();
+}
+
+public sealed class AnimationCurve<T> : IAnimationSource<T>
+{
+    // キーを検証・コピーする。最低一つのキー、厳密な時刻昇順を要求する。
+    public AnimationCurve(double duration, IReadOnlyList<AnimationKey<T>> keys,
+        IAnimationInterpolator<T> interpolator);
+    public double Duration { get; }
+    public T Sample(double time);
+}
+
+public sealed class Tween<T> : IAnimationSource<T>
+{
+    public Tween(T from, T to, double duration,
+        IAnimationInterpolator<T> interpolator, AnimationEasing easing);
+    public double Duration { get; }
+    public T Sample(double time);
+}
+
+// 対象への参照や setter を保持しない、型付きの不透明な識別子。
+public sealed class AnimationChannel<T>
+{
+    private AnimationChannel();
+    public static AnimationChannel<T> Create();
+}
+
+public sealed class AnimationTimeline
+{
+    // Builder のみが生成する不変定義。
+    internal AnimationTimeline();
+    public double Duration { get; }
+}
+
+public sealed class AnimationTimelineBuilder
+{
+    public AnimationTimelineBuilder();
+    public void Add<T>(double start, IAnimationSource<T> source,
+        AnimationChannel<T> channel, AnimationFillMode fill = AnimationFillMode.Hold);
+    public void AddTimeline(double start, AnimationTimeline timeline);
+    public void AddEvent(double time, string name, string? payload = null);
+    // 登録順を保持し、定義を検証・コピーする。全体に正の長さを要求する。
+    public AnimationTimeline Build();
+}
+
+public sealed class AnimationOutput
+{
+    public AnimationOutput();
+    public void Clear();
+    // 値を取得するだけで対象へ適用しない。未出力は false。
+    public bool TryGet<T>(AnimationChannel<T> channel, out T value);
+}
+
+public readonly record struct AnimationEvent(double Time, string Name, string? Payload);
+public readonly record struct AnimationEventOccurrence(
+    AnimationEvent Event, long LoopIndex, double UpdateOffsetSeconds);
+
+public sealed class AnimationPlayback
+{
+    public AnimationPlayback(AnimationTimeline timeline,
+        AnimationWrapMode wrapMode = AnimationWrapMode.Once);
+    public AnimationPlaybackState State { get; }
+    public double Time { get; }
+    // 有限かつ 0 以上。既定は 1。
+    public double Speed { get; set; }
+    public void Play();
+    public void Pause();
+    public void Stop();
+    public void Seek(double time);
+    public void Cancel();
+    // 結果・通過イベントを追記。この更新で完了したときだけ true。
+    public bool Update(double deltaSeconds, AnimationOutput output,
+        ICollection<AnimationEventOccurrence> events);
+}
+
+public sealed record AnimationState(AnimationTimeline Timeline, AnimationWrapMode WrapMode);
+public sealed record AnimationTransition(string From, string Trigger, string To);
+
+public sealed class AnimationController
+{
+    // 定義を検証・コピーし、初期状態の再生を開始する。
+    public AnimationController(string initialState,
+        IReadOnlyDictionary<string, AnimationState> states,
+        IReadOnlyList<AnimationTransition> transitions);
+    public string CurrentState { get; }
+    public void Trigger(string trigger);
+    // キュー順で遷移を処理してから現在状態を評価する。
+    public void Update(double deltaSeconds, AnimationOutput output,
+        ICollection<AnimationEventOccurrence> events);
+}
```

カーブは端のキーの値を保持する。Tween は明示した始値と終値を使用する。チャネルの対象との対応は消費側が保持する。Build 後の Builder の変更は生成済み定義に影響しない。Playback は初期状態 Stopped、時刻 0 とし、Controller は初期状態の Playback を Playing にする。

ビルダーの不正時刻・非有限値は ArgumentOutOfRangeException、未登録状態・重複遷移・長さ 0 の定義は ArgumentException、必須の null は ArgumentNullException とし、Build／Controller 作成時に検出する。Seek は `[0, timeline.Duration]` を要求する。

出力は再利用可能な型付き格納領域とし、object への毎更新のボックス化を避ける実装を目標とする。出力は一更新に一スレッドが操作し、読み取りと対象への適用は評価完了後に行う。独自ソースの例外時はその更新の出力を適用せず、統合層がエラーを扱う。更新前の内部状態への自動ロールバックは保証しない。

### 利用例と更新順序

以下は設計 API の利用例であり、現時点で実装・実行済みのサンプルではない。型名・using・呼び出しは上記の公開 API に対応する。

#### 値を単独で計算する

タイムラインや適用先を作らず、指定した時刻の値だけを取得できる。

```csharp
using Lumyte.Animation;

var opacity = new Tween<float>(
    0f, 1f, 0.2, AnimationInterpolators.Float, AnimationEasing.Linear);
float halfway = opacity.Sample(0.1); // 0.5。UI への適用は行わない。
```

#### UI 用のタイムラインと状態機械

位置と透明度を並列に変化させ、その後ボタンをフェードさせる。Closed と Opening をトリガーで切り替える。各状態で同じチャネルを使い、対象との対応は消費側が保持する。

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using Lumyte.Animation;

var position = AnimationChannel<Vector2>.Create();
var opacity = AnimationChannel<float>.Create();
var buttonOpacity = AnimationChannel<float>.Create();

var openingBuilder = new AnimationTimelineBuilder();
openingBuilder.Add(0, new Tween<Vector2>(
    new Vector2(0, -40), Vector2.Zero, 0.2,
    AnimationInterpolators.Vector2, AnimationEasing.EaseOut), position);
openingBuilder.Add(0, new Tween<float>(
    0f, 1f, 0.2, AnimationInterpolators.Float, AnimationEasing.Linear), opacity);
openingBuilder.Add(0.2, new Tween<float>(
    0f, 1f, 0.1, AnimationInterpolators.Float, AnimationEasing.Linear), buttonOpacity);
openingBuilder.AddEvent(0.3, "PanelOpened");

var closedBuilder = new AnimationTimelineBuilder();
closedBuilder.Add(0, new Tween<Vector2>(
    new Vector2(0, -40), new Vector2(0, -40), 0.1,
    AnimationInterpolators.Vector2, AnimationEasing.Linear), position);
closedBuilder.Add(0, new Tween<float>(
    0f, 0f, 0.1, AnimationInterpolators.Float, AnimationEasing.Linear), opacity);
closedBuilder.Add(0, new Tween<float>(
    0f, 0f, 0.1, AnimationInterpolators.Float, AnimationEasing.Linear), buttonOpacity);

var controller = new AnimationController(
    "Closed",
    new Dictionary<string, AnimationState>
    {
        ["Closed"] = new(closedBuilder.Build(), AnimationWrapMode.Once),
        ["Opening"] = new(openingBuilder.Build(), AnimationWrapMode.Once),
    },
    new[]
    {
        new AnimationTransition("Closed", "Open", "Opening"),
        new AnimationTransition("Opening", "Close", "Closed"),
    });
var output = new AnimationOutput();
var events = new List<AnimationEventOccurrence>();
controller.Trigger("Open");

// 消費側の更新関数。適用用 Action は消費側だけが保持する。
void UpdateUi(double uiDeltaSeconds, Action<Vector2> setPosition,
    Action<float> setOpacity, Action<float> setButtonOpacity,
    Action<AnimationEventOccurrence> dispatch)
{
    output.Clear();
    events.Clear();
    controller.Update(uiDeltaSeconds, output, events);

    if (output.TryGet(position, out var p)) setPosition(p);
    if (output.TryGet(opacity, out var a)) setOpacity(a);
    if (output.TryGet(buttonOpacity, out var b)) setButtonOpacity(b);
    foreach (var occurrence in events) dispatch(occurrence);
}
```

消費側が各フレームで UpdateUi に UI 時計の時間差を渡す。Close は即時に Closed へ切り替える例とする。途中の現在値から滑らかに閉じる場合は消費側が現在値を読み、明示的な始値で閉じる Tween を作る。

#### ボーンの消費側で結果を適用する

汎用カーブから回転を計算し、ボーンを所有するプロジェクトへ渡す。Animation の公開 API にボーン型や setter は追加しない。

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using Lumyte.Animation;

var rotation = AnimationChannel<Quaternion>.Create();
var curve = new AnimationCurve<Quaternion>(1.0, new[]
{
    new AnimationKey<Quaternion>(0, Quaternion.Identity),
    new AnimationKey<Quaternion>(0.5,
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4)),
    new AnimationKey<Quaternion>(1.0, Quaternion.Identity),
}, AnimationInterpolators.Quaternion);
var builder = new AnimationTimelineBuilder();
builder.Add(0, curve, rotation);
var playback = new AnimationPlayback(builder.Build(), AnimationWrapMode.Loop);
var output = new AnimationOutput();
var events = new List<AnimationEventOccurrence>();
playback.Play();

// ボーンプロジェクトが所有する適用処理を、この消費側関数へ渡す。
void UpdateBone(double simulationDeltaSeconds, Action<Quaternion> applyRotation)
{
    output.Clear();
    events.Clear();
    playback.Update(simulationDeltaSeconds, output, events);
    if (output.TryGet(rotation, out var value)) applyRotation(value);
}
```

利用全体の更新順序は、入力と条件判定、トリガー処理、時計の前進、値評価と競合解決、消費側による値取得・適用、消費側によるイベント配送とする。Animation の Update は計算結果とイベントを出力した時点で終了し、適用や配送を呼び出さない。

### Lumyte.Composition による定義の構築

[ADR-COMPOSITION-0001](../composition/COMPOSITION-0001-declarative-composition.md) のファクトリ、子要素インデクサ、名前付きスロットを使い、タイムラインと状態機械の設定を構築できるようにする。設定型は `Lumyte.Animation` に配置し、既存の `Lumyte.Composition` の Composable／ComposeParameter／ComposeContent／ComposeSlot を使用する。今回の変更では実装を追加しない。

依存方向は `Lumyte.Animation → Lumyte.Composition` とする。既存の `Lumyte.Composition.Generators` を Lumyte.Animation のビルド時に Analyzer として参照し、生成されたファクトリとインデクサーを公開する。利用側は Lumyte.Animation の生成済み API を使用し、実行時の Generator 参照を要求しない。Builder／コンストラクター経路も同じ実行定義を作る入口として維持する。

Composition は可変の設定ノードだけを組み立てる。Timeline.Build と StateMachine.Build はノードを検証し、既存の AnimationTimelineBuilder／AnimationController の契約に変換する。Build は再生を更新せず、対象への値適用やイベント配送も行わない。StateMachine.Build の戻り値は既存契約どおり初期状態の再生を開始済みのコントローラーであり、時刻は 0 のままとする。

| 設定ノード | 時刻への変換 |
| --- | --- |
| Timeline / Parallel | 子を同じ開始時刻へ配置し、長さを子の最大終了時刻とする |
| Sequence | 登録順に配置し、前の子の長さだけ次の開始時刻を進める |
| Delay | 有限かつ 0 以上の秒数だけ長さを持ち、値を出力しない |
| Track&lt;T&gt; | 指定した値ソースを型付きチャネルへ配置し、ソースの Duration を長さとする |
| Marker | その配置時刻へイベントを登録する。自身の長さは 0 |
| State | 子を Timeline と同じ並列規則で構築し、名前とループ設定を状態に対応付ける |
| Transition | 状態名・トリガー名から既存の AnimationTransition を構築する |

ネストは深さ優先・子の登録順で展開する。算出時刻のオーバーフロー・非有限値、参照の循環、null の子、名前の重複、参照先のない遷移は Build 時に拒否する。同じノードを複数の場所で再利用する場合は各配置として展開し、共有参照自体は循環としない。子のないグループは長さ 0 とし、最終タイムラインや各状態の長さには正の値を要求する。Delay だけで終わる定義も長さを保持するよう、Builder に終端指定を追加する。

構築用ノードのコレクションは Composition の既存契約に従って参照を保持できる。Build は構造・値・登録順を読み取ってコピーし、再生中にノードを参照しない。Build 後のノード変更は構築済みの定義へ影響せず、再 Build だけに反映される。値ソースは不変の契約に基づいて共有する。構築・Build 中の同時変更は禁止する。

#### 設定ノードの追加 API

以下も実装本体を省略した宣言差分とする。required メンバーには ComposeParameter を付け、生成ファクトリにも必須引数として公開する。通常の子要素は abstract なクラスを基底型とし、Composition の子要素とスロットの混在に対応する。

```diff
+// Lumyte.Animation に追加。指定した時刻まで値を出さずに長さだけ延長する。
+public sealed class AnimationTimelineBuilder
+{
+    public void SetDuration(double duration);
+}
```

SetDuration は有限かつ正の長さを指定し、項目またはイベントの最大終了時刻より短い値は Build 時に拒否する。未指定は既存の最大終了時刻の計算を使う。

```diff
+using System;
+using System.Collections.Generic;
+using Lumyte.Animation;
+using Lumyte.Composition;
+
+namespace Lumyte.Animation;
+
+public static partial class ComposeAnimation
+{
+    public static partial class Definitions
+    {
+        public abstract class TimelineItem { }
+
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class Timeline : TimelineItem
+        {
+            [ComposeContent]
+            public IReadOnlyList<TimelineItem> Children { get; set; } = [];
+            public AnimationTimeline Build();
+        }
+
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class Sequence : TimelineItem
+        {
+            [ComposeContent]
+            public IReadOnlyList<TimelineItem> Children { get; set; } = [];
+        }
+
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class Parallel : TimelineItem
+        {
+            [ComposeContent]
+            public IReadOnlyList<TimelineItem> Children { get; set; } = [];
+        }
+
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class Delay : TimelineItem
+        {
+            [ComposeParameter]
+            public required double Duration { get; init; }
+        }
+
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
+
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class Marker : TimelineItem
+        {
+            [ComposeParameter]
+            public required string Name { get; init; }
+            [ComposeParameter]
+            public string? Payload { get; init; }
+        }
+
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class State
+        {
+            [ComposeParameter]
+            public required string Name { get; init; }
+            [ComposeParameter]
+            public AnimationWrapMode WrapMode { get; init; } = AnimationWrapMode.Once;
+            [ComposeContent]
+            public IReadOnlyList<TimelineItem> Children { get; set; } = [];
+        }
+
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class Transition
+        {
+            [ComposeParameter]
+            public required string From { get; init; }
+            [ComposeParameter]
+            public required string Trigger { get; init; }
+            [ComposeParameter]
+            public required string To { get; init; }
+        }
+
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class StateMachine
+        {
+            [ComposeParameter]
+            public required string InitialState { get; init; }
+            [ComposeContent]
+            public IReadOnlyList<State> States { get; set; } = [];
+            public IReadOnlyList<Transition> TransitionItems { get; private set; } = [];
+            [ComposeSlot]
+            private static void Transitions(StateMachine target,
+                IReadOnlyList<Transition> children);
+            public AnimationController Build();
+        }
+    }
+}
```

Transitions の宣言本体は受け取った子を TransitionItems に保存する。名前付きスロットはその設定を遅延して適用するだけで、再生を開始しない。StateMachine の通常の子要素を状態、Transitions スロットの子要素を遷移として型で区別する。Timeline／Sequence／Parallel の子として異なる T の Track を混在できるが、各 Track の Channel と Source の T は一致させる。

#### 生成される入口

主な入口の差分は以下とする。State、Delay、Marker、Transition の専用デリゲートも同じ既存規則で生成し、引数順は required 群のメンバー名順とする。例では名前付き引数を使う。

```diff
+public static partial class ComposeAnimation
+{
+    public static Definitions.TimelineFactory Timeline { get; }
+    public static Definitions.SequenceFactory Sequence { get; }
+    public static Definitions.ParallelFactory Parallel { get; }
+    public static Definitions.DelayFactory Delay { get; }
+    public static Definitions.MarkerFactory Marker { get; }
+    public static Definitions.StateFactory State { get; }
+    public static Definitions.TransitionFactory Transition { get; }
+    public static Definitions.StateMachineFactory StateMachine { get; }
+    public static Definitions.Track<T> Track<T>(AnimationChannel<T> channel,
+        IAnimationSource<T> source, Optional<AnimationFillMode> fill = default,
+        IReadOnlyList<Action<Definitions.Track<T>>>? with = null);
+    public static Definitions.TrackFactory<T> TrackFactory<T>();
+
+    public static partial class Definitions
+    {
+        public delegate Timeline TimelineFactory(IReadOnlyList<Action<Timeline>>? with = null);
+        public delegate Sequence SequenceFactory(IReadOnlyList<Action<Sequence>>? with = null);
+        public delegate Parallel ParallelFactory(IReadOnlyList<Action<Parallel>>? with = null);
+        public delegate Delay DelayFactory(double duration, IReadOnlyList<Action<Delay>>? with = null);
+        public delegate Marker MarkerFactory(string name, Optional<string?> payload = default,
+            IReadOnlyList<Action<Marker>>? with = null);
+        public delegate State StateFactory(string name, Optional<AnimationWrapMode> wrapMode = default,
+            IReadOnlyList<Action<State>>? with = null);
+        public delegate Transition TransitionFactory(string from, string to, string trigger,
+            IReadOnlyList<Action<Transition>>? with = null);
+        public delegate StateMachine StateMachineFactory(string initialState,
+            IReadOnlyList<Action<StateMachine>>? with = null);
+        public delegate Track<T> TrackFactory<T>(AnimationChannel<T> channel,
+            IAnimationSource<T> source, Optional<AnimationFillMode> fill = default,
+            IReadOnlyList<Action<Track<T>>>? with = null);
+
+        // 各 partial 型内に生成するインデクサー。
+        public partial class Timeline
+        {
+            public Timeline this[params TimelineItem[] content] { get; }
+        }
+        public partial class Sequence
+        {
+            public Sequence this[params TimelineItem[] content] { get; }
+        }
+        public partial class Parallel
+        {
+            public Parallel this[params TimelineItem[] content] { get; }
+        }
+        public partial class State
+        {
+            public State this[params TimelineItem[] content] { get; }
+        }
+        public partial class StateMachine
+        {
+            public readonly struct CompositionChild
+            {
+                public static implicit operator CompositionChild(State state);
+                public static implicit operator CompositionChild(
+                    CompositionSlotAssignment<StateMachine> slot);
+            }
+            public StateMachine this[params CompositionChild[] content] { get; }
+        }
+    }
+}
+
+public static class ComposeAnimationStateMachineCompositionExtensions
+{
+    public static CompositionSlot<ComposeAnimation.Definitions.StateMachine,
+        ComposeAnimation.Definitions.Transition> Transitions(
+        this ComposeAnimation.Definitions.StateMachineFactory factory);
+}
```

ファクトリ名は各 Composable 属性の Factory で明示し、既定の Compose と区別する。StateMachine.CompositionChild には State と CompositionSlotAssignment&lt;StateMachine&gt; からの暗黙変換を既存規則で生成する。生成 API の Optional、with、スロット、配列参照、置換の契約は Composition ADR に従い、Animation 独自の構文や Generator の拡張は追加しない。

#### ネストしたタイムラインと状態機械の例

Sequence と Parallel をネストし、開始時刻を手計算せずに UI の設定を記述する。マーカーは最後のフェードが終わった時刻に配置される。例は未実装 API の利用設計である。

```csharp
using Lumyte.Animation;
using static Lumyte.Animation.ComposeAnimation;

var opacity = AnimationChannel<float>.Create();
var buttonOpacity = AnimationChannel<float>.Create();
var fadeIn = new Tween<float>(
    0f, 1f, 0.2, AnimationInterpolators.Float, AnimationEasing.Linear);
var fadeButton = new Tween<float>(
    0f, 1f, 0.1, AnimationInterpolators.Float, AnimationEasing.Linear);
var hidden = new Tween<float>(
    0f, 0f, 0.1, AnimationInterpolators.Float, AnimationEasing.Linear);

var opening = Timeline()[
    Sequence()[
        Parallel()[
            Track<float>(channel: opacity, source: fadeIn),
            Sequence()[
                Delay(duration: 0.2),
                Track<float>(channel: buttonOpacity, source: fadeButton)
            ]
        ],
        Marker(name: "PanelOpened")
    ]
];
AnimationTimeline timeline = opening.Build(); // 長さ 0.3 秒。

var definition = StateMachine(initialState: "Closed")[
    State(name: "Closed")[
        Track<float>(channel: opacity, source: hidden),
        Track<float>(channel: buttonOpacity, source: hidden)
    ],
    State(name: "Opening")[opening],
    StateMachine.Transitions()[
        Transition(from: "Closed", trigger: "Open", to: "Opening"),
        Transition(from: "Opening", trigger: "Close", to: "Closed")
    ]
];
AnimationController controller = definition.Build();
controller.Trigger("Open");

var output = new AnimationOutput();
var events = new System.Collections.Generic.List<AnimationEventOccurrence>();
output.Clear();
events.Clear();
controller.Update(0.25, output, events);
output.TryGet(opacity, out var panelValue);       // 1。
output.TryGet(buttonOpacity, out var buttonValue); // 約 0.5。
// 消費側が panelValue / buttonValue を対象へ適用する。
```

設定ノード opening を単独タイムラインと状態内の両方で使える。Build 済みの定義は各々独立しており、実行中に設定ノードを参照しない。値の適用は先の UI 利用例と同じ消費側の更新関数で行う。

### 共通の時間、状態、イベント

- 時計を内部で取得せず、利用側が秒単位の `deltaSeconds >= 0` を渡す。ゲームとボーンはシミュレーション時計、UI はゲームの一時停止に影響されない時計など、統合層が明示的に時計を選ぶ。同じ再生者を複数の更新ループから進めない。描画だけの再評価では時間を進めない。Browser の停止復帰などによる大きな時間差の制限は Engine の方針とする。
- `Play` は Playing／Paused では現在位置を保ち、Cancelled では 0 から再開する。`Pause` は Playing のみ変更する。`Stop` はどの状態からも停止して時刻を 0 に戻す。
- Update は Stopped／Cancelled では出力せず、Paused では停止位置の有効項目、Completed では Hold 項目だけを再評価する。時間が進まない更新ではイベントを返さない。`deltaSeconds == 0` は現在位置の値だけを評価する。Once は終端に達した更新だけで完了を通知する。完了後の Play は 0 から再開する。
- Loop は非負の非折り返し累積時間を保持し、サンプル時刻を `[0, Duration)` に折り返す。イベントは折り返し前の区間を使って収集する。Loop の Seek(Duration) は次周の 0 と同等に扱う。
- イベントは通過区間 `(前回時刻, 今回時刻]` で配送する。時刻 0 のイベントは停止からの初回の正の前進と各周の開始で一度ずつ配送する。Seek 後の始点イベントは配送しない。
- ループ境界では前周の Duration のイベント、その後に次周の 0 のイベントを配送する。同一時刻では格納順を保つ。複数周を進む Update も通過した全イベントを返し、暗黙の省略をしない。長時間差では出力量が増えるため、利用側が時間差を制限する。
- 評価中にゲームコードのコールバックを呼ばない。呼び出し側が渡した追記先は再入しないコレクションとし、消費側が評価終了後に配送する。複数の再生を並行して評価するとき、消費側が配送対象を明示的に選ぶ。

### スレッド、環境、性能

不変アセットと純粋な値ソースはスレッド間で共有できる。Controller、Playback、AnimationOutput は一更新に一つのスレッドだけが操作する。適用先のスレッド制約は消費側が定める。UI の適用はそのフレームワークが要求するスレッドで実行する。イベント追記先も一更新中に一つのスレッドだけが操作する。異なるキャラクターや UI の独立した再生を並列評価できるが、実行スケジューラーは Engine の責務であり Browser に並列実行を要求しない。同一入力と更新列の再現性を目指す一方、異なる CPU・ランタイム間の浮動小数点のビット一致は保証しない。

| 環境 | 評価方式 | 制約 |
| --- | --- | --- |
| Windows / Linux | Managed CPU 評価 | DirectX／Vulkan の利用可否に依存しない |
| Browser | 同じ Managed CPU 評価 | 単一スレッドで動作でき、Native と動的コード生成を要求しない |

キーの探索は二分探索を基準とし、不変の値ソースとタイムラインを再生者ごとにコピーしない。出力とイベントの容量は再利用し、内部の一時領域も再利用する。初期化・容量拡張を除く標準数値型の定常更新の Managed 割り当てゼロを検証目標とする。独自型の評価・格納コストは消費側が測定する。性能値は未測定である。

### データの所有権とエラー契約

カーブはキーを、タイムラインと状態機械は定義のコレクションをコピーして検証する。値ソースと補間器は不変の契約に基づいて共有する。T が参照型の場合、その不変性と寿命は型を提供する消費側が保証する。Animation は独自型の深いコピーや破棄を行わない。

再生者、コントローラー、出力は呼び出し側が所有する Managed オブジェクトとし、Native ハンドルや Dispose を要求しない。標準補間では有限値を要求し、Quaternion は正規化された非ゼロの値を使用して最短経路の Slerp と正規化を行う。独自型の妥当性は独自のソース・補間器が検証する。

負の時間差、NaN、無限大、範囲外の時刻は ArgumentOutOfRangeException、定義の不整合は ArgumentException、必須の null は ArgumentNullException とする。通常の引数エラーは再生状態や出力の変更前に検出する。対象の破棄、対象への適用失敗、ボーンの整合性は消費側が扱う。

## 検討した代替案

### 描画バックエンド内でクリップを評価する

GPU と近い位置で処理できるが、バックエンドごとに再生・イベントの契約が重複する。描画しないサーバーや単体テストにも使える CPU 評価を先に定める。GPU スキニング自体はこの判断で禁止しない。

### シーンのコンポーネントだけを公開する

利用方法は簡単になるが、未決定の ECS やシーンに評価処理まで依存する。再利用できる評価ライブラリと Engine の統合層を分離する。

### ボーン評価と UI Tween を別の再生システムにする

用途固有の最適化はしやすいが、時間・ループ・イベント・中断・順次実行の仕様が重複し、複数領域の演出を同期しにくい。値の型と適用は分け、時間と実行制御を共通化する。

### いつ何を再生するかをすべて利用側へ任せる

評価ライブラリを小さくできるが、遅延、順次・並列実行、トリガーによる切り替えを利用側が毎回実装する必要がある。タイムラインと基本コントローラーを今回の責務とし、編集・保存形式や高度な条件グラフだけを後続判断とする。

### Native の既存アニメーションライブラリを採用する

圧縮や SIMD 最適化の利点があるが、Browser 対応、配布、相互運用、所有権の契約が必要になる。初期段階では Managed 実装の検証容易性を優先し、測定で必要性を確認してから再検討する。

## 結果と影響

- ボーン、UI、その他の型付きプロパティ変化を同じ時間・再生制御で扱い、複数領域の順序と同期を定義できる。
- 値評価と実行制御を描画なしで検証でき、同じ定義を複数対象と環境で共有できる。
- ネストしたタイムラインと状態機械を Composition の式で構築でき、再生時には Composition の可変ノードを参照しない。
- Lumyte.Animation が Lumyte.Composition の契約を参照し、既存 Generator による設定 API の生成と互換性確認が必要になる。
- シーンと描画の契約が未確定でも評価ライブラリの設計を進められる。
- アニメーション側が実行タイミングと遷移を制御し、ゲーム・UI 側が条件判定、時計、値の適用と副作用を制御できる。
- チャネルと対象の対応、出力の適用、ボーン固有の構造と合成、イベント配送は消費側が実装する必要がある。
- 汎用値と時間制御に責務を限定することでボーン・UI の構造変更から独立できるが、多数のチャネルや複雑なタイムラインの性能は別途測定が必要になる。
- 本 ADR は設計提案であり、実装・性能・Browser 動作を検証済みとするものではない。

## 検証方針

採用後の実装では以下を受け入れ条件とする。

- 実際の Composition Generator を Analyzer として使用し、設定ノードと別アセンブリの利用例をコンパイルする。generic Track、通常の子と Transitions スロットの混在、required 引数、Build を確認する。
- ネストした Sequence／Parallel／Delay／Marker が Builder の等価な定義と同じ時刻・値・イベント順序を返すことを確認する。遅延だけの末尾と明示した長さも検証する。
- 循環、null の子、重複状態、未登録遷移、同一ノードの複数配置を検証する。Build 後の子配列や設定変更が既存の再生へ影響しないことを確認する。

- float／Vector／Quaternion／離散値のカーブと Tween、キー境界、端の保持、イージング端点、独自型の補間を検証する。
- 位置・透明度・回転・重みを同じタイムラインへ配置し、順次・並列・ネストの開始／終了順序を検証する。検証にボーンや UI のプロジェクトを参照しない。
- 同一チャネルの優先順、Hold／Release、開始終了を飛び越す更新、Cancel と再開始、Seek が中間イベントを配送しないことを確認する。
- Open／Close／Walk トリガー、未登録遷移、同一更新の複数トリガー、イベント配送からの翌更新の遷移を検証する。
- 異なる deltaSeconds を与えた二つの再生者を使い、一方を停止したまま他方だけを進められることを確認する。
- Once の終端、Pause／Play／Stop／Seek、速度 0、0／Duration／同時刻イベントと複数周を検証する。分割更新と一括更新でイベントの順序と重複・欠落を比較する。
- 不正時刻・非有限値・重複遷移・未登録状態などの通常の引数エラーで部分更新を起こさないことを確認する。
- 値評価と Update が消費側の対象や setter にアクセスせず、取得した結果の適用を消費側が選べることを確認する。
- 共通の既知入力を Windows、Linux、Browser で許容誤差内で比較する。
- 1 再生者あたり 100 数値チャネル・100 再生者を測定用の基準として、ウォームアップ後の評価時間と割り当て量を記録する。イベントの有無を分け、ハードウェアとランタイムを併記する。合格時間の予算は消費側の更新予算と合わせて後続で定める。

## 別途決定する事項

- 状態機械・タイムラインの編集と保存形式、高度な条件グラフと同期グループ。
- 消費側プロジェクトのボーン関連データ構造、ポーズ合成、ルートモーションと値適用の契約。本 ADR ではプロジェクト名や公開型を定めない。
- 各 UI フレームワーク、シーン、物理との値適用・レイアウト統合とイベント配送。
- 独自型のソース・補間器、アセットのインポート・保存形式・バージョニング。

## 参考資料

- [ADR-0001: ADR の書き方と運用](../0001-adr-writing-policy.md)
- [ADR-0002: リポジトリのフォルダ構成](../0002-repository-layout.md)
- [ADR-COMPOSITION-0001: デリゲート型ファクトリとノード操作の生成](../composition/COMPOSITION-0001-declarative-composition.md)
