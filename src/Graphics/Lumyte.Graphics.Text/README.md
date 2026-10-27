# Lumyte.Graphics.Text

文字組みエンジンから受け取ったベクター文字を、既存の2D描画へ接続する共通パッケージです。
不変な`TextLayout2D`、輪郭・カラーpaintの描画、MSDF/MTSDF生成とatlasの所有権を提供します。
このパッケージはSixLabors.FontsやSkiaSharpへ依存しません。

文字組みには[Parleyアダプター](../Lumyte.Graphics.Text.Parley/README.md)を使えます。
従来の比較用構成は[SixLaborsアダプター](../Lumyte.Graphics.Text.SixLabors/README.md)へ分離しています。
フォントの読込・シェーピング・双方向処理・折り返しの対応範囲は、選んだアダプターに従います。

## レイアウトと描画

`TextLayout2D`は配置済みの輪郭とpaintを保持するCPU snapshotです。
レイアウトエンジンやフォントを解放した後も使え、同じsnapshotを複数回・複数のcanvasへ記録できます。

```csharp
using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;

// layoutはParley等の文字組みエンジンが作ったTextLayout2D。
var canvas = new Canvas2D();
canvas.Begin();
canvas.DrawText(layout, new Vector2(20, 30));
DrawList2D list = canvas.End();
// 通常のRenderer2D.PrepareAsync → Record → Submitへ渡します。
```

`DrawText`はBeginとEndの間で呼び、呼び出し前後のcanvasの変換とclipを維持します。
originはレイアウト全体の平行移動です。paintも同じ座標で動くため、gradientは文字ごとに伸縮しません。
`RenderTarget2D.PixelScale`は、通常の2D描画と同じ論理座標から物理pixelへの倍率です。

独自の文字組みエンジンは、次のfactoryで共通snapshotを作れます。

```csharp
TextLayout2D layout = TextLayout2D.FromDrawings(
    text, inkBounds, advanceBounds, lineCount, drawings);
```

`text`と`drawings`はnullを受け付けず、drawing要素にもnullを含められません。
矩形の成分はfinite、幅・高さとlineCountは非負を要求します。
factoryはdrawingの列をコピーし、作成後の列の変更がsnapshotへ影響しないようにします。
各pathとpaintはDrawing2Dの不変なmodelを参照します。

- `Text`は元のUnicode文字列です。
- `Bounds`は輪郭の制御点・装飾・カラーpaintを含む保守的な描画矩形です。
- `AdvanceBounds`は空白等も含む組版上の矩形、`LineCount`は行数です。
- `Geometry`は通常輪郭の`TextGeometry2D`だけを列挙します。
- `Drawings`は通常輪郭と`TextPaintGeometry2D`を統合したpainter orderです。

通常輪郭は`FillPath`、COLR等のカラーpaintは有限なboundsへの`FillRect`へ展開します。
カラーpaintは既存のgradient・clip・transform・compositeをGPUで直接評価し、
文字全体のbitmap化やカラーtextureのuploadを必須にしません。
画像patternのpaintを渡す場合は、通常のDrawing2Dと同様に参照したtextureをGPU完了まで保持してください。

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

SeasonMSDFはMITライセンスです。[依存の出典とライセンス](DistanceFields/THIRD-PARTY-NOTICES.md)を参照してください。

## 制約と依存

この共通パッケージはmarkup parserやhit test、caret・selectionを定義しません。
そのような機能やフォント形式の対応は、文字組みアダプターの公開APIで確認してください。
大量の文字を描く場合は、輪郭の直接描画と距離場atlasの生成・転送・描画の負荷を測定してください。
距離場atlasはsnapshot単位で再使用でき、フォント全体の永続cacheや自動退避は行いません。

MSDF/MTSDF生成の依存はMITライセンスのSeasonMSDF 0.2.0です。
Drawing2Dのpaint model・shaderの深さ、命令数、geometry budgetは文字にも適用されます。
COLR v1向けの全28合成とgradientの境界条件は[Drawing2D](../Lumyte.Graphics.Drawing2D/README.md)を参照してください。

設計・比較・制約は[GRAPHICS-0019](../../../docs/adr/graphics/GRAPHICS-0019-text-rendering.md)を参照してください。
