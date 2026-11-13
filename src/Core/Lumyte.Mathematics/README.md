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

## 色

`Lumyte.Mathematics.Colors.SrgbRgba<T>` は `INumber<T>` の有限な RGBA 成分を保持します。Srgb/Linear の型名で色空間を明示し、数値型から alpha association やコード値の上限を推測しません。transfer は ToLinear / ToSrgb、linear の alpha・合成・行列・明度演算は LinearRgba のみに置き、encoded 補間と HSL/HSV は SrgbRgba に置きます。複数画素の span 変換と既存 UI の raw 互換処理は `Color` に置きます。

```csharp
using Lumyte.Mathematics.Colors;

var input = new SrgbRgba<float>(0.5f, 0.5f, 0.5f, 0.5f);
LinearRgba<float> premultiplied = input.ToLinear<float>().PremultiplyLinear<float>();
SrgbRgba<float> encoded = premultiplied.UnpremultiplyLinear<float>().ToSrgb<float>();
SrgbRgba<byte> codes = SrgbRgba<byte>.FromArgb32Codes(0x80FF0000u);
SrgbRgba<double> normalized = codes.NormalizeUnorm<double>(byte.MaxValue);
SrgbBgra<float> pixel = SrgbBgra<float>.DecodePixel(
    new byte[] { 0, 0, 255, 128 }, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
SrgbRgba<float> rgba = pixel.ToRgba();
var grayCodes = new SrgbGray<byte>(128); // raw 128。自動で128/255にはしない。
SrgbRgba<double> gray = grayCodes.NormalizeUnorm<double>(255); // RGB=128/255、A=1。
SrgbRgba<byte> opaqueCodes = grayCodes.ToRgba((byte)255); // alphaもrawコードを明示。
var packed = RawRgb565.FromSrgb(new SrgbRgba<float>(0, 1, 1, 1));
ushort bits = packed.PackedValue;
SrgbRgba<double> cyan = packed.InterpretSrgb<double>();
LinearRgba<double> sameBitsLinear = packed.InterpretLinear<double>();
SrgbHsla<double> model = cyan.ToHslSrgbDegrees<double>();
SrgbRgba<double> restored = model.ToRgba<double>();
var typedPixels = new SrgbBgra<float>[1];
Lumyte.Mathematics.Colors.Color.DecodePixels<SrgbBgra<float>>(
    new byte[] { 0, 0, 255, 128 }, typedPixels,
    ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
```

成分配置は `SrgbGray<T>`・`SrgbGrayAlpha<T>`・`SrgbRgb<T>`・`SrgbRgba<T>`・`SrgbBgr<T>`・`SrgbBgra<T>`・`SrgbArgb<T>`・`SrgbAbgr<T>` の別の値型です。有限な `INumber<T>` を保存し、BigIntegerや参照を含む独自型も扱います。各配置には LinearGray / LinearGrayAlpha / LinearRgb / LinearRgba / LinearBgr / LinearBgra / LinearArgb / LinearAbgr もあります。同じspaceの配置変換とUNORM正規化はtransferしません。ToLinear / ToSrgb は反対spaceの同じ配置を返し、暗黙のspace変換はありません。型名はバイナリのblittable配置を保証しません。alphaのないraw値からRGBAへのコピーは明示alphaが必要です。`DecodePixel` は明示した数値符号化を正規化した配置値へ読み、`WritePixel` はその逆です。`ColorComponentEncoding` は数値符号化だけを指定し、layout enumは公開しません。

27 種の格納形式は、RGB/BGR/RGBA/BGRA/ARGB/ABGR、8/16-bit UNORM、Gray/GrayAlpha、565/4444/5551/1010102 packed、8/16-bit SNORM、Half/float/double の RGB/RGBA 成分列です。`ColorByteOrder` は成分または packed word の byte 順を指定し、GPU texture format の対応とは別です。decode はコード値を正規化しますが、gamma と alpha association は変更しません。`FromArgb32Codes` の整数 bit 順は 0xAARRGGBB で、こちらは正規化しないコード値の factory です。

packed は `RawRgb565`・`RawRgba4444`・`RawRgba5551`・`RawRgba1010102`・`RawBgra1010102` の具体的な値型です。`Read` / `Write` は整数語のbitを保持し、`FromSrgb / FromLinear` と `InterpretSrgb<T> / InterpretLinear<T>` がspaceを明示する正規化RGBAとの境界です。transferやalpha associationは行いません。raw packed batchは `Color.ReadPackedPixels` / `WritePackedPixels`、正規化された成分配置batchは `DecodePixels` / `WritePixels` と区別します。

`SrgbHsla<T>` / `SrgbHsva<T>` はhue/S/LまたはV/alphaを保持する値型です。HSL/HSV は正規化された straight sRGB に対する degree hue の変換です。hue は有限な値を周期化し、無彩色は hue=0 とします。分数を整数結果へ黙って切り捨てません。UNORM は厳密な比・積と最近接偶数で変換し、型 cast と区別します。

一画素の `WritePixel` は失敗時に出力を変更しません。batch は長さ・enum・全重複を出力前に検査し、データ変換失敗では完了した prefix のみ更新します。RGB への書き出しは alpha=1、Gray は R=G=B を要求し、透明度や色を黙って捨てません。HDR・負の linear RGB を保つ演算と、RGB も clamp する演算を名前で区別します。ICC/profile 変換、画像 codec、GPU capability の追加は含みません。

## 標準色パレット

`CssColors.Red`・`CssColors.CornflowerBlue`・`CssColors.RebeccaPurple` などは、CSS Color 4 の148 named colorを straight-alpha sRGB の `SrgbRgba8` コード値として返します。opaque alphaは255です。構造体はC#のliteral constにできないため、各色は immutable な `static readonly` fieldです。意味付きの `SrgbRgba8` だけは `SrgbRgba<float>` へ暗黙に各 byte /255f で正規化します。gamma decode・premultiply は行いません。一般の `SrgbRgba<T>` の raw 数値規則は変えません。

```csharp
using Lumyte.Mathematics.Colors;

SrgbRgba<byte> redCodes = CssColors.Red.ToCodes(); // (255,0,0,255)
SrgbRgba<float> red = CssColors.Red; // (1,0,0,1)
SrgbBgra<float> bgra = SrgbBgra<float>.FromRgba(red);
var orange = new SrgbRgba8(255, 128, 0); // alpha省略時は255。defaultは透明黒。
SrgbRgba<float> raw = CssColors.Red.ToCodes().ConvertChecked<float>(); // 255は255f。
SrgbRgba<double> unit = CssColors.Red.ToNormalized<double>();
SrgbRgba8 quantized = (SrgbRgba8)red; // unit検証、最近接偶数量子化。HDRをclampしない。
bool found = CssColors.TryGet("cornflowerblue", out SrgbRgba8 codes);
```

`CssColors.All` は148色と別keywordの `transparent` を含む変更不能な149要素の辞書です。gray/greyなどの別名を保持し、CSSのGreenは#008000、Limeは#00FF00です。Transparentは白ではなくRGBA=(0,0,0,0)です。`TryGet` はASCIIの名前をcase-insensitiveに照合するだけで、空白除去・hex/関数parse・currentColor/system/theme色の解決はしません。

値は [W3C CSSWG Color 4の固定ソース](https://github.com/w3c/csswg-drafts/blob/8e3693ea9baa53f5411eb18d3b18201e8c15fb14/css-color-4/Overview.bs#L1633) に照合し、[color-nameの固定データ](https://github.com/colorjs/color-name/blob/9ec3195524951f58bc1d33797fccc0f8ecfe070c/index.js) と全148色を相互確認しています。どちらもruntime依存にはしません。
