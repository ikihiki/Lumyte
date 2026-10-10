# Lumyte.Mathematics

AnimationやCoreに依存しない汎用の数値計算です。`System.Numerics`のベクトル・Quaternionを使用します。

- `IntegerInterpolation.Linear`: floatの正確な二進重みによるlong補間。最寄り整数へ丸め、半分は偶数を選びます。
- `Interpolation`: スカラー線形補間と二次イージング。
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
