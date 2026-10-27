# Lumyte.Graphics.Text.SixLabors

SixLabors.Fonts 2.1.3とSkiaSharpを使う比較用の任意アダプターです。
新しい構成は[Parleyアダプター](../Lumyte.Graphics.Text.Parley/README.md)を参照してください。
このパッケージを参照した場合にだけSixLaborsとSkiaへの依存が加わります。

SixLabors.Fonts 2.1.3で文字組みし、通常のグリフは輪郭またはMSDF/MTSDFで描画します。
OpenTypeの合字・kerning・複雑script・双方向の文章、フォントフォールバック、
複数フォント・サイズ・paint、下線・打消し線等を同じ2D描画へ接続できます。
COLR v1のpaint graphはDrawing2Dのgradient・clip・変換・compositeへ変換し、GPUで直接評価します。
文字組み・COLR tableの解釈・距離場の生成はmanaged C#で動き、
ColorFont2Dがglyph IDから輪郭を取得するときだけSkiaSharpのnative assetを使います。

```csharp
using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using SixLabors.Fonts;

var fonts = new FontCollection();
// fontStreamはアプリが取得したTrueType/OpenTypeフォント。
// Browserでは同梱フォントを取得してStreamへ渡します。
FontFamily family = fonts.Add(fontStream);
var options = new TextOptions(family.CreateFont(24))
{
    WrappingLength = 480,
    LineSpacing = 1.2f,
    TextAlignment = TextAlignment.Start,
};

TextLayout2D layout = SixLaborsTextLayout2D.Create("Hello, Lumyte!", options);
var canvas = new Canvas2D();
canvas.Begin();
canvas.DrawText(layout, new Vector2(20, 30));
DrawList2D list = canvas.End();
// 通常のRenderer2D.PrepareAsync → Record → Submitへ渡します。
```

フォントサイズとWrappingLengthはTextOptionsの単位を使います。
Dpiの既定値72では、サイズ24は論理座標の24に対応します。
RenderTarget2D.PixelScaleは通常の2D描画と同じく、論理座標から物理pixelへの倍率です。
複数言語を扱う場合は、必要なglyphを含むフォントを
`TextOptions.FallbackFontFamilies`へ明示してください。

## スタイルとpaint

TextOptions/TextRunのフォント、折り返し、配置、方向、OpenType機能をそのまま利用します。
runごとのpaintはTextRun2Dで指定します。
双方向の文章には既定の`TextDirection.Auto`を使います。
SixLabors 2.1.3の明示的なLeftToRight/RightToLeftは方向の強制指定です。

```csharp
var accent = PaintGraph2D.LinearGradient(
    new Vector2(0, 0), new Vector2(180, 0),
    [new(0, new Color2D(0.1f, 0.3f, 1, 1)), new(1, new Color2D(0.8f, 0.1f, 0.5f, 1))]);
options.TextRuns =
[
    new TextRun2D
    {
        Start = 0,
        End = 5,
        Font = family.CreateFont(30, FontStyle.Bold),
        Paint = accent,
        TextDecorations = TextDecorations.Underline,
    },
];
TextLayout2D styled = SixLaborsTextLayout2D.Create("Hello, Lumyte!", options,
    PaintGraph2D.Solid(new Color2D(0.15f, 0.15f, 0.15f, 1)));
```

Start/Endは**書記素のindex**で、Startを含みEndを含まない範囲です。
サロゲートペアや結合文字を含む文字列では、UTF-16のindexやstring.Lengthから直接作らないでください。
既定paintは黒、TextRun2D.PaintがnullならCreateへ渡したpaintを使います。
ColorFont2Dを登録しない場合、COLR v0の色付きレイヤーはSixLabors経由でフォントの色を使います。
COLR v1は後述のColorFont2Dで供給します。

TextLayout2Dは生成時の輪郭とpaintを保持する不変なsnapshotです。
作成中はoptionsとrunをコピーし、上流の文字組みによって呼び出し元が変更されることを避けます。
生成後にoptionsやTextRunsを変更しても既存のlayoutへ影響しません。
装飾は全グリフの後に描画し、同じ位置・太さ・paintの装飾を重複させません。
COLRの複数レイヤーで同じ装飾callbackが来ても、半透明色が重なって濃くなることを防ぎます。
同じlayoutを複数回・複数のcanvasで使えます。
Boundsは輪郭の端点・制御点、装飾、カラーpaintの描画範囲を含む保守的な矩形で、厳密な最小外接矩形ではありません。
AdvanceBoundsは空白等も含む送りの計測値、LineCountは行数です。
Textには元の文字列、Geometryには不変な輪郭とpaintが入ります。
COLR v1を含む場合は、輪郭とTextPaintGeometry2Dを統合したDrawingsが実際の描画順を保持します。
カラーpaintも通常のcanvas.DrawTextで記録でき、描画時のbitmap化やカラーtextureのuploadは不要です。

通常のDrawTextはBeginとEndの間で呼び、呼び出し前後のcanvasの変換とclipを維持します。
originはレイアウト全体の平行移動です。paintも同じ座標で動くため、gradientは文字ごとに伸縮しません。
レイアウト自体はGPU資源を作りません。画像patternのpaintを渡す場合は、通常のDrawing2Dと同様に、
参照したtextureをGPU完了まで保持してください。

## MSDF/MTSDF描画

輪郭を直接描く経路に加え、SeasonMSDF 0.2.0のmanagedな生成器で距離場を作れます。
MSDFはRGBの異なるedge channelとmedianを使い、拡大時に鋭い角を保ちます。
MTSDFはさらにalphaへtrue signed distanceを保持し、縮小時にはその距離も使います。
MTSDFのalphaは透明度ではありません。生成時の解像度とDistanceRangeを選び、
同じownerを複数のフレームやcanvasで再使用します。

```csharp
using var distanceText = await DistanceFieldText2D.CreateAsync(
    device, layout, pixelsPerUnit: 1, distanceRange: 8,
    mode: DistanceFieldMode2D.Mtsdf);

canvas.Begin();
distanceText.Draw(canvas, new Vector2(20, 30));
DrawList2D distanceList = canvas.End();
using PreparedDraw2D prepared = await renderer.PrepareAsync(distanceList, target);
using (IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new()))
{
    distanceText.RecordUpload(commands); // ownerにつき一度だけ。submitはしません。
    prepared.Record(commands);
    commands.Finish();
    using IGraphicsSubmission submission = device.Queue.Submit([commands]);
    await submission.WaitAsync();
}
// 同じatlasを再使用するときはRecordUploadを繰り返さず、Drawから記録します。
// 全upload/drawのGPU完了後にprepared・distanceTextを解放します。
```

`CreateAsync`は輪郭ごとの距離場をRgba8Unorm atlasへpackし、upload用bufferを準備します。
COLR v1を含むlayoutでは、カラーpaint graphをそのまま保持し、
通常輪郭の距離場とDrawingsの順序で描画します。カラーpaintを距離場へ符号化しません。
`RecordUpload`はcopyとSampledへのbarrierを記録します。uploadを別のcommand bufferへ記録する場合も、
後続のdrawがatlasをsampleする前に完了・可視化される順序を利用側が保証します。
自動submit、GPU wait、フォント全体を保持する永続glyph cacheはありません。
ownerの`View`は距離場atlasの借用参照です。atlasとupload stagingのownerをGPU完了まで維持します。

CPUのpixelデータだけが必要な場合は、次のAPIを使います。

```csharp
DistanceFieldImage2D image = DistanceFieldGenerator2D.Generate(
    outlinePath, pixelsPerUnit: 1, distanceRange: 8,
    mode: DistanceFieldMode2D.Mtsdf);
// Pixels: 先頭行が上のRGBA8線形データ。
// PlaneBounds: paddingを含む配置矩形。DistanceRange: source texel単位の距離の総幅。
```

例えばDistanceRange=8は、輪郭を0.5としておよそ-4～+4 texelの距離を符号化します。
0.5より大きい値が内側です。paddingは`ceil(DistanceRange / 2) + 1` texelです。
生成器は線・二次曲線・三次曲線を保持し、InkTrap edge coloring、overlap対応、error correctionと
NonZeroの符号補正を行います。open contourは閉じ、空の輪郭は外側を表す1×1画像になります。
入力輪郭は4096 segment、出力は4,194,304 pixelまで、単辺は`maximumDimension`以下です。
`maximumDimension`は1～4096、既定2048で、超過はNotSupportedExceptionになります。
生成は同期処理で、独立した呼び出しの間で変更可能な状態を共有しません。

独自atlasはDrawing2Dの`PaintGraph2D.DistanceField`へ接続できます。
sourceはRgba8UnormかRgba16Floatを使い、sRGB形式は指定できません。
sourceの整数texel矩形とPatternToLocalで配置し、glyphのplane boundsへFillRectします。
色やgradientはchild paintで指定するため、輪郭描画と同じpaint・変換・clipを使えます。

SeasonMSDFはMITライセンスです。[依存の出典とライセンス](../Lumyte.Graphics.Text/DistanceFields/THIRD-PARTY-NOTICES.md)を参照してください。

## COLR v1カラーフォント

COLR v0/v1のtableを読み、solid、linear/radial/sweep gradient、transform、
glyph/layer参照、glyphによるclip、compositeをDrawing2Dのpaint graphへ変換します。
グリフの輪郭取得にはSkiaSharp 3.119.4の`SKFont.GetGlyphPath`を使いますが、
通常のカラーフォント描画ではSkiaのpaint engineやCPU bitmapを使いません。
COLRのbase glyph自身の輪郭も取り出せるよう、カラーtableを除いた輪郭専用のTypefaceを内部で作ります。
TrueTypeのhmtx bearingをglyfのdesign座標へ合わせ、glyph IDと輪郭の位置を保持します。
2Dバックエンドが各subpixelでpaint graphを評価するため、変換とclipは通常の図形と同じ規則です。

同じフォントデータを`ColorFont2D.Load(Stream)`へ渡し、そのFamilyから作ったFontをTextOptionsへ設定します。
SixLaborsTextLayout2D.CreateのcolorFontsへ登録すると、対応するCOLR glyphをpaint graphへ渡します。
この一覧が空でない場合、SixLabors側のCOLR v0レイヤー展開を止め、元の整形済みglyph IDを使います。
同じlayoutで使うCOLR v0/v1のフォントは、fallbackも含めすべてcolorFontsへ登録してください。
登録しないCOLR v0フォントを同時に使うと、そのカラー展開は行われません。

```csharp
using var colorFont = ColorFont2D.Load(colorFontStream);
var colorOptions = new TextOptions(colorFont.Family.CreateFont(48));
TextLayout2D colorLayout = SixLaborsTextLayout2D.Create(
    textFromColorFont, colorOptions, colorFonts: [colorFont], colorPaletteIndex: 0);
canvas.Begin();
canvas.DrawText(colorLayout, new Vector2(20, 30));
DrawList2D colorList = canvas.End();
// 通常のRenderer2Dへ渡します。カラーglyph用のtexture uploadは不要です。
```

COLR palette index 0xFFFFのforegroundは、runまたはlayoutのPaintGraph2Dを参照します。
単色のalphaだけでなく、gradient等のpaintも使えます。フォントのpalette・stop alphaと
PaintSolid/ColorStopのalphaはpaint graph上で合成し、bitmap経路にあったforeground alpha=1の制約はありません。
`colorPaletteIndex`でCPALのpaletteを選びます。paletteとforegroundはsnapshot作成時に解決し、
作成後にColorFont2DをDisposeしても生成済みpaint graphは使用できます。
既存の`colorPixelsPerUnit`引数は呼び出し互換のため残り、vector描画の解像度を制限しません。

単独glyphのpaintが必要な場合は、`ColorFont2D.RenderPaintGlyph`へ整形済みglyph ID・論理emサイズ・
foreground paint・palette indexを渡します。結果のColorGlyphPaint2Dは、baseline基準のBoundsとPaintを保持します。
フォントのglyph IDは文字コードではありません。通常はTextLayout2Dで文章全体を文字組みします。

全28種類のcompositeは`PaintGraph2D.Composite(source, backdrop, mode)`へ変換します。
backdropはそのnodeの入力paintであり、描画先attachmentの既存内容を暗黙に読みません。
COLR/CPAL仕様に従い、色stopの補間とblendはlinear-lightのpremultiplied RGBAで行います。
描画先への通常のsource-over合成は、glyph内部のisolatedなcompositeとは別に適用します。

`RenderGlyph`とColorGlyphBitmap2Dは互換性と比較用の明示的なraster APIとして残ります。
こちらはSkiaのpaint engineを呼び、指定解像度のstraight-alpha sRGB RGBAを返します。
legacy raster APIでは既定palette/可変軸を使い、foreground alphaは1のみです。
通常のTextLayout2Dとcanvas.DrawTextはこのAPIを呼びません。

COLR v1のpaint format 1～32を次のmodelへ対応させます。

| COLR paint | Drawing2Dでの表現 |
| --- | --- |
| Layers、PaintColrGlyph | 参照先を解決したpaintの合成 |
| Solid / VarSolid | paletteまたはforeground paintとalpha |
| Linear / Radial / Sweep gradientとVar形式 | ColorLineを持つgradient |
| PaintGlyph | glyph輪郭のNonZero clip |
| Transform / Translate / Scale / Rotate / SkewとVar形式 | affine transform |
| PaintComposite | 全28種類のisolated composite |

可変paint・ColorStop・ClipBoxのdeltaは、normalized座標がすべて0の既定instanceで評価します。
DeltaSetIndexMapとItemVariationStoreを読み、既定座標でも有効なdeltaを反映します。
任意のvariation軸を指定する公開APIはありません。
readerはdepth 96・paint訪問4096・ColorLine 4096 stopまでとし、不正なoffset・cycle・範囲外の参照を拒否します。
最終glyphは有限のClipListまたはPaintGlyph等から求められる描画boundsを必要とし、
無限のpaintだけで構成されたglyphはNotSupportedExceptionになります。
輪郭は通常のTrueTypeの二次曲線・CFFの三次曲線を扱います。
輪郭取得結果に非多項式conicが含まれる場合はNotSupportedExceptionで拒否します。
このreader上限とは別に、Drawing2Dのmodel・shaderの命令数とgeometry budgetも適用されます。

ColorFont2D.Loadはstreamを所有せず、現在位置から読み込みます。
desktopでは実行環境に対応するSkiaSharp native assetを配布します。
Browser WASMではSkiaSharp.NativeAssets.WebAssemblyをアプリへ追加し、
wasm-toolsでnative static linkできる構成を使います。これはglyph輪郭取得の依存です。
具体例は[Browserサンプル](../../../samples/Lumyte.Graphics.Text.Browser.Sample/README.md)を参照してください。
SkiaSharpはMITライセンスです。[SkiaSharpのライセンスとnative依存のnotice](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md)を参照してください。

## 対応範囲と配布

Pangoと同じAPIやmarkup parser、hit test、caret・selectionは提供しません。
SVG glyph、bitmap emojiを含むあらゆるカラーフォント形式の完全な描画は保証しません。
実際に描画できる文字は供給したフォントのglyph・OpenType tableとSixLabors 2.1.3の対応に依存します。
大量の文字を描く場合は、輪郭の直接描画と距離場atlasの生成・転送・描画の負荷を測定してください。
距離場atlasはsnapshot単位で再使用でき、フォント全体の永続cacheや自動退避は行いません。

Browser WASMでもFontCollectionへフォントデータを明示して供給します。
ブラウザーからOSのフォントを列挙できることを前提とせず、フォント取得はアプリで非同期に行います。
SixLaborsTextLayout2D.Createは同期処理です。

ブラウザーでの組み込み例は[Lumyte.Graphics.Text.Browser.Sample](../../../samples/Lumyte.Graphics.Text.Browser.Sample/README.md)を参照してください。

SixLabors.Fontsは**Six Labors Split License 1.0**です。
条件によりApache-2.0または商用ライセンスが適用されます。
2.1.3固定でも利用条件は残ります。[ライセンス本文](https://github.com/SixLabors/Fonts/blob/v2.1.3/LICENSE)を確認してください。
2.1.3は比較対象を再現するため保持しており、新規構成の基準にはしません。
[公式security policy](https://github.com/SixLabors/Fonts/blob/main/SECURITY.md)では、
古いmajorはセキュリティ修正の対象外です。
現行版へ更新する場合はAPI移行とライセンス条件を確認します。
条件を満たす公開プロジェクトでは、assembly名限定のcommunity keyを同梱できます。
[現行版のキー運用](https://docs.sixlabors.com/articles/fonts/index.html)を参照してください。

設計・比較・制約は[GRAPHICS-0024](../../../docs/adr/graphics/GRAPHICS-0024-text-rendering.md)を参照してください。
