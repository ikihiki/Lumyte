# Lumyte.Mathematics

AnimationやCoreに依存しない汎用の数値計算です。`System.Numerics`のベクトル・Quaternionを使用します。

- `IntegerInterpolation.Linear`: floatの正確な二進重みによるlong補間。最寄り整数へ丸め、半分は偶数を選びます。
- `Interpolation`: スカラー・Vector2／3／4の線形補間と二次イージング。
- `BezierInterpolation.Cubic`: 任意の点型のDe Casteljau評価。補間デリゲートは呼び出し側で再利用できます。
- `CubicBezierTiming`: (0, 0)から(1, 1)へのベジェ曲線で、入力xからyを逆算します。
- `HermiteInterpolation.Interpolate`: float／Vector2／Vector3／Vector4／Quaternionの接線補間。intervalは正の有限値で、接線と同じ単位系を使用します。
- `QuaternionInterpolation`: 球面補間と倍精度成分の正規化。

```csharp
using Lumyte.Mathematics;

var curve = new CubicBezierTiming(0.42, 0, 0.58, 1);
double progress = curve.Transform(0.5);
float value = HermiteInterpolation.Interpolate(0f, 10f, 2f, 0f, 3, 0.5f);
```

線形・空間ベジェ・球面補間は外挿できます。Hermiteのamountと時間ベジェの入力は有限の[0, 1]です。点・接線は有限値、回転の端値は単位Quaternionを渡してください。Hermiteは正確な端点で保存値を返し、内部のQuaternionを正規化します。ゼロ長または非有限成分の正規化と、floatへ変換できないHermite成分はInvalidOperationExceptionになります。キー・時計・Duration・状態機械は持ちません。

## 幾何

`Lumyte.Mathematics.Geometry` は `INumber<T>` による整数・BigInteger・decimal・IEEE 浮動小数点・互換な独自実数型の点、線分、矩形・箱、円・球、楕円・一様半径の角丸矩形を提供します。値は有限で、寸法は非負です。`Contains` は境界を含み、矩形の `ContainsHalfOpen` は右・下を除きます。距離・射影は選択した精度を保持し、範囲外は `OverflowException`、整数に表せない分数結果は `ArithmeticException`、平方根の方針がない独自結果型は該当 metric で `NotSupportedException` です。IEEE の向きと交差は浮動小数点の判定で厳密述語ではなく、整数・decimal の判定は厳密な有理数演算を使います。

```csharp
using Lumyte.Mathematics.Geometry;

var segment = new Segment2D<double>(new(0, 0), new(10, 0));
double distance = segment.DistanceTo(new(5, 3)); // 3
var circle = new Circle2D<double>(new(0, 0), 5);
bool contains = circle.Contains(new(3, 4)); // true
```

`Geometry2D.EvaluateQuadraticBezier`・`EvaluateCubicBezier` は曲線の評価、`TryFlattenQuadraticBezier`・`TryFlattenCubicBezier` は許容誤差・最大深さ・最大点数を明示する平坦化を提供します。予算超過時は `false` を返し、出力リストを変更しません。`Geometry2D.ContainsPolygonClosed`／`ContainsPolygonEvenOddClosed` は閉輪郭の nonzero winding／even-odd 判定です。`Geometry2D.GetTransformedBoundsBcl` は BCL の `Vector2` と `Matrix3x2` に委譲する割り当て不要の float 境界計算で、BCL と同じ非有限値の挙動を保持します。汎用ベクトル・行列・Quaternion は再実装しません。描画の scissor、stroke、GPU coverage の方針は描画側に残します。

`Bounds2D<T>` と `Geometry2D` の境界操作は半開の raw edge、空・逆転・無限境界を必要とする UI／window／text のアダプター向けです。`Rect2D<T>` は数値型を問わず checked 矩形の保存・移動・拡張・union を共有し、整数専用型は持ちません。`Geometry2D` の Bcl 接尾辞は BCL float 演算順を保つ streaming 曲線と距離・境界縮約、`TransformAffineFused`／`AccumulateWindingFused` は FMA による座標変換と半開 Y 遷移を表します。これらは検証付き閉形状とは異なる契約を明示し、描画・入力・OS・wire の単位や予算は利用側に残します。

## 数値型と結果型

点・線分の `DistanceTo<TResult>`、`DistanceSquared<TResult>`、`ClosestPoint<TResult>` で計算結果型を明示できます。整数に分数の結果が必要な場合は切り捨てず `ArithmeticException` です。decimal は double に狭めません。`ConvertChecked<TTo>` は BCL と同じ範囲検査付き変換で、無損失変換ではありません。

```csharp
var line = new Lumyte.Mathematics.Geometry.Segment2D<int>(new(0, 0), new(1, 1));
var closest = line.ClosestPoint<decimal>(new(1, 0)); // (0.5, 0.5)
```

`FromMinMax`・`FromCenterSize`・`FromPoints` と `TryCreate`・`TryFromPoints`、point の `Origin`・`FromCoordinates`・`CopyTo` を用途に応じて選べます。中心の半寸法や非 IEEE 型の角丸半径に必要な半分を保存型で表現できないときは明示的に失敗します。IEEE の角丸半径は半寸法を超えない表現可能値へ下向きに clamp します。無限大を持たない型では `Bounds2D<T>.TryGetUnbounded` は false です。Complex のような順序なし数値は対象外です。独自型の root は自動推測せず、対応する結果型を選びます。

`Geometry2D.SquaredDistancesBcl` は float／double の別々の座標 span を一括計算します。Vector256／Vector128 を使い、未対応環境では scalar に戻ります。shift した重複は出力前に拒否し、同一開始の in-place を許します。少数の点では scalar 呼び出しの方が適する場合があります。

## 共通計算の入口

公開 utility は `Geometry2D` と `Geometry3D` です。実装ファイルの分割を呼び出し側に公開しません。形状固有の操作は各値型に残し、境界を含む polygon、半開 bounds、BCL float、FMA と checked affine をメソッド名で区別します。streaming には `Geometry2D.IStreamingPointSink` を実装した struct を ref で渡せます。

```csharp
using Lumyte.Mathematics.Geometry;

var point = Geometry2D.EvaluateQuadraticBezier<int, decimal>(
    new(0, 0), new(0, 1), new(1, 0), 0.5m);
double distance = Geometry3D.Distance<int, double>(new(0, 0, 0), new(2, 3, 6));
```

通常の別 assembly から公開境界を使う実行例は [Mathematics sample](../../../samples/Lumyte.Mathematics.Sample/README.md) にあります。

囲む矩形を構築する `FromMinMax`・`FromPoints`・`Union` は、丸めた extent を足し戻した最大辺も入力を含むよう必要時だけ外向きに補正します。有限に表現できない場合は例外／Try factory の false とし、無条件の余白は付けません。汎用 Bezier では定数成分と subnormal 成分を早期の積の underflow で失わないようにします。
