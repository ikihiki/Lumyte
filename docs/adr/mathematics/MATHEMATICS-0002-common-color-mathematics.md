# ADR-MATHEMATICS-0002: 色の数値表現と共通計算を描画から独立させる

- 状態: 提案
- 日付: 2026-11-13
- 基準コミット: `ef61258f27ec0f19240a5333d4388f1a3ac9de50`

## 背景

利用者は、UI の色補間、描画の paint、clear、画像 effect で同じ色変換を利用したいが、sRGB の変換と alpha 処理が各機能へ分散している。各値には数値型だけでなく、符号化、alpha の表現、値域の違いがあり、同じ RGBA として無条件に統合すると透明色や HDR の結果を変えてしまう。

基準コードの `src/Graphics/Lumyte.Graphics.Drawing2D/Model/Color2D.cs` の `Color2D` は有限な [0,1] の straight-alpha sRGB である。同プロジェクトの `Rendering/DrawingProgram2D.cs` の `Linear` は float で decode・premultiply し、`Execution/PreparedDraw2D.cs` の `Linear` は clear 用に double で decode する。`src/Graphics/Lumyte.Graphics.Drawing2D.RenderGraph/RenderGraphRenderer2D.cs` と `src/UI/Lumyte.UI/UiAnimationInterpolators.cs` にも同系統の変換がある。

[ADR-MATHEMATICS-0001 の共通計算と利用側の責務](MATHEMATICS-0001-generic-geometry.md#共通計算と利用側の責務)の依存方向を色にも適用する。[ADR-GRAPHICS-0018 の色の契約](../graphics/GRAPHICS-0018-2d-graphics-engine.md#座標clip色を共通化する)と [ADR-GRAPHICS-0017 の詳細契約](../graphics/GRAPHICS-0017-image-effects.md#詳細契約)の描画結果・値域・alpha 規則は維持し、共通数学への委譲を独立して追加する。

## 決定

既存パッケージ `Lumyte.Mathematics` の `Lumyte.Mathematics.Colors` に、数値成分を保持する `Rgba<T>` と公開入口 `Color` を追加する。色空間・alpha・量子化の意味をメソッド名と引数で明示し、描画・UI・text が共有できる CPU 計算を移す。実装の精度別・SIMD 別の分割は公開クラスとして露出しない。

### 利用者の変更前・変更後

同じ sRGB 色を linear premultiplied に変換する。変更前は基準コードの `DrawingProgram2D.Linear` と同じ float 計算を利用側でも書く必要があり、下記はその抜粋である。

```csharp
using System;
using System.Numerics;
using Lumyte.Graphics.Drawing2D;

var input = new Color2D(0.5f, 0.5f, 0.5f, 0.5f);
static float Decode(float c) => c <= 0.04045f
    ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
var output = new Vector4(Decode(input.R) * input.A,
    Decode(input.G) * input.A, Decode(input.B) * input.A, input.A);
// RGB は約 0.10702057、alpha は 0.5。
```

変更後は、今回追加する共通 API に符号化と alpha の操作を明示する。以下は提案する呼び出しであり、基準コードに既存の API ではない。

```csharp
using Lumyte.Mathematics.Colors;

var input = new Rgba<float>(0.5f, 0.5f, 0.5f, 0.5f);
Rgba<float> straight = Color.DecodeSrgb<float, float>(input);
Rgba<float> output = Color.PremultiplyLinear<float, float>(straight);

Rgba<byte> codes = Color.FromArgb32(0x80FF0000u);
Rgba<double> normalized = Color.NormalizeUnorm<byte, double>(codes, byte.MaxValue);
// A は 128/255、R は 1。これは数値 cast でも sRGB decode でもない。
```

既存の `Color2D`、clear、UI animation の公開呼び出しは維持する。利用側の薄い変換で共通 API を使い、単に private 関数の名前を置き換えるだけでなく、その本体の重複を取り除く。

### 数値成分、符号化、値域

`Rgba<T>` の制約は順序付き実数の `INumber<T>` とし、unmanaged は要求しない。整数・BigInteger・decimal・IEEE 浮動小数点と契約を満たす独自型で保存でき、成分は有限値を要求する。値型自体は色空間や UNORM の上限を推測せず、[0,1]、[0,255]、HDR を同じ数値規則へ押し込まない。alpha も保存時には有限性だけを要求し、操作ごとの意味で検証する。

正規化された sRGB 操作では RGB・alpha を [0,1] とする。linear の通常の色操作では RGB の負値と 1 超を許し、alpha だけを [0,1] とする。straight／premultiplied のどちらであるかは型引数 T から推測しない。`Rgba<T>` は表現のタグを持たないため、利用者は呼ぶ操作の事前条件を満たす責務を持つ。これを曖昧な汎用 `ConvertColor` や暗黙変換では隠さない。

- `NormalizeUnorm` は正の整数最大コード値を引数に取り、全成分が [0,max] であることを検証する。コード値と上限の厳密な比を作ってから結果型へ一度で丸め、上限を先に float/double 化しない。整数結果型で比を表せなければ拒否する。`QuantizeUnorm` は単位区間を検証し、入力数値の厳密な値と整数上限の数学的積を最近接偶数へ丸める。IEEE 入力はその二進有理数、decimal は十進有理数として扱い、独自型は decimal/BigInteger との忠実な往復変換を確認できなければ `ArithmeticException` とする。両方向とも端点 0↔0、1↔maximum を保証し、ulong 最大値・巨大 BigInteger 上限でも中間の浮動小数点化による overflow を起こさない。符号付き整数の負値や範囲外を黙って clamp しない。
- `FromArgb32`／`ToArgb32` は整数の bit 位置を `0xAARRGGBB` と定める。RGBA／BGRA メモリ列とは区別し、ホストの endian で意味を変えない。`FromRgba8` は 8-bit のコード値を保持する構築である。
- 保存の型変換 `ConvertChecked<TTo>` は BCL の `CreateChecked` の範囲検査であり、UNORM の再スケール、gamma 変換、alpha 変換、無損失変換ではない。整数に表せない分数を要する数学操作には結果型を選び、整数除算で黙って切り捨てない。
- sRGB の非整数累乗は明示した IEEE 結果型で近似計算する。decimal や巨大整数を保存できることを、任意精度の gamma 計算ができる保証にしない。結果型への変換誤差も契約に含める。

正規化や算術で表せない整数結果は `ArithmeticException`、checked overflow・有限な数学結果を表現できない場合は `OverflowException` とする。入力の非有限値・範囲外は `ArgumentOutOfRangeException`。組み込み型の default は透明黒として有効であり、独自数値型の default はその型の規則に従う。等値・ハッシュは成分の数値等値で、正負ゼロを等しく扱い、近似許容誤差を混ぜない。

### transfer、alpha、補間

sRGB decode は `c <= 0.04045` で `c / 12.92`、それ以外で `((c + 0.055) / 1.055)^2.4`。encode は `c <= 0.0031308` で `12.92 * c`、それ以外で `1.055 * c^(1/2.4) - 0.055` とする。alpha に transfer 関数を適用しない。通常の encode は RGB が単位区間であることを検証し、HDR の clamp・露出・tone mapping を暗黙に行わない。必要な clamp は明示操作とし、SceneLinear をそのまま表示色へ変換しない。

float paint は float、clear は float 入力の閾値比較と double の係数・累乗を保持する。UI は単に全体を double 化せず、alpha を `from.A + ((to.A - from.A) * (double)amount)` の順、すなわち差だけ float で計算してから double に昇格する。RGB の decode・加重計算・除算・encode は既存の double 順序、最後の成分変換は float とする。例えば alpha 0.1f→0.9f の中点は既存の 0.49999997f を保持し、一律 double 減算の 0.5f に変えない。GPU の浮動小数点・step・pow と CPU の結果を bit 単位で同一とする保証はせず、対応する数値・画素の基準で比較する。

`PremultiplyLinear` は RGB に alpha を一度掛ける。`UnpremultiplyLinear` は alpha>0 のときだけ除算し、alpha=0 では RGB をゼロへ正規化する。保存型は alpha=0 の隠れた RGB を保持できるが、associate／unassociate の往復でそれを復元する保証はしない。HDR の premultiplied RGB を RGB<=alpha に制限しない。

sRGB の色補間は decode → premultiply → linear RGBA 補間 → unpremultiply → encode とする。新しい検証付き API は有限な amount と正規化入力を検証し、外挿を一律 clamp せず、結果が正規化 sRGB を表せない場合は拒否する。端点は型変換を除いて元の成分を保持し、内部の alpha==0 は透明黒とする。非ゼロの定数・subnormal・透明色で、不要な中間丸めによる成分消失を検証する。

既存 UI はこの検証付き pipeline に無条件に通さない。互換用の `InterpolateSrgbLinearPremultipliedRaw` は Vector4 の float 入出力で上記 mixed-precision 式と端点・ゼロ alpha 分岐を評価し、入力・中間値の unit 検証をせず、最後の float 化までを共通層へ集約する。UI adapter は最終的な `Color2D` 構築だけで範囲を検証する。黒色の alpha 0.9f→1f、amount=1.0000001192092896f では中間 double alpha が 1 を超えても最終 float は 1f となる既存挙動を保持し、新 API の中間 unit 検証で拒否しない。raw 経路の非有限値は IEEE のまま伝播し、既存入口での例外を保持する。

### 合成、画像 effect、利用側の境界

共通合成は linear premultiplied を入力とする。`SourceOverLinearPremultiplied` は RGB・alpha とも `source + destination * (1 - sourceAlpha)`。Plus は契約差を名前に表し、`AddLinearPremultiplied` は HDR RGB を保持して alpha のみ clamp、`AddUnitLinearPremultiplied` は RGB・alpha を単位区間へ clamp する。Color2D の通常描画と HDR effect のどちらかへ一律に合わせない。

4×5 色行列は、alpha>0 で unpremultiply・その他 RGB=0 → straight linear RGBA へ行列と定数項 → alpha のみ clamp → premultiply の順を保持する。CPU の線形演算は BCL の `Vector4`・`Matrix4x4` と定数 `Vector4` を使い、新しい汎用ベクトル・行列を作らない。BCL の row-vector と既存 4×5 row-major の対応は adapter で明示する。行列係数が有限でも GPU の全出力画素が有限になる保証は追加しない。

共通数学は描画・UI・OS・画像 codec を参照しない。既存の境界は次のように保持する。

- `Color2D` の constructor・init・with の有限 [0,1] 検証、default・deconstruction を維持し、値は正規化された straight sRGB として渡す。clear の raw `ClearColor` と `BlendConstant` の GPU API 境界には同じ unit 制約を付けない。
- `ModelMaterial` の linear な base/emissive、`ColorDrawArguments.Tint` の linear multiplier、`ModelVertex` の固定 shader layout は維持する。HDR は storage の形式と presentation の能力を区別する。
- gradient stop は非減少順で、重複・範囲外 offset、empty、Pad/Repeat/Reflect を保持する。画像は各 tap の decode と必要な premultiply の後に補間し、premultiplied 入力へ alpha を再乗算しない。
- native font palette は sRGB コード、foreground は paint を指す規則を維持する。currentColor を固定 RGBA へ潰さない。interop 配置と生成コードは数学型へ置換しない。
- sRGB attachment の store encode と presentation shader の encode を二重適用しない。texture format ID、channel 順、bytes/texel、readback・PNG、MTSDF の距離 alpha はそれぞれの境界に残す。
- GPU の compositing・filter・色行列を CPU へ移して読み戻すことはしない。GPU の式と CPU の共通数学は意味を揃えて検証し、実行環境ごとの実装を維持する。

### 公開 API 差分

以下は基準コードに対する追加の主要宣言の抜粋であり、既存の型・シグネチャは削除しない。部品の保存・構築と一般の計算を分け、公開 utility は `Color` に集約する。

```diff
+using System.Numerics;
+
+namespace Lumyte.Mathematics.Colors;
+
+public readonly record struct Rgba<T> where T : INumber<T>
+{
+    // 全成分 finite。単位・色空間・alpha 表現は推測しない。
+    public Rgba(T r, T g, T b, T a);
+    public T R { get; }
+    public T G { get; }
+    public T B { get; }
+    public T A { get; }
+    public static bool TryCreate(T r, T g, T b, T a, out Rgba<T> result);
+    // 成分順 RGBA、入力は正確に 4、出力は 4 要素以上。
+    public static Rgba<T> FromComponents(ReadOnlySpan<T> values);
+    public void CopyTo(Span<T> destination);
+    public Rgba<TTo> ConvertChecked<TTo>() where TTo : INumber<TTo>;
+}
+
+public static partial class Color
+{
+    public static Rgba<byte> FromRgba8(byte r, byte g, byte b, byte a);
+    public static Rgba<byte> FromArgb32(uint argb);
+    public static uint ToArgb32(Rgba<byte> codes);
+    public static Rgba<TResult> NormalizeUnorm<TCode, TResult>(Rgba<TCode> codes, TCode maximum)
+        where TCode : IBinaryInteger<TCode> where TResult : INumber<TResult>;
+    // 厳密な入力値×整数上限を最近接偶数へ。0/1 端点を保証し、範囲外を clamp しない。
+    public static Rgba<TCode> QuantizeUnorm<T, TCode>(Rgba<T> unit, TCode maximum)
+        where T : INumber<T> where TCode : IBinaryInteger<TCode>;
+    public static Rgba<TResult> DecodeSrgb<T, TResult>(Rgba<T> unitSrgb)
+        where T : INumber<T> where TResult : IFloatingPointIeee754<TResult>;
+    public static Rgba<TResult> EncodeSrgb<T, TResult>(Rgba<T> unitLinear)
+        where T : INumber<T> where TResult : IFloatingPointIeee754<TResult>;
+    public static Rgba<TResult> PremultiplyLinear<T, TResult>(Rgba<T> straight)
+        where T : INumber<T> where TResult : INumber<TResult>;
+    public static Rgba<TResult> UnpremultiplyLinear<T, TResult>(Rgba<T> associated)
+        where T : INumber<T> where TResult : INumber<TResult>;
+    public static Rgba<TResult> InterpolateSrgbLinearPremultiplied<T, TResult>(
+        Rgba<T> from, Rgba<T> to, TResult amount)
+        where T : INumber<T> where TResult : IFloatingPointIeee754<TResult>;
+    // 互換用 mixed-precision。入力・中間 unit 検証なし、最終 float 化後は利用側が検証。
+    public static Vector4 InterpolateSrgbLinearPremultipliedRaw(Vector4 from, Vector4 to, float amount);
+    public static Rgba<T> SourceOverLinearPremultiplied<T>(Rgba<T> source, Rgba<T> destination)
+        where T : INumber<T>;
+    // HDR RGB を保持し alpha のみ clamp。
+    public static Rgba<T> AddLinearPremultiplied<T>(Rgba<T> source, Rgba<T> destination)
+        where T : INumber<T>;
+    // RGB と alpha を clamp。HDR 版とは別操作。
+    public static Rgba<T> AddUnitLinearPremultiplied<T>(Rgba<T> source, Rgba<T> destination)
+        where T : INumber<T>;
+    public static Rgba<float> ApplyMatrixLinearPremultiplied(
+        Rgba<float> input, Matrix4x4 matrix, Vector4 offset);
+}
```

From/Try 構築、明示変換、span はそれぞれ失敗時の出力保証を持つ。TryCreate は無効入力で false・default、CopyTo は短い出力を例外で拒否し書き込まない。数値型を一般化したことと、SIMD で任意の T を直接再解釈できることは別である。SIMD を追加する場合は float・double 等の型別経路と scalar を照合し、長さ・重なり・空入力・末尾、NaN、丸め、tiny batch を含めて測定する。速度・無割り当ての数値目標はこの判断では保証しない。

CSS 全構文、名前付き色一覧、ICC profile・広色域・知覚色差、HSL/HSV の UI 編集、任意精度 gamma は今回の対象外とする。これらを暗黙に sRGB の単純変換として扱わない。

## 検討した代替案

- **描画用 Color2D を全機能へ移す:** 呼び出しは少ないが、float・straight sRGB・unit 制約を HDR・clear・byte palette へ強制する。共通保存と操作を独立させ、既存の境界型は維持する。
- **全色を Vector4 だけで扱う:** BCL の演算は再利用できるが、数値型と UNORM・gamma・alpha の意味が見えない。ベクトル演算は BCL、色の契約は Rgba と明示操作に分ける。
- **色空間・精度・実装ごとに公開 utility を作る:** 実装は分類しやすいが、利用者へ backend 選択を漏らす。一つの Color 入口と意味の明確なメソッドにまとめる。
- **全経路を double や GPU 式へ統一する:** 重複は減るが、float paint・double clear/UI・shader の丸めと閾値が変わる。共有できる数学だけを委譲し、既存の演算経路を検証する。

## 結果と影響

色計算を描画依存なく使い、量子化・色空間・alpha の二重変換を明示的に避けられる。利用者は保存型だけで色の意味が決まらないことを理解し、正規化と cast、表示色と HDR を区別する必要がある。既存 API の意味を維持するための adapter と CPU/GPU の照合は残る。

受け入れ条件は、sRGB の両閾値と隣接値、float/double の元計算との比較、量子化の端値・中点・往復・ulong 最大値・巨大整数上限、負値・NaN・infinity・overflow の拒否を検証することである。byte・各整数型・BigInteger・decimal・IEEE・参照を含む独自数値型の保存と、対応する結果型での操作を区別して確認する。透明な非黒の端点、alpha=0、UI の mixed-precision 中点と最終 float 化後に有効となる外挿、定数・subnormal の補間、HDR・負の RGB、Plus の二つの clamp 規則、行列の定数項を確認する。

実装の共通化では Drawing2D・RenderGraph clear・UI animation・font palette の元データと画素基準を保持し、sRGB attachment、UNORM presentation、画像 tap ごとの alpha、MTSDF の意味を検査する。公開 API に実装 helper が漏れないこと、数学への依存が一方向であることを別の通常 consumer で確認する。文書の採否、実装、CPU の数値一致、実 GPU の画素検証と性能測定はそれぞれ別の事実として扱う。
