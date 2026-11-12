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

`Lumyte.Mathematics.Geometry` は `Half`・`float`・`double` の点、線分、矩形・箱、円・球、楕円・一様半径の角丸矩形を提供します。値は有限で、寸法は非負です。`Contains` は境界を含み、矩形の `ContainsHalfOpen` は右・下を除きます。距離・射影は選択した精度を保持し、表現できない結果は `OverflowException` です。向きと交差は浮動小数点の判定で、厳密述語ではありません。

```csharp
using Lumyte.Mathematics.Geometry;

var segment = new Segment2D<double>(new(0, 0), new(10, 0));
double distance = segment.DistanceTo(new(5, 3)); // 3
var circle = new Circle2D<double>(new(0, 0), 5);
bool contains = circle.Contains(new(3, 4)); // true
```

`Bezier2D` は曲線の評価と、許容誤差・最大深さ・最大点数を明示する平坦化を提供します。予算超過時は `false` を返し、出力リストを変更しません。`Polygon2D` は閉輪郭の nonzero winding／even-odd 判定です。`NumericsGeometry.GetTransformedBounds` は BCL の `Vector2` と `Matrix3x2` に委譲する割り当て不要の float 境界計算で、BCL と同じ非有限値の挙動を保持します。汎用ベクトル・行列・Quaternion は再実装しません。描画の scissor、stroke、GPU coverage の方針は描画側に残します。

`Bounds2D<T>` と `CoordinateGeometry` は半開の raw edge、空・逆転・無限境界を必要とする UI／window／text のアダプター向けです。`IntegerRect2D<T>` は checked 整数 pixel 矩形の保存・移動・拡張・union を共有します。`DrawingGeometry` は BCL float 演算順を保つ streaming 曲線と距離・境界縮約、`AffineGeometry2D`／`WindingGeometry2D` は FMA による座標変換と半開 Y 遷移を提供します。これらは検証付き閉形状とは異なる契約を明示し、描画・入力・OS・wire の単位や予算は利用側に残します。
