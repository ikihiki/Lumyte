# ADR-MATHEMATICS-0001: 2D・3D 幾何の精度を型引数で選択する

- 状態: 採用
- 日付: 2026-11-12
- 基準コミット: `1dbf62977a4805cb32d97811f8a890237bea20cb`

## 背景

利用者は、描画とは独立して 2D・3D の形状を表現し、座標の精度を用途ごとに選びたい。基準コードの Mathematics は補間を提供するが汎用の幾何型を持たず、Drawing2D の `Rect2D` は float の描画用矩形なので、double の幾何計算には利用側の型や計算が必要になる。

基準コードは `src/Core/Lumyte.Mathematics/Lumyte.Mathematics.csproj`、同ディレクトリの `Interpolation.cs`、`src/Graphics/Lumyte.Graphics.Drawing2D/Model/Rect2D.cs` の各宣言で確認できる。[ADR-0002 の各ディレクトリの役割](../0002-repository-layout.md#各ディレクトリの役割)と [ADR-ANIMATION-0004 の汎用数学計算の配置](../animation/ANIMATION-0004-format-independent-value-sources.md#汎用数学計算の配置)に従い、数学から描画・時計・Animation への依存は追加しない。

## 決定

既存の `Lumyte.Mathematics` パッケージに、名前空間 `Lumyte.Mathematics.Geometry` の不変な幾何値型を追加する。型名の `2D`・`3D` は座標空間の次元、型引数 `T` は成分の数値型を表し、`Circle2D<float>` と `Circle2D<double>` を同じ API で使えるようにする。

### 利用者の変更前・変更後

目的は、中心からの距離で円の内側を判定し、同じ処理を必要な精度で使うことである。変更前は汎用円型が存在しないため、例えば double の計算を利用側で記述する。

```csharp
// 変更前: 呼び出し側の計算。Lumyte の既存 API ではない。
double centerX = 0, centerY = 0, radius = 5;
double x = 3, y = 4;
double dx = x - centerX, dy = y - centerY;
bool inside = Math.Sqrt(dx * dx + dy * dy) <= radius; // true
```

変更後は以下の API を使う。境界上の点を含む結果は維持し、精度の選択を型引数へ移す。

```csharp
using Lumyte.Mathematics.Geometry;

var circle = new Circle2D<double>(new Point2D<double>(0, 0), 5);
bool inside = circle.Contains(new Point2D<double>(3, 4)); // true

var compact = new Circle2D<float>(new Point2D<float>(0, 0), 5);
bool same = compact.Contains(new Point2D<float>(3, 4)); // true

var sphere = new Sphere3D<double>(new Point3D<double>(0, 0, 0), 5);
bool inside3D = sphere.Contains(new Point3D<double>(0, 3, 4)); // true
```

整数の保存と分数の結果が必要な処理も、整数専用の別形状を使わず記述する。例えば以下では座標を int のまま保持し、射影の戻り値だけを decimal に選ぶ。

```csharp
using Lumyte.Mathematics.Geometry;

var bounds = Rect2D<int>.FromMinMax(new(0, 0), new(4, 6));
var segment = new Segment2D<int>(new(0, 0), new(1, 1));
Point2D<decimal> projected = segment.ClosestPoint<decimal>(new(1, 0)); // (0.5, 0.5)
bool inside = bounds.Contains(new(4, 6)); // 閉境界を含む
```

型引数が変われば丸め誤差と表現範囲も変わる。上の小整数の例は結果を共有するが、任意の入力で異なる精度の判定が一致する保証はしない。

### プロジェクト全体の統合

管理コードの汎用幾何計算は Mathematics または BCL に集約する。Drawing2D の曲線・距離・境界集約、clip の倍精度変換・winding、UI の半開境界、入力・診断の距離と矩形処理も同じ計算を共有する。既存の公開呼び出し側の記述は維持し、内部の重複計算を共通 API への呼び出しへ置き換える。

単位・半開区間・空集合・無限境界・検証方法の異なる型は薄い境界として維持する。GPU shader、native ABI、整数 pixel scissor、OS window、text caret、診断 wire の形状表現は、その境界の配置・精度・互換性を保持し、管理側の共有できる計算だけを委譲する。clip の FMA による倍精度演算、水平辺の半開遷移、描画の余白と subdivision budget は観測可能な契約として維持する。

### 次元・精度と依存境界

幾何の保存型は `INumber<T>` を満たす順序付き実数表現へ一般化し、整数・native integer・Int128・UInt128・BigInteger・decimal・Half・float・double と互換な独自型を扱う。`unmanaged` 制約を除去し、整数矩形を `Rect2D<T>` に統合する。複素数は順序を持つ実数でないため対象に含めない。

整数の距離・射影・補間は暗黙の整数除算で小数を失わない。保存型で完結する厳密判定・二乗距離・構築と、明示した計算結果型による距離・射影・補間を分離する。既存の IEEE 浮動小数点呼び出しの精度と演算順は維持する。decimal は自動的に double へ変換せず、BigInteger は double の範囲を超えても厳密判定できる。

ベクトル・行列・Quaternion は再実装せず、float は BCL の Vector2・Vector3・Matrix3x2、3D 行列は Matrix4x4 を使う。batch／span の型別高速経路は scalar と同じ意味を検証し、対応しない数値型・hardware には scalar を残す。空集合と無限境界は型の能力を区別し、整数の sentinel に浮動小数点無限大を埋め込まない。

### 形状と数値の契約

範囲は点、線分、軸平行矩形・箱、円・球、楕円、角丸矩形、Bezier 曲線の評価・平坦化、線分交差、閉輪郭の winding 判定とする。描画の曲線許容誤差、分割予算、stroke cap・join の三角形化、GPU coverage、整数 scissor の丸めと余白は Drawing2D が保持する。数学側はそれらに使う距離、境界の集約、変換後境界を提供し、レンダラーを複製しない。

- 有限な形状の成分は有限値を要求する。コンストラクターと演算の引数に NaN・無限大があれば `ArgumentOutOfRangeException` とする。半径・幅・高さが負、または箱の各軸で Min が Max より大きい場合も同様とする。
- 組み込み数値の `default` は各成分ゼロの有効値である。独自数値型の default はその型の規則に従い、原点には `Origin` を使える。半径ゼロ、幅・高さゼロ、Min と Max が同じ箱、始点と終点が同じ線分を許す。これらは空集合ではなく、点や線へ退化した閉集合として扱う。
- `Contains` は境界を含む。矩形・箱は各軸の閉区間、円・球は中心からの距離が半径以下の領域とする。ゼロ半径の円・球は中心だけを含む。距離はユークリッド距離で、点から線分への距離は射影を端点で制限し、ゼロ長線分では端点への距離を返す。
- 同値性は成分ごとの T の数値等値とし、許容誤差を混ぜない。正負のゼロは等値として同じハッシュを返す。近似比較や許容誤差付き判定の API は今回追加しない。
- 数値処理は不要な二乗オーバーフローを避けるスケーリングを行う。有限入力でも距離・射影で必要な差や最終結果を T の有限値として表現できない場合は `OverflowException` とし、NaN・無限大を正常な計算結果として返さない。`Contains` も演算不能を false に読み替えない。IEEE の向きと交差の符号判定は指数を分離して中間値の不要な桁あふれを避けるが、厳密算術ではない。整数・decimal の符号判定は有理数で厳密に計算する。
- 例として中心ゼロ・半径 5 の円は (3, 4) を含み、(3, 4.5) を含まない。幅ゼロの矩形はその直線上の閉区間を含む。double の (1e200, 0) と原点の距離は有限の 1e200 を返し、不要な二乗の無限大を返さない。

`NumericsGeometry` は BCL の float 演算をそのまま提供する境界であり、非有限値を拒否しない。描画側は変換後の有限性を検証し、既存の例外・余白・丸めを保持する。

不変な値型なので、破棄・スレッド所有・更新通知の仕組みは持たない。GPU バッファとのメモリ配置互換や SIMD 化、割り当て・速度の数値目標はこの判断では保証しない。

### 公開 API 差分

基準コードに対する追加の主要宣言を以下に示す。宣言は判断対象の抜粋であり、実装コードではない。省略した各型の読み取り専用プロパティはコンストラクター引数の先頭を大文字にした同名・同型とし、幾何値型で成分の値等値を提供する。引数の有限性、退化形状、算術失敗は「形状と数値の契約」に従う。

```diff
+using System.Numerics;
+
+namespace Lumyte.Mathematics.Geometry;
+
+public readonly record struct Point2D<T> where T : INumber<T>
+{
+    public Point2D(T x, T y);
+    public T X { get; }
+    public T Y { get; }
+    public T DistanceTo(Point2D<T> other);
+}
+
+public readonly record struct Point3D<T> where T : INumber<T>
+{
+    public Point3D(T x, T y, T z);
+    public T X { get; }
+    public T Y { get; }
+    public T Z { get; }
+    public T DistanceTo(Point3D<T> other);
+}
+
+public readonly struct Segment2D<T> where T : INumber<T>
+{
+    public Segment2D(Point2D<T> start, Point2D<T> end);
+    public T DistanceTo(Point2D<T> point);
+    public Point2D<T> ClosestPoint(Point2D<T> point);
+    public bool Intersects(Segment2D<T> other);
+}
+
+public readonly struct Segment3D<T> where T : INumber<T>
+{
+    public Segment3D(Point3D<T> start, Point3D<T> end);
+    public T DistanceTo(Point3D<T> point);
+    public Point3D<T> ClosestPoint(Point3D<T> point);
+}
+
+public readonly struct Rect2D<T> where T : INumber<T>
+{
+    // X・Y が最小側、Width・Height が非負の長さ。最大側も有限であること。
+    public Rect2D(T x, T y, T width, T height);
+    public bool Contains(Point2D<T> point);
+    // 最大側を除外し、幅・高さゼロは空集合として扱う。
+    public bool ContainsHalfOpen(Point2D<T> point);
+}
+
+public readonly struct AxisAlignedBox3D<T> where T : INumber<T>
+{
+    public AxisAlignedBox3D(Point3D<T> min, Point3D<T> max);
+    public bool Contains(Point3D<T> point);
+}
+
+public readonly struct Circle2D<T> where T : INumber<T>
+{
+    public Circle2D(Point2D<T> center, T radius);
+    public bool Contains(Point2D<T> point);
+}
+
+public readonly struct Sphere3D<T> where T : INumber<T>
+{
+    public Sphere3D(Point3D<T> center, T radius);
+    public bool Contains(Point3D<T> point);
+}
+
+public readonly record struct RoundedRect2D<T> where T : INumber<T>
+{
+    // 半径は非負。短辺の半分へ制限する。
+    public RoundedRect2D(Rect2D<T> bounds, T radius);
+    public bool Contains(Point2D<T> point);
+}
+
+public readonly record struct Ellipse2D<T> where T : INumber<T>
+{
+    public Ellipse2D(Point2D<T> center, T radiusX, T radiusY);
+    public bool Contains(Point2D<T> point);
+}
+
+public static class NumericsGeometry
+{
+    // BCL の float 座標とアフィン行列を使う。四隅を変換し、割り当てずに縮約する。
+    public static void GetTransformedBounds(Vector2 minimum, Vector2 maximum,
+        Matrix3x2 transform, out Vector2 transformedMinimum, out Vector2 transformedMaximum);
+    // 行列への scale の合成と異なり、各点を変換後に乗算して丸めを維持する。
+    public static void GetTransformedBounds(Vector2 minimum, Vector2 maximum,
+        Matrix3x2 transform, float scale, out Vector2 transformedMinimum, out Vector2 transformedMaximum);
+    public static void GetBounds(Vector2 a, Vector2 b, Vector2 c, Vector2 d,
+        out Vector2 minimum, out Vector2 maximum);
+}
+
+public static class Polygon2D
+{
+    // 輪郭を閉じ、境界を含む。空輪郭は false。
+    public static bool Contains<T>(ReadOnlySpan<Point2D<T>> vertices, Point2D<T> point)
+        where T : INumber<T>;
+    public static bool ContainsEvenOdd<T>(ReadOnlySpan<Point2D<T>> vertices, Point2D<T> point)
+        where T : INumber<T>;
+}
+
+public static class Bezier2D
+{
+    // amount は有限な閉区間 [0, 1]。
+    public static Point2D<T> Quadratic<T>(Point2D<T> start, Point2D<T> control,
+        Point2D<T> end, T amount) where T : INumber<T>;
+    public static Point2D<T> Cubic<T>(Point2D<T> start, Point2D<T> control1,
+        Point2D<T> control2, Point2D<T> end, T amount) where T : INumber<T>;
+    // 正の有限 tolerance、深さ 0..30、追加点数 2 以上。予算超過は false、出力を維持する。
+    public static bool TryFlattenQuadratic<T>(Point2D<T> start, Point2D<T> control,
+        Point2D<T> end, T tolerance, List<Point2D<T>> destination, int maxDepth, int maxPoints)
+        where T : INumber<T>;
+    public static bool TryFlattenCubic<T>(Point2D<T> start, Point2D<T> control1,
+        Point2D<T> control2, Point2D<T> end, T tolerance, List<Point2D<T>> destination,
+        int maxDepth, int maxPoints) where T : INumber<T>;
+}
```

`Rect2D<T>` の x + width、y + height が有限に収まらない場合、構築時に `OverflowException` とする。`Sphere3D<T>` は球であり、3D 空間内に置いた円を意味する `Circle3D<T>` と混同しない。軸平行を表す型名は、回転可能な箱と区別できる `AxisAlignedBox3D<T>` とする。

既存 `Lumyte.Graphics.Drawing2D.Rect2D` の名前空間、float 成分、コンストラクターは維持する。数学側へ描画型を参照させず、利用側は成分を取り出して描画型を構築する。double などから float へ落とす際は利用側が範囲・丸めを確認し、変換結果が有限かを検証する。補間 API のジェネリック化、描画 API の一括置換、暗黙の相互変換は含めない。`EffectPixelRect` は整数 pixel の単位を表す公開境界として残し、保存と全幾何計算を `Rect2D<int>` に委譲する。`Rect2D` は未検証のコマンド宣言であり、有限・閉集合の数学 `Rect2D<float>` と検証時点が異なるため別の境界とする。

### 境界方針と追加 API

閉形状の `Rect2D<T>` と、半開・空・無限境界を表す `Bounds2D<T>` は異なる集合契約を持つ。後者の raw edge は逆転をそのまま保持し、幅・高さの変換時だけゼロへ制限する。UI が既存の NaN の比較規則を必要とする箇所では、その規則を薄いアダプターに残す。原点・寸法から上限を作る精度は呼び出し側の T で指定し、float の和と double の和を取り違えない。

clip は FMA の演算順を指定する `AffineGeometry2D`・`WindingGeometry2D` を使う。これは境界を含む堅牢な `Polygon2D` と異なり、半開 Y 遷移と厳密な符号比較を維持する低水準の演算である。float の streaming 曲線は既存の BCL 評価順を保つ `DrawingGeometry` を使い、validated generic 曲線へ変換して丸めを変えない。sink の失敗時は追加済み点を保持し、renderer が全体の edge budget を管理する。

以下も基準コミットに対する追加宣言の抜粋である。既存公開型の引数・戻り値と例外契約は維持し、内部委譲だけを変える。

```diff
+public readonly record struct Bounds2D<T>(T Left, T Top, T Right, T Bottom)
+    where T : INumber<T>
+{
+    // 半開・未検証。逆転・ゼロ面積・NaN は空、無限辺を許す。
+    public static Bounds2D<T> Empty { get; }
+    public static Bounds2D<T> Unbounded { get; }
+    public bool Contains(T x, T y);
+    public Bounds2D<T> Intersect(Bounds2D<T> other);
+    public Bounds2D<T> Union(Bounds2D<T> other);
+}
+
+public static class CoordinateGeometry
+{
+    // 検証は呼び出し側。raw edge の共通演算と origin-size の明示変換。整数演算は checked。
+    public static bool ContainsHalfOpenEdges<T>((T Left, T Top, T Right, T Bottom) edges,
+        T pointX, T pointY) where T : INumber<T>;
+    public static (T Left, T Top, T Right, T Bottom) IntersectRawEdges<T>(
+        (T Left, T Top, T Right, T Bottom) first,
+        (T Left, T Top, T Right, T Bottom) second) where T : INumber<T>;
+    public static (T Left, T Top, T Right, T Bottom) EncloseRawEdges<T>(
+        (T Left, T Top, T Right, T Bottom) first,
+        (T Left, T Top, T Right, T Bottom) second) where T : INumber<T>;
+}
+
+public static class AffineGeometry2D
+{
+    // IEEE は入れ子の FMA。非 IEEE の行列係数変換は損失を拒否する。
+    public static Point2D<T> Transform<T>(Point2D<T> point, Matrix3x2 transform)
+        where T : INumber<T>;
+}
+
+public static class WindingGeometry2D
+{
+    public static int Contribution<T>(T startX, T startY, T endX, T endY, T pointX, T pointY)
+        where T : INumber<T>;
+    public static int Accumulate<T>(ReadOnlySpan<Segment2D<T>> edges, Point2D<T> point)
+        where T : INumber<T>;
+}
+
+public static class DrawingGeometry
+{
+    public interface IPointSink { void Append(Vector2 point); }
+    public static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end);
+    public static void IncludeInBounds(Vector2 point, ref Vector2 minimum, ref Vector2 maximum);
+    // streaming。深さ 0..30、callback/sink 失敗では追加済み点を保持する。
+    public static void AppendFlattenedQuadratic<TSink>(Vector2 start, Vector2 control,
+        Vector2 end, float tolerance, ref TSink sink, int maxDepth) where TSink : struct, IPointSink;
+    public static void AppendFlattenedCubic<TSink>(Vector2 start, Vector2 control1,
+        Vector2 control2, Vector2 end, float tolerance, ref TSink sink, int maxDepth)
+        where TSink : struct, IPointSink;
+}
```

### 数値演算・構築関数・batch の契約

閉矩形の `Union` は退化した点・線も含め、`Outset` はそれらも拡張する。閉集合の交差は `TryIntersect` が空集合の有無を返し、境界だけの接触も成功とする。`EffectPixelRect` は半開 pixel の空集合規則をアダプターで維持し、空の union と outset だけを判定して共通矩形へ委譲する。

整数と decimal の幾何判定および射影の中間値は BigInteger による厳密な有理数として扱い、unsigned の負の差を wrap させない。既存の同一 IEEE 型による距離と射影は従来の演算順を維持する。異なる結果型を指定した metric は元の座標差・射影から計算し、最終結果だけを変換する。整数結果に小数や無理数が必要なら `ArithmeticException` とし、明示した fractional な結果型を選ぶ。

decimal の分数結果と平方根は decimal の精度で最近接偶数へ丸め、double を経由しない。IEEE 結果への有理数変換も対象精度へ一度で丸める。独自数値型の同型演算は checked 演算・変換契約に従い、平方根の方針がない型はその metric だけを拒否する。独自型から異なる metric 結果型へ進む場合は decimal または BigInteger との厳密な往復変換を確認し、確認できなければ `ArithmeticException` とする。

`ConvertChecked` は BCL の `CreateChecked` と同じ範囲検査であり、無損失変換の保証ではない。分数から整数への明示変換や大整数から浮動小数点への変換は丸めを含み得る。保存する中心・半径・寸法の半分がその型で表現できない centered factory や radius clamp は `ArgumentException`、Try factory は false とする。整数の直線曲線は分割を必要としなければそのまま平坦化でき、分割で分数が必要なら明示した結果型を使う。

`Bounds2D<T>.Empty` は IEEE 型では逆転した無限辺、その他ではゼロ面積の default を使う。`TryGetUnbounded` は無限大がない型で false を返し、`Unbounded` はその場合 `NotSupportedException` とする。構築・containment・有限境界演算まで拒否するものではない。

構築は用途が分かる `FromMinMax`・`FromCenterSize`・`FromPoints`、検証失敗を分岐にする `TryCreate`・`TryFromPoints` にそろえる。point の座標 span は成分を通常の読み書きで扱い、BigInteger や参照を含む独自型のメモリ配置を仮定しない。空の点集合は From factory が `ArgumentException`、Try factory は false とする。

`GeometryBatch.SquaredDistances` は float／double の座標 span に対して各点独立の差・二乗・加算を行う。BCL Vector256／Vector128 を利用し、未対応 hardware と端数は scalar へ戻す。FMA への融合や点間の和の並べ替えは行わず、IEEE の overflow・infinity・NaN を伝播する（NaN payload は未規定）。長さとずれた重複を出力前に検査し、同じ開始位置の in-place は許す。

追加 API の主要宣言は以下の通りである。既存の同型 metric 呼び出しは残る。

```diff
+public readonly record struct Point2D<T> where T : INumber<T>
+{
+    public static Point2D<T> Origin { get; }
+    public static bool TryCreate(T x, T y, out Point2D<T> point);
+    public static Point2D<T> FromCoordinates(ReadOnlySpan<T> coordinates);
+    public void CopyTo(Span<T> destination);
+    public void Deconstruct(out T x, out T y);
+    public TResult DistanceTo<TResult>(Point2D<T> other) where TResult : INumber<TResult>;
+    public TResult DistanceSquared<TResult>(Point2D<T> other) where TResult : INumber<TResult>;
+    public Point2D<TTo> ConvertChecked<TTo>() where TTo : INumber<TTo>;
+}
+
+public readonly record struct Segment2D<T> where T : INumber<T>
+{
+    public Point2D<TResult> ClosestPoint<TResult>(Point2D<T> point) where TResult : INumber<TResult>;
+    public TResult DistanceTo<TResult>(Point2D<T> point) where TResult : INumber<TResult>;
+    public TResult DistanceSquared<TResult>(Point2D<T> point) where TResult : INumber<TResult>;
+}
+
+public readonly record struct Rect2D<T> where T : INumber<T>
+{
+    public static Rect2D<T> FromMinMax(Point2D<T> min, Point2D<T> max);
+    public static Rect2D<T> FromCenterSize(Point2D<T> center, T width, T height);
+    public static Rect2D<T> FromPoints(ReadOnlySpan<Point2D<T>> points);
+    public static bool TryFromPoints(ReadOnlySpan<Point2D<T>> points, out Rect2D<T> result);
+    public static bool TryCreate(T x, T y, T width, T height, out Rect2D<T> result);
+    public Rect2D<TTo> ConvertChecked<TTo>() where TTo : INumber<TTo>;
+    public Rect2D<T> Offset(T x, T y);
+    public Rect2D<T> OffsetInverse(T x, T y);
+    public Rect2D<T> Outset(T x, T y);
+    public Rect2D<T> Union(Rect2D<T> other);
+    public bool TryIntersect(Rect2D<T> other, out Rect2D<T> result);
+}
+
+public static class GeometryBatch
+{
+    public static void SquaredDistances(ReadOnlySpan<float> x, ReadOnlySpan<float> y,
+        float pointX, float pointY, Span<float> destination);
+    public static void SquaredDistances(ReadOnlySpan<double> x, ReadOnlySpan<double> y,
+        double pointX, double pointY, Span<double> destination);
+}
```

```diff
+public static class Bezier2D
+{
+    public static Point2D<TResult> Quadratic<T, TResult>(Point2D<T> start,
+        Point2D<T> control, Point2D<T> end, TResult amount)
+        where T : INumber<T> where TResult : INumber<TResult>;
+}
+
+public static class AffineGeometry2D
+{
+    public static Point2D<TResult> Transform<T, TResult>(Point2D<T> point, Matrix3x2 transform)
+        where T : INumber<T> where TResult : INumber<TResult>;
+    // 係数も保存型のまま渡す checked な変換。
+    public static Point2D<T> Transform<T>(Point2D<T> point, T m11, T m12, T m21, T m22, T m31, T m32)
+        where T : INumber<T>;
+}
```

3D の point・segment も同じ結果型選択・span・変換の規則を使う。箱は `FromMinMax`・`FromCenterSize`・`FromPoints`・`TryFromPoints`・`Union`・`TryIntersect`、各形状は `TryCreate` と `ConvertChecked` を提供する。Bezier の評価・平坦化と affine 変換も保存型と結果型を分けた overload を提供する。

## 検討した代替案

- `Circle2F`・`Circle2D` のように精度別の型名を用意する: 各型を最適化しやすいが、次元と精度の D が紛らわしく、同じ処理の宣言と検証が増える。次元は名前、精度は型引数へ分離する。
- `Geometry2D.Circle<T>`・`Geometry3D.Sphere<T>` と次元別に配置する: 型名は短くなるが、両次元を使う処理で修飾や別名が増える。共通名前空間と次元接尾辞を選ぶ。
- 保存型と計算結果型を常に同じにする: 宣言は短いが、整数の距離・射影・中点を切り捨てるか表現できなくなる。保存は共通の INumber 制約とし、分数を要する演算では結果型を選べるようにする。
- すべて double、または既存の float ベクトルへ統一する: 宣言は簡単だが、利用者が成分型と計算精度を選べない。保存だけ T にして内部を double へ固定する案も採らない。

## 結果と影響

利用者は次元と精度を独立して選び、2D・3D の幾何を描画依存なしで扱える。既存コードは書き換え不要だが、新しい幾何型と描画型の境界では明示的な成分変換が必要になる。ジェネリック化により型ごとのコード生成量や最適化の差が生じるため、SIMD の利用や高速化は保証ではなく実装時の確認対象となる。

受け入れ条件は、符号付き・符号なし整数、native integer、Int128・UInt128、BigInteger、decimal、Half・float・double と参照を含む独自 INumber 型での構築、境界を含む内外判定、2D・3D の距離と線分への射影、退化形状、default、正負のゼロの等値・ハッシュを検証することである。NaN・無限大・負の寸法・逆転した境界の拒否、表現限界付近の距離、算術結果のオーバーフロー、精度ごとの丸め差も確認する。double の値が float に狭められていないことは、float では区別できない座標差を使って確認する。BigInteger は double の表現範囲を超える座標、decimal は末尾桁、異なる結果型の metric は変換前の座標差と整数の分数拒否を検証する。batch は scalar と同じ結果、長さ・重複検査、intrinsic 無効時の fallback を確認する。

既存 Mathematics・Animation・Drawing2D のテストと公開宣言を照合し、幾何追加によって依存方向や既存 API の意味が変わらないことを確認する。採用は実装・検証の完了を意味せず、これらの受け入れ条件はコードとテストで確認する。
