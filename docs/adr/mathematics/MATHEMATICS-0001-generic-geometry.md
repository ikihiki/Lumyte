# ADR-MATHEMATICS-0001: 数値型を選べる共通幾何へ計算を集約する

- 状態: 採用
- 日付: 2026-11-12
- 基準コミット: `1dbf62977a4805cb32d97811f8a890237bea20cb`

## 背景

利用者は、描画・UI・ウィンドウで使う幾何を共有し、整数・decimal・浮動小数点などの座標表現を用途ごとに選びたい。基準コードには描画用の float 矩形と各機能の境界計算があるが、数値型を選べる共通幾何がなく、別精度の計算や境界規則の維持を利用側が負担している。

基準コミットの `src/Core/Lumyte.Mathematics/Lumyte.Mathematics.csproj` は独立した数値計算の配置先であり、同ディレクトリの `Interpolation.cs` は補間を提供する。`src/Graphics/Lumyte.Graphics.Drawing2D/Model/Rect2D.cs` の `Rect2D` は float の宣言型、`Rendering/DrawingBounds2D.cs`・`Rendering/PathFlattening2D.cs`・`Rendering/NormalizedClip2D.cs` はその描画計算を持つ。`src/UI/Lumyte.UI/UiInputBounds.cs` の `UiInputBounds` と `src/Graphics/Lumyte.Graphics.ImageEffects/EffectPixelRect.cs` の `EffectPixelRect` には半開境界・整数演算の別契約がある。

[ADR-0002 の各ディレクトリの役割](../0002-repository-layout.md#各ディレクトリの役割)と [ADR-ANIMATION-0004 の汎用数学計算の配置](../animation/ANIMATION-0004-format-independent-value-sources.md#汎用数学計算の配置)に従い、数学から描画・時計・Animation への依存を追加せずに共通化する。

## 決定

`Lumyte.Mathematics.Geometry` に、順序付き数値型を選べる幾何値と共通計算を集約する。共通計算の公開入口は `Geometry2D`・`Geometry3D` に統一し、実装ファイルの分割を公開クラスの分割として露出しない。次元は `2D`・`3D`、保存する数値型は `T`、必要に応じて計算結果の型は `TResult` で指定し、既存機能は境界規則を保ったまま共通計算と `System.Numerics` を利用する。

### 利用者の変更前・変更後

基準コードには汎用の円型がないため、円内判定は利用側で記述する。次は既存 API を模したものではなく、利用側の代替計算である。

```csharp
// 変更前: 半径 5 の円に点 (3, 4) が含まれるか。
double centerX = 0, centerY = 0, radius = 5;
double x = 3, y = 4;
double dx = x - centerX, dy = y - centerY;
bool inside = Math.Sqrt(dx * dx + dy * dy) <= radius; // true
```

変更後は同じ判定を共通形状で記述する。保存に整数を使い、結果に小数が必要な射影だけを decimal にすることもできる。

```csharp
using Lumyte.Mathematics.Geometry;

var circle = new Circle2D<double>(new(0, 0), 5);
bool inside = circle.Contains(new(3, 4)); // true。境界を含む。

var rectangle = Rect2D<int>.FromMinMax(new(0, 0), new(4, 6));
bool closed = rectangle.Contains(new(4, 6)); // true
bool halfOpen = rectangle.ContainsHalfOpen(new(4, 6)); // false
var segment = new Segment2D<int>(new(0, 0), new(1, 1));
Point2D<decimal> projected = segment.ClosestPoint<decimal>(new(1, 0)); // (0.5m, 0.5m)
Point2D<decimal> curve = Geometry2D.EvaluateQuadraticBezier<int, decimal>(
    new(0, 0), new(0, 1), new(1, 0), 0.5m); // (0.25m, 0.5m)
double distance3D = Geometry3D.Distance<int, double>(new(0, 0, 0), new(2, 3, 6)); // 7
```

既存の描画・UI・ウィンドウの呼び出し側は書き換え不要とし、内部の重複計算を委譲へ変える。精度や結果型を変えると表現範囲・丸め・失敗条件も変わり、異なる数値型で任意の判定結果が一致する保証はしない。

### 数値表現と演算結果

幾何の保存型と共通 helper の制約は `INumber<T>` とし、`unmanaged` は要求しない。符号付き・符号なし整数、native integer、Int128・UInt128、BigInteger、decimal、Half・float・double、契約を満たす独自型を扱う。複素数は順序付きの実座標でないため対象外とする。整数矩形も `Rect2D<T>` で表し、数値型別の矩形系列は設けない。

- **同型演算:** `DistanceTo`・`ClosestPoint` などは保存型を結果型にも使う。整数に小数や無理数が必要なら `ArithmeticException` とし、整数除算で黙って切り捨てない。
- **明示した結果型:** `DistanceTo<TResult>`・`ClosestPoint<TResult>`・Bezier の結果型 overload で、保存と結果の表現を分ける。距離・射影は元座標から計算し、最終結果を変換する。巨大な共通座標を先に float へ変換して小さな差を失わない。
- **厳密性と丸め:** 整数・decimal の幾何判定と射影の中間値は BigInteger による有理数で扱う。decimal の分数結果と平方根は decimal 精度で最近接偶数へ丸め、double を経由しない。有理数から IEEE 結果への変換も対象精度へ一度で丸める。
- **既存 IEEE 経路:** 同じ IEEE 型の距離・射影は精度と演算順を維持する。不要な二乗の桁あふれを避けるが、向き・交差の浮動小数点判定に厳密算術は保証しない。
- **独自数値型:** 同型演算は checked 演算とその型の変換契約に従う。平方根の方針がなければその演算は `NotSupportedException` とし、対応する明示結果型を選べる。異なる metric 結果型への変換では decimal または BigInteger との厳密な往復変換を確認できなければ `ArithmeticException` とする。
- **明示的な型変換:** `ConvertChecked<TTo>` は BCL の `CreateChecked` に従う範囲検査であり、無損失変換ではない。分数の整数化や大整数の浮動小数点化には丸めを含み得る。

演算不能を false に読み替えず、checked 算術の桁あふれと有限な結果を表現できない場合は `OverflowException` とする。非整数結果の拒否と、利用者が明示した丸め付き型変換は別の契約である。

### 形状・境界・構築

対象は点、線分、軸平行矩形・箱、円・球、楕円、角丸矩形、Bezier の評価・平坦化、線分交差、輪郭の内外判定とする。`Sphere3D<T>` は球であり、3D 空間内に置かれた円とは区別する。座標の単位・画面の上下・左右手系は固定しない。

**有限な閉形状**は NaN・無限大、負の寸法・半径、逆転した min/max を `ArgumentOutOfRangeException` で拒否する。`Contains` は境界を含み、半径ゼロ・ゼロ長線分・零面積矩形も点や線へ退化した集合として扱う。閉矩形の `Union`・`Outset` は退化形状も対象とし、`TryIntersect` は辺・点の接触も成功とする。`Rect2D<T>.IsEmpty` は零面積の指標であり、閉集合の不存在を意味しない。

**半開の座標境界**は `Bounds2D<T>` と `Geometry2D` の境界操作に分離する。raw edge は逆転した空境界や NaN を保持し、上限の加算精度は呼び出し側の T に従う。`Bounds2D<T>` の Contains は最大側を除外し、逆転・零面積・NaN は空とする。Empty は IEEE 型では逆転した無限辺、その他では零面積の default とし、無限大のない型で `TryGetUnbounded` は false、Unbounded は `NotSupportedException` とする。既存 UI が必要とする NaN の union 規則は薄いアダプターで維持する。

構築名は `FromMinMax`・`FromCenterSize`・`FromPoints` とし、検証失敗を分岐にする場合は `TryCreate`・`TryFromPoints` を使う。min/max の逆順を暗黙に整列せず、空の点列は From factory で `ArgumentException`、Try factory で false とする。角丸半径は短辺の半分へ clamp する。Half・float・double の角丸半径は必要ならその半分以下の最大の表現可能値へ下向きに丸め、半径が逆側の角の開始位置を超えないようにする。中心からの半寸法、または非 IEEE 型の角丸半径の制限値が T で表現できない場合は `ArgumentException`、対応する Try factory は false とし、整数の半分を切り捨てない。

点は Origin、座標 span の構築・コピー・deconstruction を提供する。BigInteger や参照を含む独自型のメモリ配置は仮定しない。組み込み型の default はゼロとして有効だが、独自型の default はその型に従う。同値性・ハッシュは成分の数値等値とし、正負のゼロを等しく扱い、許容誤差は混ぜない。

`FromMinMax`・`FromPoints`・`Union` は、保存した幅・高さを原点へ足し戻しても指定した最大辺を含むことを保証する。丸めで内側へ戻る場合だけ extent を外向きに調整し、既に包含する値は広げない。IEEE は直後の表現可能 extent を一度だけ試し、decimal は有限桁の範囲で次の表現可能値を構築する。extent または再構成した辺が有限に表せない場合は `OverflowException` とし、Try factory は false を返す。Outset も要求した最大辺を失う場合に同じ補正を行う。交差と center/size のような別の集合・寸法契約へ外向き補正を一律適用しない。

### 共通計算と利用側の責務

形状固有の構築・包含・距離は値型に残し、Bezier・輪郭・境界・affine・batch などの共有操作は次元別の入口へ置く。BCL float 演算、FMA、checked、半開境界、空領域の扱いをメソッド名と引数で区別する。streaming の論理 sink は `Geometry2D.IStreamingPointSink` とし、内部の helper 型を戻り値・引数・制約から漏らさない。friend assembly を追加して公開境界を迂回しない。

ベクトル・行列・Quaternion は再実装しない。float の通常計算は BCL の Vector2・Vector3・Matrix3x2、3D の行列計算は Matrix4x4 を使う。数学側に描画・OS・通信への参照を持たせず、次の共通計算を利用側から呼び出す。

- `Geometry2D.GetTransformedBoundsBcl`・`AppendFlattenedQuadraticBcl` など: 境界集約、変換後境界、float の線分距離・曲線分割。BCL の演算順と非有限値の扱いを保ち、scale overload は行列へ scale を合成せず各点の変換後に乗算する。変換後の有限性・描画の余白・pixel 丸めは利用側が検証する。
- `Geometry2D.TransformAffineFused`・`AccumulateWindingFused`: clip の倍精度変換、融合積和（FMA）の評価順、半開 Y 遷移と符号比較。境界を含む `ContainsPolygonClosed` とは異なる低水準規則を維持する。非 IEEE の行列係数変換は損失を拒否する。
- `Bounds2D<T>` と `Geometry2D.ContainsHalfOpenEdges`・`IntersectBoundsClamped` など: 描画・UI・ウィンドウ・text の包含、交差、境界集約。半開領域、空の位置、NaN、float 和と double 和を区別する。

`Lumyte.Graphics.Drawing2D.Rect2D` は未検証のコマンド宣言、`EffectPixelRect` は整数 pixel の単位と半開 empty 規則を持つため公開境界として維持する。後者は保存・計算を `Rect2D<int>` に委譲し、空の union・outset の判定だけをアダプターに残す。OS window・caret・診断 wire・native ABI・GPU shader と外部ライブラリの形状表現も、その単位・配置・互換性を保持する。

描画側は stroke cap・join の三角形化、曲線誤差・分割予算、GPU coverage、整数 scissor の丸めと余白を担当する。汎用 Bezier の補間は非ゼロの定数成分を保ち、IEEE の積が subnormal またはゼロへ underflow する場合は厳密な加重和から一度で丸める。通常の IEEE 加重演算と signed zero の規則、decimal の厳密な中間計算を維持する。汎用 Bezier は有限な amount の閉区間 [0, 1]、正の有限 tolerance、深さ 0..30、追加点数 2 以上を要求する。平坦化は予算超過時に false を返して出力を維持し、描画用 streaming 分割は sink 失敗時に追加済み点を保持する。この差を共通化のために消さない。既存補間 API と描画公開 API の意味は変更しない。

### SIMD と一括処理

`Geometry2D.SquaredDistancesBcl` は float・double の別座標 span に対し、各点で差・二乗・加算を行う。BCL Vector256、Vector128、scalar の順に使用可能な経路を選び、端数も scalar で処理する。その他の数値型は一般の幾何 API を使い、一括 SIMD 対応と数値型の対応範囲を混同しない。

FMA へ融合せず、点間の加算順序も変更しない。IEEE の overflow・infinity・NaN を伝播し、NaN payload は未規定とする。長さとずれた重複を出力前に検査し、同じ開始位置の in-place、空入力、出力末尾の保持を許す。SIMD を全呼び出しへ強制せず、少量処理・未対応 hardware には通常経路を残す。GPU 配置互換、速度・割り当ての数値目標は保証しない。

### 公開 API 差分

以下は基準コミットに対する追加の主要宣言であり、全 API の一覧ではない。共通契約は上記各節に従い、3D の点・線分も同じ結果型・変換・座標アクセス規則を使う。省略した形状は `INumber<T>` を制約とする readonly record struct で、検証付き構築・包含・明示変換を提供する。

```diff
+using System.Numerics;
+
+namespace Lumyte.Mathematics.Geometry;
+
+public readonly record struct Point2D<T> where T : INumber<T>
+{
+    public Point2D(T x, T y);
+    public static Point2D<T> Origin { get; }
+    public static bool TryCreate(T x, T y, out Point2D<T> point);
+    // 成分数は正確に 2。CopyTo は 2 要素以上の出力へ書く。
+    public static Point2D<T> FromCoordinates(ReadOnlySpan<T> coordinates);
+    public void CopyTo(Span<T> destination);
+    public void Deconstruct(out T x, out T y);
+    public T DistanceTo(Point2D<T> other);
+    public TResult DistanceTo<TResult>(Point2D<T> other) where TResult : INumber<TResult>;
+    public TResult DistanceSquared<TResult>(Point2D<T> other) where TResult : INumber<TResult>;
+    public Point2D<TTo> ConvertChecked<TTo>() where TTo : INumber<TTo>;
+}
+
+public readonly record struct Segment2D<T> where T : INumber<T>
+{
+    public Segment2D(Point2D<T> start, Point2D<T> end);
+    // 射影は端点で制限。ゼロ長なら端点を返す。
+    public Point2D<TResult> ClosestPoint<TResult>(Point2D<T> point) where TResult : INumber<TResult>;
+    public TResult DistanceTo<TResult>(Point2D<T> point) where TResult : INumber<TResult>;
+    public TResult DistanceSquared<TResult>(Point2D<T> point) where TResult : INumber<TResult>;
+    public bool Intersects(Segment2D<T> other);
+}
+
+public readonly record struct Rect2D<T> where T : INumber<T>
+{
+    // 非負の寸法と有限な最大辺を要求。桁あふれは OverflowException。
+    public Rect2D(T x, T y, T width, T height);
+    public static Rect2D<T> FromMinMax(Point2D<T> min, Point2D<T> max);
+    public static Rect2D<T> FromCenterSize(Point2D<T> center, T width, T height);
+    public static Rect2D<T> FromPoints(ReadOnlySpan<Point2D<T>> points);
+    public static bool TryFromPoints(ReadOnlySpan<Point2D<T>> points, out Rect2D<T> result);
+    public static bool TryCreate(T x, T y, T width, T height, out Rect2D<T> result);
+    public bool Contains(Point2D<T> point);
+    public bool ContainsHalfOpen(Point2D<T> point);
+    public Rect2D<T> Offset(T x, T y);
+    public Rect2D<T> OffsetInverse(T x, T y);
+    public Rect2D<T> Outset(T x, T y);
+    public Rect2D<T> Union(Rect2D<T> other);
+    public bool TryIntersect(Rect2D<T> other, out Rect2D<T> result);
+    public Rect2D<TTo> ConvertChecked<TTo>() where TTo : INumber<TTo>;
+}
+
+public readonly record struct Bounds2D<T>(T Left, T Top, T Right, T Bottom) where T : INumber<T>
+{
+    public static Bounds2D<T> Empty { get; }
+    public static Bounds2D<T> Unbounded { get; }
+    public static bool TryGetUnbounded(out Bounds2D<T> result);
+    public static Bounds2D<T> FromOriginSize(T x, T y, T width, T height);
+    public bool Contains(T x, T y);
+    public Bounds2D<T> Intersect(Bounds2D<T> other);
+    public Bounds2D<T> Union(Bounds2D<T> other);
+}
+
+public static partial class Geometry2D
+{
+    // amount は有限な [0, 1]。Cubic・平坦化にも保存型と結果型を分ける overload を持つ。
+    public static Point2D<TResult> EvaluateQuadraticBezier<T, TResult>(Point2D<T> start,
+        Point2D<T> control, Point2D<T> end, TResult amount)
+        where T : INumber<T> where TResult : INumber<TResult>;
+    public static bool ContainsPolygonClosed<T>(ReadOnlySpan<Point2D<T>> vertices, Point2D<T> point)
+        where T : INumber<T>;
+    // IEEE は FMA、非 IEEE は損失のない係数変換と checked 算術。
+    public static Point2D<TResult> TransformAffineFused<T, TResult>(Point2D<T> point, Matrix3x2 transform)
+        where T : INumber<T> where TResult : INumber<TResult>;
+    public static Point2D<T> TransformAffineChecked<T>(Point2D<T> point,
+        T m11, T m12, T m21, T m22, T m31, T m32) where T : INumber<T>;
+    // raw edge の半開包含。整数の範囲検査と空境界の扱いは各操作名で区別する。
+    public static bool ContainsHalfOpenEdges<T>((T Left, T Top, T Right, T Bottom) edges,
+        T pointX, T pointY) where T : INumber<T>;
+    public static void GetTransformedBoundsBcl(Vector2 minimum, Vector2 maximum, Matrix3x2 transform,
+        out Vector2 transformedMinimum, out Vector2 transformedMaximum);
+    // 論理的な点の受け取り口。struct sink は ref と制約付き呼び出しで boxing しない。
+    public interface IStreamingPointSink { void Append(Vector2 point); }
+    public static void AppendFlattenedQuadraticBcl<TSink>(Vector2 start, Vector2 control, Vector2 end,
+        float tolerance, ref TSink sink, int maxDepth) where TSink : struct, IStreamingPointSink;
+    public static void SquaredDistancesBcl(ReadOnlySpan<float> x, ReadOnlySpan<float> y,
+        float pointX, float pointY, Span<float> destination);
+    public static void SquaredDistancesBcl(ReadOnlySpan<double> x, ReadOnlySpan<double> y,
+        double pointX, double pointY, Span<double> destination);
+}
+
+public static class Geometry3D
+{
+    public static TResult Distance<T, TResult>(Point3D<T> first, Point3D<T> second)
+        where T : INumber<T> where TResult : INumber<TResult>;
+    public static TResult DistanceSquared<T, TResult>(Point3D<T> first, Point3D<T> second)
+        where T : INumber<T> where TResult : INumber<TResult>;
+    public static Point3D<TResult> ClosestPointOnSegment<T, TResult>(Segment3D<T> segment, Point3D<T> point)
+        where T : INumber<T> where TResult : INumber<TResult>;
+    public static AxisAlignedBox3D<T> GetBounds<T>(ReadOnlySpan<Point3D<T>> points) where T : INumber<T>;
+}
```

箱は `AxisAlignedBox3D<T>`、有限形状は `Circle2D<T>`・`Sphere3D<T>`・`Ellipse2D<T>`・`RoundedRect2D<T>` とする。箱にも min/max・center/size・点列の構築、Union・TryIntersect を提供する。`Geometry2D.ContainsPolygonClosed`・`ContainsPolygonEvenOddClosed` は順序付き頂点列を閉輪郭として扱い、境界を含め、空輪郭は false とする。点列からの境界構築は凸包構築ではない。

## 検討した代替案

- **精度別の形状型や整数矩形を別系列にする:** 型ごとの最適化は容易だが、同じ形状と計算の宣言が重複する。形状は T、分数が必要な結果は TResult で分ける。
- **すべて float または double で計算する:** BCL に合わせやすいが、decimal の末尾桁や巨大な整数の差を失う。数値型の意味を保持し、型別の高速経路だけを併設する。
- **保存型と結果型を常に一致させる:** 宣言は短いが、整数の距離・射影・補間を切り捨てるか拒否するしかない。同型呼び出しを残し、明示結果型も選べるようにする。
- **次元別の名前空間へ分け、全境界型を置換する:** 個別の型名は短くなるが、両次元の混在と単位・検証・ABI の違いが見えにくい。共通名前空間と次元接尾辞を選び、必要な境界型は薄く残す。

## 結果と影響

利用者は次元・保存型・結果型を独立して選び、描画依存のない共通幾何を使える。既存機能の呼び出しは維持する一方、新しい型と描画・OS・通信境界の間では明示変換が必要になる。厳密な中間計算とジェネリック化にはコード量・計算量の負担があり、SIMD の採用だけで全処理の高速化は保証しない。

受け入れ条件は、各整数型・BigInteger・decimal・IEEE 型と参照を含む独自 INumber 型での構築、包含・交差・射影・結果型選択を検証することである。境界接触、退化形状、空・無限境界、NaN、正負ゼロの等値とハッシュ、checked overflow、非整数結果の拒否、明示変換の丸めも確認する。BigInteger は double の範囲を超える座標、decimal は末尾桁と平方根、異なる結果型は変換前の座標差、独自型は演算能力の不足を検証する。

共通化では、既存の描画・UI・window・text・整数 pixel の規則が変わらず、数学への依存が一方向であることを確認する。公開型とその引数・戻り値・generic 制約・nested 型を再帰的に調べ、内部の実装分割が公開されず、別 assembly の通常参照だけで共通操作を利用できることを確認する。一括処理は通常・intrinsic 無効・部分対応 hardware の各経路で scalar と照合し、長さ・重複・端数・空入力・割り当てと workload ごとの効果を検証する。これらは受け入れ条件であり、採用状態そのものが実装・検証完了を意味するものではない。
