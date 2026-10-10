# ADR-ANIMATION-0004: フォーマットに依存しない補間と値ソースの合成

- 状態: 採用
- 日付: 2026-10-09

## 背景

Lottie の属性カーブは区間ごとの Hold と時間ベジェ、位置の空間ベジェを必要とする。glTF の CUBICSPLINE は値と入出力接線を持つ。Rive や Spine では複数の値のブレンドも必要になる。既存の AnimationCurve は全区間で同じ補間器を使い、Tween のイージングも固定列挙であるため、これらの値計算を利用側が繰り返し実装する必要がある。

読み込みと値の反映は別責務とする。今回の判断は、形式を解釈した後に利用できる、型付きの純粋な値計算の拡張である。形式の完全な再生対応を表明するものではない。

## 決定

既存の時計、Duration、タイムライン、Repeat／Reverse、Seek、マーカー、状態機械と型付き Output を利用し、以下を追加する。

- AnimationKey の出発キーに Hold、区間補間器、時間イージングを設定できるようにする。既存の二引数コンストラクターと二要素の分解は保持する。
- 時間の三次ベジェは x(t) を逆算して y(t) を求める。x 制御点は [0, 1]、y は有限値とし、オーバーシュートを許す。単なる y(進捗) の評価とは区別する。
- 空間ベジェは区間補間器として提供する。任意の T に既存の補間器を適用する。Vector2／Vector3 では位置のベジェ、独自型では利用側の補間規則を使用できる。
- 接線付き Hermite カーブを追加する。接線の単位は値／秒であり、各区間の秒数を掛ける。float、Vector2／3／4、Quaternion の実装を提供する。Quaternion は成分の三次補間後に正規化する。接線は単位 Quaternion を要求せず、ゼロも許す。
- 時間再マッピングは `IAnimationSource<Duration>` が返す子の局所時間で値ソースを評価する。逆行・停止・速度変化を表現できる。値ソースのイベントは持たないため、非単調な写像でのイベント配送規則を追加する必要はない。
- 二つの値ソースを時間依存の [0, 1] の重みでブレンドする。Output の後評価優先規則を変更せず、一つの値ソースの内部で合成する。複数入力はネストできるが、Quaternion 等では合成順に意味がある。
- Source 定義、Curve、HermiteCurve、Sampled、TimeRemap、Blend と SourceTrack を Lumyte.Composition の生成対象にする。Timeline の Build が子ソースを内部で確定し、利用側で子ごとの Build を呼ぶ必要をなくす。

レンダラー、画像・パス・ボーンのデータ構造、IK、スキン、マスク、描画順、フォーマットのローダー、Rive の状態機械との API 互換、状態遷移のクロスフェードはこの値ソース拡張の対象に含めない。図形やメッシュの変形データは利用側の T と補間器で表現できる。

## 公開 API の差分

比較元: main `f3f4f670d48ffbd9f663f9ec737e462a280cd5ca`。名前空間は Lumyte.Animation。既存 API の削除はない。

```diff
 public readonly record struct AnimationKey<T>(Duration Time, T Value)
 {
+    // 次のキーまで出発値を保持。正確な次キー時刻ではそのキーの値を返す。
+    public bool Hold { get; init; }
+    // null はカーブ全体の補間器を使用。
+    public IAnimationInterpolator<T>? Interpolator { get; init; }
+    // null は線形の時間進捗。Hold では使用しない。
+    public IAnimationTiming? Timing { get; init; }
 }
+
+public interface IAnimationTiming
+{
+    // 入力は有限な [0, 1]。出力は有限値で、範囲外のオーバーシュートを許す。
+    double Transform(double amount);
+}
+public static class AnimationTimings
+{
+    public static IAnimationTiming Linear { get; }
+    public static IAnimationTiming EaseIn { get; }
+    public static IAnimationTiming EaseOut { get; }
+    public static IAnimationTiming EaseInOut { get; }
+    // x1/x2 は [0, 1]、すべて有限値。制御点の y は範囲外も許す。
+    public static IAnimationTiming CubicBezier(double x1, double y1, double x2, double y2);
+}
 public static class AnimationInterpolators
 {
+    // 型付き時間の線形補間。境界は元の Ticks、内点は最寄りの Tick へ偶数丸め。
+    public static IAnimationInterpolator<Duration> Duration { get; }
+    // De Casteljau 法。制御値を保持するため、可変参照型は利用側で不変にする。
+    public static IAnimationInterpolator<T> CubicBezier<T>(T control1, T control2, IAnimationInterpolator<T> interpolator);
 }
+
+public readonly record struct AnimationHermiteKey<T>(Duration Time, T Value, T IncomingTangent, T OutgoingTangent);
+public interface IAnimationHermiteInterpolator<T>
+{
+    T Interpolate(T from, T to, T outgoingTangent, T incomingTangent, Duration interval, float amount);
+}
+public static class AnimationHermiteInterpolators
+{
+    public static IAnimationHermiteInterpolator<float> Float { get; }
+    public static IAnimationHermiteInterpolator<Vector2> Vector2 { get; }
+    public static IAnimationHermiteInterpolator<Vector3> Vector3 { get; }
+    public static IAnimationHermiteInterpolator<Vector4> Vector4 { get; }
+    public static IAnimationHermiteInterpolator<Quaternion> Quaternion { get; }
+}
+public sealed class AnimationHermiteCurve<T> : IAnimationSource<T>
+{
+    // 正の duration、非空のキー、厳密増加かつ範囲内の Time。キー配列をコピーする。
+    public AnimationHermiteCurve(Duration duration, IReadOnlyList<AnimationHermiteKey<T>> keys, IAnimationHermiteInterpolator<T> interpolator);
+    public Duration Duration { get; }
+    public T Sample(Duration time);
+}
+public sealed class AnimationTimeRemap<T> : IAnimationSource<T>
+{
+    // 両子は正の長さ。外側の長さは timeMap.Duration。
+    public AnimationTimeRemap(IAnimationSource<T> source, IAnimationSource<Duration> timeMap);
+    public Duration Duration { get; }
+    // 写像結果が子の [0, Duration] 外なら ArgumentOutOfRangeException。
+    public T Sample(Duration time);
+}
+public sealed class AnimationBlend<T> : IAnimationSource<T>
+{
+    // 三つの子は同じ正の長さ。
+    public AnimationBlend(IAnimationSource<T> from, IAnimationSource<T> to, IAnimationSource<float> weight, IAnimationInterpolator<T> interpolator);
+    public Duration Duration { get; }
+    // 有限な [0, 1] の重み。0/1 は対応する子だけを評価する。
+    public T Sample(Duration time);
+}
```

Composition の宣言は以下とする。属性からファクトリー、with、子置換とインデクサーを生成する。SourceTrack は既存 Track と同じ寄与・Fill 契約を使用する。

```diff
 public static partial class ComposeAnimation
 {
+    // 生成される主なファクトリー。with は既存 Composition の編集アクション一覧。
+    public static Definitions.Sampled<T> Sampled<T>(IAnimationSource<T> value, IReadOnlyList<Action<Definitions.Sampled<T>>>? @with = null);
+    public static Definitions.Curve<T> Curve<T>(Duration duration, IAnimationInterpolator<T> interpolator, IReadOnlyList<Action<Definitions.Curve<T>>>? @with = null);
+    public static Definitions.HermiteCurve<T> HermiteCurve<T>(Duration duration, IAnimationHermiteInterpolator<T> interpolator, IReadOnlyList<Action<Definitions.HermiteCurve<T>>>? @with = null);
+    public static Definitions.TimeRemap<T> TimeRemap<T>(Definitions.Source<Duration> timeMap, Definitions.Source<T> value, IReadOnlyList<Action<Definitions.TimeRemap<T>>>? @with = null);
+    public static Definitions.Blend<T> Blend<T>(Definitions.Source<T> from, IAnimationInterpolator<T> interpolator, Definitions.Source<T> to, Definitions.Source<float> weight, IReadOnlyList<Action<Definitions.Blend<T>>>? @with = null);
+    public static Definitions.SourceTrack<T> SourceTrack<T>(AnimationChannel<T> channel, Definitions.Source<T> source, Lumyte.Composition.Optional<AnimationFillMode> fill = default, IReadOnlyList<Action<Definitions.SourceTrack<T>>>? @with = null);
+
     public static partial class Definitions
     {
+        public abstract class Source<T>
+        {
+            // 子を検証・確定する。編集可能な定義の参照は実行用ソースに残さない。
+            public IAnimationSource<T> Build();
+        }
+        public partial class Sampled<T> : Source<T>
+        {
+            // 正の長さを持つ不変ソース。子の実装は共有する。
+            public required IAnimationSource<T> Value { get; init; }
+        }
+        public partial class Curve<T> : Source<T>
+        {
+            // 正の値。
+            public required Duration Duration { get; init; }
+            public required IAnimationInterpolator<T> Interpolator { get; init; }
+            // 非空・厳密な時刻順。Build 時にキーをコピーする。
+            public IReadOnlyList<AnimationKey<T>> Keys { get; set; }
+        }
+        public partial class HermiteCurve<T> : Source<T>
+        {
+            // 正の値。
+            public required Duration Duration { get; init; }
+            public required IAnimationHermiteInterpolator<T> Interpolator { get; init; }
+            // 非空・厳密な時刻順。接線の単位は値／秒。
+            public IReadOnlyList<AnimationHermiteKey<T>> Keys { get; set; }
+        }
+        public partial class TimeRemap<T> : Source<T>
+        {
+            public required Source<T> Value { get; init; }
+            public required Source<Duration> TimeMap { get; init; }
+        }
+        public partial class Blend<T> : Source<T>
+        {
+            public required Source<T> From { get; init; }
+            public required Source<T> To { get; init; }
+            public required Source<float> Weight { get; init; }
+            public required IAnimationInterpolator<T> Interpolator { get; init; }
+        }
+        public partial class SourceTrack<T> : TimelineItem
+        {
+            public required AnimationChannel<T> Channel { get; init; }
+            public required Source<T> Source { get; init; }
+            // 既定は Hold。未定義の列挙値は Build 時に拒否。
+            public AnimationFillMode Fill { get; init; }
+        }
     }
 }
```

## 契約と数値

すべての値ソースは正の Duration と、副作用のない Sample を持つ。Sample の入力は [0, Duration] とし、範囲外を拒否する。キーの前後は端値を保持し、正確なキー時刻では保存値を直接返す。時間と値の補間を分離する。Hold はイージング結果に依存しない。

時間の進捗は既存の float の内点表現を維持する。Duration の内点補間は最寄りの Tick へ丸め、ちょうど半分は偶数丸めとする。境界は元の Ticks を直接返す。ベジェ y のオーバーシュートは潰さないが、独自 Timing の NaN／Infinity と float の表現範囲を超える結果は評価時に拒否する。区間設定を使うカーブで、計算後の標準値が非有限または非単位の回転になった場合も InvalidOperationException とする。独自補間器は受け取るオーバーシュートに対して意味を定める必要がある。離散値には Hold を指定する。

標準の Hermite は有限成分と正の区間長、有限な [0, 1] の進捗を要求する。倍精度で係数と成分を計算し、float へ丸めた結果が非有限の場合は拒否する。Quaternion は成分を倍精度のまま正規化してから float に変換し、有限な大きな接線や微小な接線を変換途中で失わない。三次補間は最短経路の符号反転を行わず、ゼロ長の結果を正規化できない場合は InvalidOperationException とする。

値ソースとタイムラインの構築は別であり、Source の Build は純粋な値計算用の不変ソースを返す。状態機械の Build が開始済み実行者を返す既存契約を変更しない。SourceTrack に入れた Source は Timeline の確定時に内部で Build する。循環した Source 定義は ArgumentException で拒否し、同じ子の共有は許す。 同じ Build 内では参照同一性で構築結果を再利用し、共有した定義を木へ展開しない。構築結果の再利用はその Build 内だけに限定し、後続の Build は編集後の新しいスナップショットを作る。キー配列はコピーするが、独自ソース・補間器・Timing・可変参照型の値は利用側が不変性を保証する。

## 例

```csharp
using System.Numerics;
using Lumyte.Animation;
using Lumyte.Core.Time;
using static Lumyte.Animation.ComposeAnimation;

var duration = Duration.FromSeconds(2);
var channel = AnimationChannel<Vector2>.Create();
var motion = Curve<Vector2>(duration, AnimationInterpolators.Vector2)[
    new AnimationKey<Vector2>(Duration.Zero, Vector2.Zero)
    {
        Timing = AnimationTimings.CubicBezier(0.42, 0, 0.58, 1),
        Interpolator = AnimationInterpolators.CubicBezier(
            new Vector2(0, 100), new Vector2(100, 100), AnimationInterpolators.Vector2),
    },
    new AnimationKey<Vector2>(duration, new Vector2(100, 0))];
var reverseTime = Curve<Duration>(duration, AnimationInterpolators.Duration)[
    new AnimationKey<Duration>(Duration.Zero, duration),
    new AnimationKey<Duration>(duration, Duration.Zero)];
var timeline = Timeline()[SourceTrack<Vector2>(channel,
    TimeRemap<Vector2>(value: motion, timeMap: reverseTime))].Build();
// 子の Curve／TimeRemap ごとの Build は不要。値の反映は消費側で行う。
```

```csharp
var blend = Blend<float>(
    from: Sampled<float>(new Tween<float>(0, 10, duration, AnimationInterpolators.Float, AnimationEasing.Linear)),
    to: Sampled<float>(new Tween<float>(10, 20, duration, AnimationInterpolators.Float, AnimationEasing.Linear)),
    weight: Curve<float>(duration, AnimationInterpolators.Float)[
        new AnimationKey<float>(Duration.Zero, 0),
        new AnimationKey<float>(duration, 1)],
    interpolator: AnimationInterpolators.Float);
var values = Timeline()[SourceTrack<float>(AnimationChannel<float>.Create(), blend)].Build();
```

### 共有ソースの評価

共有ノードを持つCompositionソースは、Sample呼び出しごとに独立した作業領域を借り、共有ソースの参照と局所時刻の組み合わせで計算結果を再利用する。時間写像が同じ子を異なる時刻で呼ぶ場合は別の値として扱う。Blendの重みが0または1の場合は選択した子だけを評価する。

作業領域は構築済みグラフ内で再利用するが、評価間で値を引き継がない。例外時も値の参照を消去して返却する。並行評価と再入呼び出しには別の作業領域を与え、時計や消費側の状態をキャッシュしない。共有のないグラフと直接構築した値ソースにはこの評価用ラッパーを追加しない。定常的に同じ規模を評価する場合は作業領域の容量を再利用し、初回・容量拡張・同時評価数増加を除いてManaged割り当てゼロを維持する。

### 汎用数学計算の配置

`Lumyte.Mathematics`を独立したNuGetプロジェクトとして`src/Core/`へ配置する。AnimationからMathematicsへの一方向の依存とし、MathematicsはAnimation・Core・Compositionに依存しない。線形・二次イージング、ベジェ評価と逆算、Hermiteの重み・成分評価、Quaternion球面補間・正規化を共有する。Animationはキー検証・Durationの秒への変換・補間インターフェースへの適合を担当する。

追加APIは以下。既存Animation APIのシグネチャは維持する。

```diff
+namespace Lumyte.Mathematics;
+public static class Interpolation
+{
+    public static float Linear(float from, float to, float amount);
+    public static double EaseIn(double amount);
+    public static double EaseOut(double amount);
+    public static double EaseInOut(double amount);
+}
+public static class IntegerInterpolation
+{
+    // floatの正確な二進重みを使い、最寄り整数・半分は偶数へ丸める。
+    public static long Linear(long from, long to, float amount);
+}
+public sealed class CubicBezierTiming
+{
+    public CubicBezierTiming(double x1, double y1, double x2, double y2);
+    public double Transform(double amount);
+}
+public static class BezierInterpolation
+{
+    public static T Cubic<T>(T from, T control1, T control2, T to,
+        float amount, Func<T, T, float, T> interpolate);
+}
+public static class HermiteInterpolation
+{
+    public static float Interpolate(float from, float to, float outgoingTangent,
+        float incomingTangent, double interval, float amount);
+    public static Vector2 Interpolate(Vector2 from, Vector2 to, Vector2 outgoingTangent,
+        Vector2 incomingTangent, double interval, float amount);
+    public static Vector3 Interpolate(Vector3 from, Vector3 to, Vector3 outgoingTangent,
+        Vector3 incomingTangent, double interval, float amount);
+    public static Vector4 Interpolate(Vector4 from, Vector4 to, Vector4 outgoingTangent,
+        Vector4 incomingTangent, double interval, float amount);
+    public static Quaternion Interpolate(Quaternion from, Quaternion to, Quaternion outgoingTangent,
+        Quaternion incomingTangent, double interval, float amount);
+}
+public static class QuaternionInterpolation
+{
+    public static Quaternion Slerp(Quaternion from, Quaternion to, float amount);
+    public static Quaternion Normalize(double x, double y, double z, double w);
+}
```

IntegerInterpolationは有限のfloat重みを正確な二進有理数として計算し、最寄りのlongへ偶数丸めする。外挿も許すが、丸めた結果がlongの範囲外ならOverflowException、非有限の重みならArgumentOutOfRangeExceptionを返す。Duration補間はこの計算を利用する。

MathematicsのHermiteはintervalの単位を定めず、接線と一致する正の有限値を要求する。amountは有限の[0, 1]、端値・接線は有限値、Quaternionの端値は単位回転を前提とする。時間ベジェも有限の[0, 1]を入力とし、不正なパラメーターはArgumentOutOfRangeExceptionになる。線形・空間ベジェ・球面補間は外挿を許す。ゼロ長・非有限Quaternionの正規化とfloatに収まらないHermite成分はInvalidOperationExceptionになる。

```csharp
using Lumyte.Mathematics;

var easing = new CubicBezierTiming(0.42, 0, 0.58, 1);
double progress = easing.Transform(0.5);
float value = HermiteInterpolation.Interpolate(0f, 10f, 2f, 0f, 3, 0.5f);
```

## 検討した代替案

### フォーマット固有のモデルをコアに入れる

パス、ボーン、描画モデルの所有権が形式や消費側と衝突する。今回必要な時間・値計算を共通化し、形式のアダプターは外側に置く。

### すべて独自 IAnimationSource で実装する

既存の拡張点として可能だが、時間ベジェの逆算、接線の単位、Quaternion の正規化とネストした設定の検証が利用側に分散する。共通の純粋な実装を提供する。

### Output 全体を重み付き合成する

未寄与チャネル、型ごとの合成器、参照姿勢と状態遷移の時間管理まで契約が広がる。今回の二入力ブレンドは値ソース内に限定し、Output と状態機械の規則を維持する。

## 結果と影響

- 形式を読み込んだ後のキーフレーム、接線、位置曲線、時間写像と重みを共通の計算へ変換できる。
- 既存キーの構築・分解と、既定の線形カーブを保持できる。
- 曲線・写像・ブレンドを Composition でネストでき、子ごとの Build を省ける。
- ベジェ逆算と複数ソースの評価には追加の計算コストがある。恒常的な割り当ては増やさない目標とする。カーブの時刻・値と追加の区間設定は内部で分け、設定がない通常のカーブには区間設定の配列を持たせない。構築時にはキーのスナップショットと内部の時刻・値配列を作る負担が増える。
- マスク、形状のトポロジー、骨格の制約、状態クロスフェード、各形式の全機能は別の責務・判断として残る。

## 検証方針

- 時間ベジェの逆算を独立した既知値・退化制御点・オーバーシュートで検証する。
- 区間ごとの Hold、イージング、空間補間器と、正確なキー時刻・境界保持を確認する。
- Hermite の秒単位接線、非等間隔キー、Quaternion 接線と正規化、非有限値を確認する。
- 逆行・停止の時間写像、無効写像、時変ブレンドと 0／1 の短絡を確認する。
- Composition の生成 API、定義のスナップショット、共有子と循環検証、SourceTrack の Fill を確認する。
- Release で新しい数値ソースのウォームアップ後の割り当てゼロと既存回帰テストを確認する。

## 実装・検証記録

2026-10-09、Linux x64 / .NET 10 で実装を確認した。Animation の Release テスト 89 件（追加 15 件）が成功した。時間ベジェの逆算、キー境界、Hold、空間補間、秒単位接線、Quaternion 正規化、極値と不正結果、時間写像、重みの短絡、定義の共有・循環・スナップショット、Release／Repeat を含む。Debug ビルド、対象の整形検証、Markdown 検査も成功した。割り当ての検証は Release で行った。

生成済み Composition API と Hermite を NuGet パッケージだけを参照する別プロジェクトから使用できた。サンプルは子ソースの Build を省いたベジェ・時間写像・ブレンドの組合せで、中間値 25.0 を確認した。

main `f3f4f67` と独立した Release DLL を比較し、各版 2 プロセス・各ケース 7 サンプルで測定した。線形の 2／128／1024 キーはそれぞれ約 11.03／18.74／24.46 ns から 11.39／18.94／24.08 ns で、同程度の範囲だった。追加の時間ベジェ、空間ベジェ、Hermite、時間写像、ブレンドを含む 10 ケースの全測定区間で Managed 割り当ては 0 B/op、GC は 0 回だった。

測定は Debian 13、AMD EPYC 9V74 の仮想環境、Runtime 10.0.12、単一 CPU 固定、tiered compilation／ReadyToRun 無効で行った。200ms のウォームアップ後に約 100ms の反復バッチを較正し、時刻生成・Sample・デリゲート呼出しと加算を測定した。構築時の時間・保持メモリ、通常の tiered PGO、Windows／Browser の性能は未測定。限定的な値評価の測定であり、描画や実際のフレーム時間を示すものではない。

## 参考資料

- [ADR-ANIMATION-0001: アニメーションシステム](ANIMATION-0001-animation-system.md)
- [ADR-ANIMATION-0003: 実行者の構築とイベント時刻](ANIMATION-0003-animation-execution-and-event-time.md)
- [glTF 2.0: Animation](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#animations)
- [CSS Easing Functions: Cubic Bézier](https://www.w3.org/TR/css-easing-1/#cubic-bezier-easing-functions)

2026-10-10、汎用計算をLumyte.Mathematicsへ分離し、単体Releaseテスト5件とAnimationの89件が成功した。MathematicsのQuaternion正規化は成分をスケールし、doubleの最大値・最小非ゼロ値も扱う。分離後に上記10ケースを再測定し、割り当て0 B/op・GC 0回を維持した。Quaternion Hermiteはこの正規化強化により約54.24 nsから70.88 nsとなった。

2026-10-10のレビュー修正後、ReleaseテストはAnimation 113件・Mathematics 20件の計133件が成功した。正確な整数補間は独立したBigInteger有理数計算約9,800組と照合した。共有グラフの並行・再入評価と例外後の清掃、異なる局所時刻、低速イベント時刻・最大Tick／Loop・Reverseの回帰テストを追加した。Debugビルド、整形、NuGetパッケージ経由の共有グラフと整数補間も確認した。

`0866c35`との交互2プロセス・各7サンプルの比較では、27定義の共有グラフのBuild割り当ては約51 MiBから8.75 KiBへ、評価は約13.06 msから3.19 µsへ減った。Duration補間は約61.25 nsから20.23 nsとなった。Loopマーカー評価は約176.94 nsから188.20 nsへ増加した。内部座標のInt128化を含む結果として記録する。全14評価ケースで割り当て0 B/op・GC 0回を確認した。測定環境は上記と同じで、構築と評価を別計測した。
