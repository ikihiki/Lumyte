# ADR-MATHEMATICS-0002: 色の数値表現と共通計算を描画から独立させる

- 状態: 採用
- 日付: 2026-11-13
- 基準コミット: `ef61258f27ec0f19240a5333d4388f1a3ac9de50`

## 背景

利用者は、UI の色補間、描画の paint、clear、画像 effect で同じ色変換を利用したいが、sRGB の変換と alpha 処理が各機能へ分散している。各値には数値型だけでなく、符号化、alpha の表現、値域の違いがあり、同じ RGBA として無条件に統合すると透明色や HDR の結果を変えてしまう。

基準コードの `src/Graphics/Lumyte.Graphics.Drawing2D/Model/Color2D.cs` の `Color2D` は有限な [0,1] の straight-alpha sRGB である。同プロジェクトの `Rendering/DrawingProgram2D.cs` の `Linear` は float で decode・premultiply し、`Execution/PreparedDraw2D.cs` の `Linear` は clear 用に double で decode する。`src/Graphics/Lumyte.Graphics.Drawing2D.RenderGraph/RenderGraphRenderer2D.cs` と `src/UI/Lumyte.UI/UiAnimationInterpolators.cs` にも同系統の変換がある。

[ADR-MATHEMATICS-0001 の共通計算と利用側の責務](MATHEMATICS-0001-generic-geometry.md#共通計算と利用側の責務)の依存方向を色にも適用する。[ADR-GRAPHICS-0018 の色の契約](../graphics/GRAPHICS-0018-2d-graphics-engine.md#座標clip色を共通化する)と [ADR-GRAPHICS-0017 の詳細契約](../graphics/GRAPHICS-0017-image-effects.md#詳細契約)の描画結果・値域・alpha 規則は維持し、共通数学への委譲を独立して追加する。

## 決定

既存パッケージ `Lumyte.Mathematics` の `Lumyte.Mathematics.Colors` に、成分配置を表す `Gray<T>`・`Rgb<T>`・`Rgba<T>` などと packed・HSL/HSV の値型を追加する。一色の構築・配置変換は各型に置き、transfer・alpha・合成・補間の共通演算は `Rgba<T>` の static factory と instance method に集約する。`Color` は複数画素の span 操作と既存 UI の raw 互換処理だけを担当する。色空間・alpha・量子化の意味はメソッド名と引数で明示し、実装の精度別・SIMD 別の分割は公開クラスとして露出しない。

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

成分配置も型で保持する。基準コードでは灰色 byte コードと正規化色を区別する共通値型がなく、利用側で配置・正規化・opaque alpha を管理する。例えば同じ灰色を描画用の値へ渡す記述は次の通りである。

```csharp
using Lumyte.Graphics.Drawing2D;

byte grayCode = 128;
var grayColor = new Color2D(grayCode / 255f, grayCode / 255f, grayCode / 255f, 1);
```

変更後は、今回追加する共通 API に符号化と alpha の操作を明示する。以下は提案する呼び出しであり、基準コードに既存の API ではない。

```csharp
using Lumyte.Mathematics.Colors;

var input = new Rgba<float>(0.5f, 0.5f, 0.5f, 0.5f);
Rgba<float> straight = input.DecodeSrgb<float>();
Rgba<float> output = straight.PremultiplyLinear<float>();

Rgba<byte> codes = Rgba<byte>.FromArgb32Codes(0x80FF0000u);
Rgba<double> normalized = codes.NormalizeUnorm<double>(byte.MaxValue);
// A は 128/255、R は 1。これは数値 cast でも sRGB decode でもない。
Bgra<float> pixel = Bgra<float>.DecodePixel(
    new byte[] { 0, 0, 255, 128 }, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
// BGRA byte 列を正規化 RGBA へ。R=1、A≈0.502。gamma と alpha association は変更しない。
Rgba<double> cyan = new Hsva<double>(180d, 1d, 1d, 1d).ToRgba<double>();
var grayCodes = new Gray<byte>(128);
Rgba<float> gray = grayCodes.NormalizeUnorm<float>(255);
var opaqueCodes = grayCodes.ToRgba((byte)255);
Rgb565 packed = Rgb565.FromNormalized(cyan);
```

既存の `Color2D`、clear、UI animation の公開呼び出しは維持する。利用側の薄い変換で共通 API を使い、単に private 関数の名前を置き換えるだけでなく、その本体の重複を取り除く。

### 数値成分、符号化、値域

`Rgba<T>` の制約は順序付き実数の `INumber<T>` とし、unmanaged は要求しない。整数・BigInteger・decimal・IEEE 浮動小数点と契約を満たす独自型で保存でき、成分は有限値を要求する。値型自体は色空間や UNORM の上限を推測せず、[0,1]、[0,255]、HDR を同じ数値規則へ押し込まない。alpha も保存時には有限性だけを要求し、操作ごとの意味で検証する。

正規化された sRGB 操作では RGB・alpha を [0,1] とする。linear の通常の色操作では RGB の負値と 1 超を許し、alpha だけを [0,1] とする。straight／premultiplied のどちらであるかは型引数 T から推測しない。`Rgba<T>` は表現のタグを持たないため、利用者は呼ぶ操作の事前条件を満たす責務を持つ。これを曖昧な汎用 `ConvertColor` や暗黙変換では隠さない。

- `NormalizeUnorm` は T で表す正の整数最大コード値を引数に取り、上限と全成分が整数値で全成分が [0,max] であることを検証する。T 自体は INumber なので、float に保存した整数コードにも使えるが、小数コードは拒否する。コード値と上限の厳密な比を作ってから結果型へ一度で丸め、上限を先に float/double 化しない。整数結果型で比を表せなければ拒否する。`QuantizeUnorm` は単位区間を検証し、入力数値の厳密な値と整数上限の数学的積を最近接偶数へ丸める。IEEE 入力はその二進有理数、decimal は十進有理数として扱い、独自型は decimal/BigInteger との忠実な往復変換を確認できなければ `ArithmeticException` とする。両方向とも端点 0↔0、1↔maximum を保証し、ulong 最大値・巨大 BigInteger 上限でも中間の浮動小数点化による overflow を起こさない。符号付き整数の負値や範囲外を黙って clamp しない。
- `FromArgb32Codes`／`ToArgb32Codes` は整数の bit 位置を `0xAARRGGBB` と定める。`FromRgba8Codes` と合わせてコード値をそのまま T に保存し、`Rgba<float>` へ格納しても 255 は 255f のままである。`ToArgb32Codes` は全成分が整数値かつ [0,255] であることを検証し、非整数コードの切り捨てはしない。正規化する `DecodePixel` と名前で区別する。RGBA／BGRA メモリ列・ホストの endian で整数の bit 位置を変えない。
- 保存の型変換 `ConvertChecked<TTo>` は BCL の `CreateChecked` の範囲検査であり、UNORM の再スケール、gamma 変換、alpha 変換、無損失変換ではない。整数に表せない分数を要する数学操作には結果型を選び、整数除算で黙って切り捨てない。
- sRGB の非整数累乗は明示した IEEE 結果型で近似計算する。decimal や巨大整数を保存できることを、任意精度の gamma 計算ができる保証にしない。結果型への変換誤差も契約に含める。

正規化や算術で表せない整数結果は `ArithmeticException`、checked overflow・有限な数学結果を表現できない場合は `OverflowException` とする。入力の非有限値・範囲外は `ArgumentOutOfRangeException`。組み込み型の default は透明黒として有効であり、独自数値型の default はその型の規則に従う。等値・ハッシュは成分の数値等値で、正負ゼロを等しく扱い、近似許容誤差を混ぜない。

### 成分配置・格納形式・色モデル

成分配置は `Gray<T>`・`GrayAlpha<T>`・`Rgb<T>`・`Rgba<T>`・`Bgr<T>`・`Bgra<T>`・`Argb<T>`・`Abgr<T>` の有限値型で表す。各型は `INumber<T>` を要求し unmanaged を要求しない。`Gray<byte>(128)` は raw 数値128であり、保存型から正規化・符号化を推測しない。`ToRgba(alpha)` は alpha のない型で明示 alpha を要求し、コード255と正規化1を勝手に選ばない。`FromRgba` は alpha のない配置で A=1 を、Gray 系では R=G=B を要求し、暗黙に透明度や色を捨てない。`NormalizeUnorm<TResult>(maximum)` は alpha のない配置に正規化後 A=1 を補い、alpha のある配置では全成分を正規化する。raw コードと正規化値の境界はこの名前で区別する。

一画素の `DecodePixel` / `WritePixel` は配置を receiver 型で決め、`ColorComponentEncoding` は配置でなく UNORM8/16・SNORM8/16・IEEE16/32/64 の数値符号化だけを選ぶ。27種類の対応外の配置と符号化の組は `ArgumentException`、未知の符号化・byte 順は `ArgumentOutOfRangeException` とする。独立した packed 値型 `Rgb565`・`Rgba4444`・`Rgba5551`・`Rgba1010102`・`Bgra1010102` はそれぞれ ushort/uint の `PackedValue` を保持する。packed 型は `Read` / `Write` で整数語をそのまま入出力し、`FromNormalized<T>` / `ToNormalized<T>` で正規化 RGBA と変換する。型が bit 配置を指定するため公開 layout enum は不要であり、元の enum API は公開しない。

型付き batch は `IColorPixel<TSelf>` の最小契約（PixelSize・DecodePixel・WritePixel）に従い、`Color.DecodePixels<TPixel>` / `WritePixels<TPixel>` で成分配置型の span を処理する。packed の raw batch は `Color.ReadPackedPixels<TPixel>` / `WritePackedPixels<TPixel>` と `IPackedColorPixel<TSelf>`（ByteSize・Read・Write）で区別する。内部の codec descriptor は公開しない。Graphics の TextureFormat と ID を共有せず、以下の対応を数学側で提供することは GPU texture format の追加や全 backend での使用保証ではない。

| 形式群 | 対応する格納形式 | 正規化と配置 |
| --- | --- | --- |
| 8-bit UNORM | Rgb8・Bgr8・Rgba8・Bgra8・Argb8・Abgr8 | 各成分 /255。名前の順に byte を並べる |
| 16-bit UNORM | Rgb16・Bgr16・Rgba16・Bgra16 | 各成分 /65535。成分内の byte 順は引数で明示 |
| Gray / GrayAlpha UNORM | Gray8・Gray16・GrayAlpha8・GrayAlpha16 | Gray を RGB へ複製、GrayAlpha は gray→alpha の順。数字は各成分の bit 数 |
| 16-bit packed UNORM | Rgb565・Rgba4444・Rgba5551 | Rgb565 は R:15..11/G:10..5/B:4..0、Rgba4444 は R:15..12/G:11..8/B:7..4/A:3..0、Rgba5551 は R:15..11/G:10..6/B:5..1/A:0 |
| 32-bit packed UNORM | Rgba1010102・Bgra1010102 | 前者は R:9..0/G:19..10/B:29..20/A:31..30。後者は R/B を交換 |
| SNORM | Rgba8Snorm・Rgba16Snorm | 符号付き n-bit を max(code/(2^(n-1)-1),-1) へ。alpha も格納成分として同じ写像 |
| IEEE 成分列 | Rgb16Float・Rgba16Float・Rgb32Float・Rgba32Float・Rgb64Float・Rgba64Float | Half・float・double の順列。有限な負値と HDR を保持し、単位区間へ clamp しない |

`DecodePixel` は先頭の一画素を読み、UNORM/SNORM を正規化し、IEEE は成分値を T へ変換する。整数 T で表せない分数は `ArithmeticException`、負の SNORM 値を unsigned T に格納できない場合は `OverflowException` とし、黙って切り捨てない。alpha のない形式は A=1 とする。packed の各成分には各 bit 幅の上限を用い、RGB565 を一律 /255 にしない。成分列ではチャンネル順と成分内の endian を別々に扱い、packed では上記の bit 位置を持つ整数語を指定 endian で読む。

`WritePixel` は逆の格納を行う。UNORM は [0,1]、SNORM は [-1,1] を検証して最近接偶数で量子化し、SNORM の -1 は負の最大対称コード（8-bit は -127）へ正規化する。最小コード -128 も decode は -1 になるため、数値の往復と bit 列の往復は別である。浮動小数点への丸めは対象形式の最近接偶数とし、非有限となる overflow は拒否する。形式から sRGB／linear、alpha association を推測・変換しない。

alpha のない形式への書き出しは A=1 を要求し、透明度を黙って捨てない。必要なら利用者が合成するか `WithAlpha(T.One)` で明示する。Gray への書き出しは R=G=B を要求し、勝手に luminance を計算しない。`ToLinearGrayscale<TResult>` は linear RGB の明度を `0.2126R+0.7152G+0.0722B` と定義し、同じ値を RGB に入れて alpha を保持する。sRGB の灰色を作る場合は decode と encode を明示する。

`Hsla<T>`・`Hsva<T>` は hue/S/LまたはV/A の型付き値を保持し、RGBA からの結果も tuple ではなくこの型で返す。モデル構築でも hue の周期化と unit 検証を行い、色変換の式と最終丸めは共通の厳密演算を再利用する。HSL・HSV は格納 byte 形式と分け、正規化された straight sRGB に対する `FromHslSrgbDegrees`／`FromHsvSrgbDegrees` と `ToHslSrgbDegrees`／`ToHsvSrgbDegrees` を提供する。hue の単位は度で、構築時に有限な hue を [0,360) に周期化する。saturation・lightness/value・alpha は [0,1]。HSL→RGB は C=(1-|2L-1|)S、HSV→RGB は C=VS とし、X=C(1-|(H/60 mod 2)-1|)、sector 順に (C,X,0)/(X,C,0)/(0,C,X)/(0,X,C)/(X,0,C)/(C,0,X) を選ぶ。HSL では L-C/2、HSV では V-C を全 RGB 成分に加える。RGB→HSL/HSV は最大 M、最小 m、差 D を用い、V=M、L=(M+m)/2、HSV の S は M=0 なら 0・他は D/M、HSL の S は D=0 なら 0・他は D/(1-|2L-1|)。D=0 の hue は 0 とし、他は最大成分を R→G→B の優先順で選び、60×((G-B)/D)、60×((B-R)/D+2)、60×((R-G)/D+4) を周期化する。有限 hue の周期化は剰余で行い、360 の加減算を入力値の大きさだけ反復しない。結果への丸めで 360 になれば 0 に戻す。無彩色は hue=0、saturation=0 とする。HDR、linear RGB の物理的な明度、profile に依存する CMYK と混同しない。HSL/HSV の全式は、IEEE 入力も表現された二進有理数、decimal は十進有理数、整数は厳密な整数として評価し、中間の L・chroma・分母を先に丸めない。最終出力の各成分だけを対象の IEEE/decimal 精度へ最近接偶数で丸め、整数結果に表せない分数は `ArithmeticException` とする。独自 INumber の値を decimal/BigInteger との忠実な往復変換で抽出できなければ同じ例外で拒否する。例えば float RGB=(1,1,1-2^-24) の HSL saturation は 1 を保ち、丸めた L=1 によるゼロ除算を起こさない。必要な分数を表せる結果型を選ぶ。

一画素の DecodePixel は必要 byte 数以上の入力の先頭だけを消費する。WritePixel は成分の変換と長さを先に検証し、失敗時は出力を書き換えない。未知の形式・byte 順は `ArgumentOutOfRangeException`、短い span は `ArgumentException`、非有限入力・範囲外は従来の数値例外とする。複数画素操作は destination の画素数（Write は source の画素数）を処理数とし、必要な byte 数・画素数は checked で求める。長さ・checked サイズ計算・enum 値を最初に検査し、source/destination の重複は同じ開始位置を含め全て `ArgumentException` で拒否する。これらの事前検査失敗では全出力を維持する。データ変換に失敗した場合は完了した画素の prefix だけを更新し、失敗画素と未処理 suffix・未使用末尾は変更しない。画像の stride・圧縮・palette・codec はこの一画素形式の契約には含めない。

### 標準の名前付き色

標準色は単一の `CssColors` catalog から `CssColors.Red`・`CssColors.CornflowerBlue` のように取り出す。CSS の148個の named color と gray/grey・aqua/cyan・fuchsia/magenta の別名を含め、`Rgba<byte>` の straight-alpha sRGB 8-bit コード値を immutable な static readonly field として提供する。C# の構造体は literal const にできないため readonly field を使い、値型ごとの暗黙な正規化や gamma 変換は行わない。opaque alpha は255であり、正規化値は `NormalizeUnorm<float>(255)` で明示する。

`Transparent` は148色とは別の CSS keyword として RGBA=(0,0,0,0) を返す。currentColor と system/theme 色は利用文脈に依存するため固定値を登録しない。`All` は148色と transparent の149 keyword を小文字名で保持する変更不能な辞書として公開する。`TryGet` は ASCII の名前を ordinal case-insensitive で照合し、null・非ASCII・未知名では false と default を返す。空白除去や hex・関数構文の parse は行わない。catalog は名前付きデータだけを担当し、数値演算やテーマ管理を追加しない。Drawing2D の `Color2D.FromSrgb8(Rgba<byte> codes)` は明示した8-bit sRGBコードを共通の `NormalizeUnorm<float>(255)` へ委譲してから既存 constructor で検証する。Mathematics から Drawing2D への依存や暗黙変換は作らない。

基準コードでは標準色のbyteコードを共通の名前で取り出す機能がなく、利用側が値を管理する。同じCornflowerBlueを既存の描画用constructorへ渡す場合は数値を繰り返す。

```csharp
using Lumyte.Graphics.Drawing2D;

var color = new Color2D(100f / 255, 149f / 255, 237f / 255, 1);
```

変更後は catalog から取得し、必要な正規化と配置変換を明示する。

```csharp
using Lumyte.Mathematics.Colors;

// 利用側で数値コードを管理する代わりに名前から取得する。
Rgba<byte> codes = CssColors.CornflowerBlue; // (100,149,237,255)
Rgba<float> normalized = codes.NormalizeUnorm<float>(255);
Bgra<float> layout = Bgra<float>.FromRgba(normalized);
```

Drawing2D では同じ色を名前から構築し、既存 canvas API へ渡せる。

```csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;

var color = Color2D.FromSrgb8(CssColors.CornflowerBlue);
```

```diff
+namespace Lumyte.Graphics.Drawing2D;
+public readonly record struct Color2D
+{
+    // 8-bit sRGBコードを共通数学で正規化する。alphaも0..255のコード。
+    public static Color2D FromSrgb8(Lumyte.Mathematics.Colors.Rgba<byte> codes);
+}
```

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

以下は基準コードに対する追加の主要宣言の抜粋であり、既存の型・シグネチャは削除しない。部品の保存・構築と一般の計算を分け、単一色の操作は `Rgba<T>` に集約する。From/Try は static factory、元の色を入力とする操作は instance method とする。`Color` の公開対象は複数画素操作と raw 互換処理だけに限定する。

```diff
+using System.Numerics;
+
+namespace Lumyte.Mathematics.Colors;
+
+public readonly record struct Rgba<T> : IColorPixel<Rgba<T>> where T : INumber<T>
+{
+    // 全成分 finite。色空間・alpha・コード上限は推測しない。
+    public Rgba(T r, T g, T b, T a);
+    public T R { get; }
+    public T G { get; }
+    public T B { get; }
+    public T A { get; }
+    public static bool TryCreate(T r, T g, T b, T a, out Rgba<T> result);
+    // 入力は正確に RGBA 4 成分、CopyTo は 4 要素以上を要求する。
+    public static Rgba<T> FromComponents(ReadOnlySpan<T> values);
+    public void CopyTo(Span<T> destination);
+    public Rgba<T> WithAlpha(T alpha);
+    public Rgba<TTo> ConvertChecked<TTo>() where TTo : INumber<TTo>;
+    // 整数コードをそのまま格納。DecodePixel の正規化と区別する。
+    public static Rgba<T> FromRgba8Codes(byte r, byte g, byte b, byte a);
+    public static Rgba<T> FromArgb32Codes(uint argb);
+    public uint ToArgb32Codes();
+    // upper と各成分が整数値であることを検証する。
+    public Rgba<TResult> NormalizeUnorm<TResult>(T maximum) where TResult : INumber<TResult>;
+    public Rgba<TCode> QuantizeUnorm<TCode>(TCode maximum) where TCode : IBinaryInteger<TCode>;
+    public static Rgba<T> DecodePixel(ReadOnlySpan<byte> source, ColorComponentEncoding encoding, ColorByteOrder byteOrder);
+    // 必要 byte 数を書いて返す。短い出力・変換失敗では書き込みなし。
+    public int WritePixel(Span<byte> destination, ColorComponentEncoding encoding, ColorByteOrder byteOrder);
+    public Rgba<TResult> DecodeSrgb<TResult>() where TResult : IFloatingPointIeee754<TResult>;
+    public Rgba<TResult> EncodeSrgb<TResult>() where TResult : IFloatingPointIeee754<TResult>;
+    public Rgba<TResult> PremultiplyLinear<TResult>() where TResult : INumber<TResult>;
+    public Rgba<TResult> UnpremultiplyLinear<TResult>() where TResult : INumber<TResult>;
+    public Rgba<TResult> InterpolateSrgbLinearPremultiplied<TResult>(Rgba<T> to, TResult amount)
+        where TResult : IFloatingPointIeee754<TResult>;
+    // this が source。相手は destination。
+    public Rgba<T> SourceOverLinearPremultiplied(Rgba<T> destination);
+    public Rgba<T> AddLinearPremultiplied(Rgba<T> destination);
+    public Rgba<T> AddUnitLinearPremultiplied(Rgba<T> destination);
+    // BCL の float 行列・係数・結果。入力も明示した float 経路へ変換する。
+    public Rgba<float> ApplyMatrixLinearPremultiplied(Matrix4x4 matrix, Vector4 offset);
+    public Rgba<TResult> ToLinearGrayscale<TResult>() where TResult : INumber<TResult>;
+    public static Rgba<T> FromHslSrgbDegrees<TModel>(TModel hue, TModel saturation, TModel lightness, TModel alpha)
+        where TModel : INumber<TModel>;
+    public static Rgba<T> FromHsvSrgbDegrees<TModel>(TModel hue, TModel saturation, TModel value, TModel alpha)
+        where TModel : INumber<TModel>;
+    public Hsla<TResult> ToHslSrgbDegrees<TResult>()
+        where TResult : INumber<TResult>;
+    public Hsva<TResult> ToHsvSrgbDegrees<TResult>()
+        where TResult : INumber<TResult>;
+}
+
+public enum ColorByteOrder { LittleEndian, BigEndian }
+// 配置と数値符号化を分離し、配置 enum を公開しない。
+public enum ColorComponentEncoding { Unorm8, Unorm16, Snorm8, Snorm16, Float16, Float32, Float64 }
+public interface IColorPixel<TSelf> where TSelf : IColorPixel<TSelf>
+{
+    static abstract int PixelSize(ColorComponentEncoding encoding, ColorByteOrder byteOrder);
+    static abstract TSelf DecodePixel(ReadOnlySpan<byte> source, ColorComponentEncoding encoding, ColorByteOrder byteOrder);
+    int WritePixel(Span<byte> destination, ColorComponentEncoding encoding, ColorByteOrder byteOrder);
+}
+public readonly record struct Gray<T> : IColorPixel<Gray<T>> where T : INumber<T>
+{
+    public Gray(T value);
+    public T Value { get; }
+    // raw alpha を利用者が指定する。正規化1とbyte255を推測しない。
+    public Rgba<T> ToRgba(T alpha);
+    // RGBが等しくA=1の場合のみ受け入れる。
+    public static Gray<T> FromRgba(Rgba<T> color);
+    public Rgba<TResult> NormalizeUnorm<TResult>(T maximum) where TResult : INumber<TResult>;
+    public static Gray<T> DecodePixel(ReadOnlySpan<byte> source, ColorComponentEncoding encoding, ColorByteOrder byteOrder);
+    public int WritePixel(Span<byte> destination, ColorComponentEncoding encoding, ColorByteOrder byteOrder);
+}
+// GrayAlpha<T>, Rgb<T>, Bgr<T>, Bgra<T>, Argb<T>, Abgr<T> も有限な配置値。
+// 各配置型は IColorPixel<TSelf> を実装し、一色の操作を自身へ置く。
+public readonly record struct Rgb565 : IPackedColorPixel<Rgb565>
+{
+    public Rgb565(ushort packedValue);
+    public ushort PackedValue { get; }
+    public static int ByteSize { get; }
+    public static Rgb565 Read(ReadOnlySpan<byte> source, ColorByteOrder byteOrder);
+    public int Write(Span<byte> destination, ColorByteOrder byteOrder);
+    public static Rgb565 FromNormalized<T>(Rgba<T> color) where T : INumber<T>;
+    public Rgba<T> ToNormalized<T>() where T : INumber<T>;
+}
+// Rgba4444/Rgba5551/Rgba1010102/Bgra1010102 も各bit配置の整数語を保持。
+public interface IPackedColorPixel<TSelf> where TSelf : IPackedColorPixel<TSelf>
+{
+    static abstract int ByteSize { get; }
+    static abstract TSelf Read(ReadOnlySpan<byte> source, ColorByteOrder byteOrder);
+    int Write(Span<byte> destination, ColorByteOrder byteOrder);
+}
+// 正規化straight sRGBのモデル値。hueは度、S/L/V/Aはunit。
+public readonly record struct Hsla<T> where T : INumber<T>
+{
+    public Hsla(T hueDegrees, T saturation, T lightness, T alpha);
+    public T HueDegrees { get; }
+    public T Saturation { get; }
+    public T Lightness { get; }
+    public T Alpha { get; }
+    public Rgba<TResult> ToRgba<TResult>() where TResult : INumber<TResult>;
+}
+public readonly record struct Hsva<T> where T : INumber<T>
+{
+    public Hsva(T hueDegrees, T saturation, T value, T alpha);
+    public T HueDegrees { get; }
+    public T Saturation { get; }
+    public T Value { get; }
+    public T Alpha { get; }
+    public Rgba<TResult> ToRgba<TResult>() where TResult : INumber<TResult>;
+}
+
+// 148 named colorと、別keywordのtransparent。全fieldは8-bit sRGBコード。
+public static class CssColors
+{
+    public static readonly Rgba<byte> Red;
+    public static readonly Rgba<byte> CornflowerBlue;
+    public static readonly Rgba<byte> RebeccaPurple;
+    public static readonly Rgba<byte> Transparent;
+    // 全149keyword。変更不能、キーは小文字、ordinal case-insensitive照合。
+    public static IReadOnlyDictionary<string, Rgba<byte>> All { get; }
+    public static bool TryGet(string? name, out Rgba<byte> color);
+}

+public static partial class Color
+{
+    // 長さと全ての重複を出力前に検査。空は許容、未使用末尾は保持。
+    public static void DecodePixels<TPixel>(ReadOnlySpan<byte> source, Span<TPixel> destination,
+        ColorComponentEncoding encoding, ColorByteOrder byteOrder) where TPixel : IColorPixel<TPixel>;
+    public static void WritePixels<TPixel>(ReadOnlySpan<TPixel> source, Span<byte> destination,
+        ColorComponentEncoding encoding, ColorByteOrder byteOrder) where TPixel : IColorPixel<TPixel>;
+    public static void ReadPackedPixels<TPixel>(ReadOnlySpan<byte> source, Span<TPixel> destination,
+        ColorByteOrder byteOrder) where TPixel : IPackedColorPixel<TPixel>;
+    public static void WritePackedPixels<TPixel>(ReadOnlySpan<TPixel> source, Span<byte> destination,
+        ColorByteOrder byteOrder) where TPixel : IPackedColorPixel<TPixel>;
+    // legacy mixed-precision。非有限値を含む Vector4 の raw 互換処理は有限値 Rgba に強制しない。
+    public static Vector4 InterpolateSrgbLinearPremultipliedRaw(Vector4 from, Vector4 to, float amount);
+}
```

From/Try 構築、明示変換、span はそれぞれ失敗時の出力保証を持つ。TryCreate は無効入力で false・default、CopyTo は短い出力を例外で拒否し書き込まない。数値型を一般化したことと、SIMD で任意の T を直接再解釈できることは別である。SIMD を追加する場合は float・double 等の型別経路と scalar を照合し、長さ・重なり・空入力・末尾、NaN、丸め、tiny batch を含めて測定する。速度・無割り当ての数値目標はこの判断では保証しない。

CSS 全構文、currentColor・system color の解決、ICC profile・広色域・知覚色差、CMYK の profile 変換、任意精度 gamma は今回の対象外とする。HSL/HSV は数値変換を提供するが、UI 編集部品は作らない。これらを暗黙に sRGB の単純変換として扱わない。

## 検討した代替案

- **描画用 Color2D を全機能へ移す:** 呼び出しは少ないが、float・straight sRGB・unit 制約を HDR・clear・byte palette へ強制する。共通保存と操作を独立させ、既存の境界型は維持する。
- **全色を Vector4 だけで扱う:** BCL の演算は再利用できるが、数値型と UNORM・gamma・alpha の意味が見えない。ベクトル演算は BCL、色の契約は Rgba と明示操作に分ける。
- **色空間・精度・実装ごとに公開 utility を作る:** 実装は分類しやすいが、利用者へ backend 選択を漏らす。一色の操作は `Rgba<T>`、複数画素や raw 互換処理は `Color` に分け、実装 helper は隠す。
- **全経路を double や GPU 式へ統一する:** 重複は減るが、float paint・double clear/UI・shader の丸めと閾値が変わる。共有できる数学だけを委譲し、既存の演算経路を検証する。

## 結果と影響

色計算を描画依存なく使い、量子化・色空間・alpha の二重変換を明示的に避けられる。利用者は保存型だけで色の意味が決まらないことを理解し、正規化と cast、表示色と HDR を区別する必要がある。既存 API の意味を維持するための adapter と CPU/GPU の照合は残る。

受け入れ条件は、全格納形式の既知の bit/byte 列、両 endian、チャンネル順、alpha 補完と廃棄の拒否、Gray の等成分条件、SNORM の最小コードと正規化、IEEE HDR/subnormal/overflow、HSL/HSV の無彩色・hue 周期・分数結果・白近傍と subnormal での厳密な中間計算、batch の失敗画素/suffix 不変性を確認することである。sRGB の両閾値と隣接値、float/double の元計算との比較、量子化の端値・中点・往復・ulong 最大値・巨大整数上限、負値・NaN・infinity・overflow の拒否を検証することである。byte・各整数型・BigInteger・decimal・IEEE・参照を含む独自数値型の保存と、対応する結果型での操作を区別して確認する。透明な非黒の端点、alpha=0、UI の mixed-precision 中点と最終 float 化後に有効となる外挿、定数・subnormal の補間、HDR・負の RGB、Plus の二つの clamp 規則、行列の定数項を確認する。

実装の共通化では Drawing2D・RenderGraph clear・UI animation・font palette の元データと画素基準を保持し、sRGB attachment、UNORM presentation、画像 tap ごとの alpha、MTSDF の意味を検査する。公開 API に実装 helper が漏れないこと、数学への依存が一方向であることを別の通常 consumer で確認する。文書の採否、実装、CPU の数値一致、実 GPU の画素検証と性能測定はそれぞれ別の事実として扱う。
