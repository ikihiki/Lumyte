# ADR-GRAPHICS-0021: 外部文字描画向けcoverage・距離場・COLRv1 paint合成

- 状態: 採用
- 日付: 2026-10-26

## 背景

外部文字ライブラリがglyphの大きさ、変形、cache状況に応じてpath、mesh、mask、距離場、色画像を使い分けられるようにしたい。COLRv1では輪郭の集合だけでなくgradient・変換・clip・隔離合成を保持する必要がある。文字だけのrendererやフォント依存を2Dコアへ埋め込むと、この形式選択や既存の汎用命令と責務が重なる。

[GRAPHICS-0018](GRAPHICS-0018-2d-graphics-engine.md)のpaintと、[GRAPHICS-0019](GRAPHICS-0019-2d-paths-and-strokes.md)のpath、[GRAPHICS-0020](GRAPHICS-0020-2d-images-and-meshes.md)の色画像・meshに、DrawCoverageMask、DrawDistanceField、DrawPaintGraphとLayers/Compositeを追加する。

## 決定

### DrawGlyphRunを追加せず汎用命令を揃える

| 外部ライブラリが選ぶ表現 | 2Dへの入力 |
| --- | --- |
| outline | FillPath / StrokePath |
| tessellation済み輪郭 | DrawMesh |
| grayscale coverage atlas | DrawCoverageMask |
| SDF / MSDF / MTSDF atlas | DrawDistanceField |
| RGBA bitmap | DrawImage |
| COLRv1の解決済みpaint | DrawPaintGraph、または形状命令に渡すPaintGraph2D |

文字のfallback、shaping、glyph配置、描画形式選択、outlineの取得、variation、palette、foreground、cacheとatlas管理は外部ライブラリが担当する。同じglyphでも状況により別形式を渡せる。Drawing2Dはfont IDやglyph runを解釈せず、画像とgeometryの契約だけを扱う。

追加3命令に専用interfaceを設け、登録表・CreateDefault・UseGraphics2D・型別保存・配送を更新する。合計11命令となり、指定した実装を構築時に固定する。mask用rendererだけの上書きがpathやpaint内部の描画方式を暗黙に変えることはない。

### coverage textureと距離場

CoverageImage2Dは単一mip/layerのSampled D2 R8Unormを借用し、Rを[0,1]のcoverageとして読む。色のdecodeやpremultiplyは行わない。Sourceはtexel、Destinationはlocal矩形で、sample位置は逆Canvas変換から求める。得たcoverageと共通paint、Canvas clipを合成する。

DistanceField2Dはencodingと正のDistanceRangeを持つ。SDFはR8UnormのR、MSDF/MTSDFはRgba8UnormのRGB中央値を輪郭距離として解釈する。sRGB形式を受け付けず、内側正の `texelDistance = (sample - 0.5) * DistanceRange` を要求する。MTSDFのAは供給データのtrue signed distanceとして残るが、現行の輪郭AAでは参照せずRGBを使う。Aを使った別の距離効果まで実装済みとはしない。

標準shaderはpixel→texel変換のX/Y微分の長さの最大値をrateとし、`coverage = saturate(0.5 + texelDistance / max(rate, 1e-8))`で画面pixel単位の輪郭AAへ変換する。これは距離場の画面上の勾配を厳密に解析する方式ではなく、非一様変換では保守的な幅の近似となる。画面sampleごとのcoverageにpaintを掛け、最終平均を取る。

mask・距離場は渡されたsamplerでsampleする。Source内の論理位置でもfilterのtapが隣へ届くため、atlas padding・range・符号の整合は供給側が保証する。基本paintのSource相対tile処理とは異なり、atlas padding不要とはみなさない。MaxAnisotropy > 1は標準実装で拒否する。外部view・親texture・samplerとそのleaseはGPU完了まで利用側が保持する。

### paint合成と隔離の意味

PaintGraph2D.Layersは子列をコピーし、先頭から順にSrcOverする。空列は透明とする。Compositeはsourceとbackdropをそれぞれ透明な初期値から独立に評価し、指定modeで合成する。backdropはpaintの子であり、既存Canvasの描画背景ではない。Canvas背景を読むエフェクトとは区別する。

CompositeMode2DはCOLRv1の28modeを持つ。Porter-Duff、Plus、separable blend、HSL modeをlinear premultipliedの共通表現で計算する。blend式が必要とするstraight RGBはalpha>0のときだけ除算して取得し、alpha=0では0を使う。Plusは成分を1へ飽和させ、HSL modeは仕様のlum/saturation操作後にpremultipliedへ戻す。

標準実装は子を先にDrawingInstructionへ展開し、fragmentの値配列から二つの子を参照する。隔離は「独立した透明面と等価な色の評価」を意味し、この点ごとのpaint合成には物理的な中間textureを作らない。画像化するレイヤーやぼかしは別のRenderGraph経路にする。

Opacityは子全体の合成結果へ一度掛ける。外側のshape・Canvas clipは合成済みpaintへ一度適用し、Layersの子へ分配しない。同じ幾何評価段階ではshape・clip・paint clipを共通subpixel位置で評価する。

Clear、Src、DestIn等は透明な領域も結果へ影響する。DrawPaintGraphは有限のtargetとCanvas clipを評価範囲とし、子の輪郭unionだけへscissorを狭めない。たとえばDestInのsourceが透明な範囲ではbackdropを消す必要がある。形状命令へpaintを渡す場合は、外側形状が有限の評価範囲となる。結果をCanvasへ出す最後の合成は通常のSrcOverであり、paint内のClearが既存Canvas背景を直接消す契約ではない。

### COLRv1 adapterが保持する情報

| COLRv1要素 | 2D側の表現 |
| --- | --- |
| Solid / VarSolid | CPAL・foreground・alpha解決後のSolid |
| LinearGradient / Var版 | p0/p1/p2を保持する3点linearとColorLine |
| RadialGradient / Var版 | c0/r0/c1/r1を保持する2円radial |
| SweepGradient / Var版 | 変換済みcenter・angleとColorLine |
| PaintGlyph | glyph outlineによるClip |
| PaintColrGlyph | 参照先のpaintを解決して共有し、clip適用範囲を保持 |
| PaintColrLayers | 順序を保持するLayers |
| Transform / Translate / Scale / Rotate / SkewとVar/center版 | centerを含むMatrix3x2のTransform |
| PaintComposite | source・backdropと28modeのComposite |
| ClipList / ClipBox / variable clip box | 適用範囲に矩形pathのClip |

adapterはfont unitsとY上向き座標をCanvasの論理座標へ変換し、outline、gradient、clip box、回転とsweep角へ一貫した変換を施す。NonZeroのwindingと穴を保持し、参照glyphを単一輪郭へ縮約しない。variation storeと正規化軸、CPAL、foreground(0xFFFF)、stop alphaはgraph生成前に解決する。異なる軸・palette・foreground間でgraphを誤共有しない。

ColorLineはvariation適用後にoffsetで安定ソートし、同じoffsetのfont内順序を残す。offsetの[0,1]への一律制限や重複除去はしない。参照循環、不正offset/glyph、深さ・node数超過はadapter側で拒否し、Drawing2Dへ不正fontの解析を持ち込まない。

### 評価上限と対応範囲

CPUのPaintGraph2D factoryは深さ128・展開node65536を上限とする。標準GPU経路はpaint深さ64・shape/clipを含む256命令・262144個のVector4という、より小さい実行予算を適用する。共有DAGも実展開の量を数え、GPUの固定長配列を越える前に失敗する。factoryで作成できるgraphが全て標準rendererで実行できるとは限らない。

3点linearの分母0、同心同半径radial、0角度sweep、幅0のRepeat/Reflectは基本描画ADRと同じNotSupportedExceptionとする。COLRv1を表現できるモデルと、全てのfont入力に対する参照実装との一致は区別する。現行検証は合成fixtureと距離場fixtureであり、font adapterと実fontの網羅的な参照画像比較は本PRに含めない。

## 検討した代替案

| 代替案 | 採用しない理由 |
| --- | --- |
| DrawGlyphRunとfont rendererを2Dへ内蔵 | shaping・cache・形式選択の責務が外部文字ライブラリと重複する |
| 全glyphを単色atlasへ変換 | 拡大・変形・COLRv1のpaint構造と精度を保持できない |
| Compositeを描画順の平坦なpath列へ変換 | backdrop、group opacity、透明領域の演算が変わる |
| paint合成を全て中間textureへ展開 | 点ごとの色評価に不要なpassとallocationを導入する |

## 結果と影響

文字の表現選択を外部へ委ねたまま、汎用命令で複数形式を扱える。COLRv1のpath集合とpaintの共有・隔離の意味を維持し、命令rendererは公開nodeを解析できる。

一方、距離場の生成品質とpaddingは供給側に依存し、DAGの共有だけで実行コストが小さくなる保証はない。全COLRv1入力の互換性、MTSDFのAを使う効果、LCD subpixel描画、font cacheは別途設計・検証する。

## 検証方針

ModelTestsでtexture format、distance range、Layersの配列コピーと空列、Compositeのenumとgraph上限を確認する。RenderingGeometryTestsで共有DAGの展開予算を確認する。GPUテストでは28modeを列挙した期待RGBA、透明領域のDestIn、group opacity、coverageとSDF/MSDF/MTSDFの輪郭をVulkan/wgpuで比較する。adapter導入時には固定font・variation・palette・参照glyph・退化gradientも参照画像へ追加する。

参照: [OpenType COLR](https://learn.microsoft.com/en-us/typography/opentype/spec/colr)、[CPAL](https://learn.microsoft.com/en-us/typography/opentype/spec/cpal)、[Compositing and Blending Level 1](https://www.w3.org/TR/compositing-1/)。

## API・利用例

比較元はsplit/2d-images-meshes。既存のpath・mesh・RGBA画像APIはそれぞれの先行ADRを参照する。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    public sealed class CoverageImage2D
+    {
+        // R8UnormのRを[0,1]のcoverageとして読む。view/samplerは同一deviceから借用。
+        public CoverageImage2D(IGraphicsTextureView view, IGraphicsSampler sampler);
+        public IGraphicsTextureView View { get; }
+        public IGraphicsSampler Sampler { get; }
+    }
+    public enum DistanceFieldEncoding2D { Sdf, Msdf, Mtsdf }
+    public sealed class DistanceField2D
+    {
+        // データtexture。sRGB形式は拒否。SDFはR8、MSDF/MTSDFはRGBA8を使用。
+        // texel距離=(sample-0.5)*distanceRange、内側正。rangeはfiniteかつ正数。
+        public DistanceField2D(IGraphicsTextureView view, IGraphicsSampler sampler,
+            DistanceFieldEncoding2D encoding, float distanceRange);
+        public IGraphicsTextureView View { get; }
+        public IGraphicsSampler Sampler { get; }
+        public DistanceFieldEncoding2D Encoding { get; }
+        public float DistanceRange { get; }
+    }
+    public enum CompositeMode2D
+    {
+        Clear, Src, Dest, SrcOver, DestOver, SrcIn, DestIn, SrcOut, DestOut,
+        SrcAtop, DestAtop, Xor, Plus, Screen, Overlay, Darken, Lighten,
+        ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion,
+        Multiply, HslHue, HslSaturation, HslColor, HslLuminosity
+    }
+    public sealed record LayersPaintNode2D(IReadOnlyList<PaintNode2D> Children) : PaintNode2D;
+    public sealed record CompositePaintNode2D(PaintNode2D Source, PaintNode2D Backdrop,
+        CompositeMode2D Mode) : PaintNode2D;
+    public sealed record DrawCoverageMaskCommand2D(CoverageImage2D Mask, Rect2D Source,
+        Rect2D Destination, PaintGraph2D Paint, DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record DrawDistanceFieldCommand2D(DistanceField2D Field, Rect2D Source,
+        Rect2D Destination, PaintGraph2D Paint, DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record DrawPaintGraphCommand2D(PaintGraph2D Graph,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public interface IDrawCoverageMaskRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawCoverageMaskCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IDrawDistanceFieldRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawDistanceFieldCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IDrawPaintGraphRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawPaintGraphCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public sealed class DefaultDrawCoverageMaskRenderer2D : IDrawCoverageMaskRenderer2D
+    {
+        public DefaultDrawCoverageMaskRenderer2D();
+    }
+    public sealed class DefaultDrawDistanceFieldRenderer2D : IDrawDistanceFieldRenderer2D
+    {
+        public DefaultDrawDistanceFieldRenderer2D();
+    }
+    public sealed class DefaultDrawPaintGraphRenderer2D : IDrawPaintGraphRenderer2D
+    {
+        public DefaultDrawPaintGraphRenderer2D();
+    }
+}
```

```diff
 namespace Lumyte.Graphics.Drawing2D
 {
     public sealed class PaintGraph2D
     {
+        public static PaintGraph2D Layers(ReadOnlySpan<PaintGraph2D> children);
+        public static PaintGraph2D Composite(PaintGraph2D source,
+            PaintGraph2D backdrop, CompositeMode2D mode);
     }
     public abstract class DrawingCanvas2D
     {
+        public void DrawCoverageMask(CoverageImage2D mask, Rect2D source,
+            Rect2D destination, PaintGraph2D paint);
+        public void DrawDistanceField(DistanceField2D field, Rect2D source,
+            Rect2D destination, PaintGraph2D paint);
+        public void DrawPaintGraph(PaintGraph2D graph);
     }
     public sealed class DrawingCommandRenderers2D
     {
-        public DrawingCommandRenderers2D(IFillRectRenderer2D fillRect,
-            IFillRoundedRectRenderer2D fillRoundedRect, IFillCircleRenderer2D fillCircle,
-            IDrawLineRenderer2D drawLine, IFillPathRenderer2D fillPath, IStrokePathRenderer2D strokePath,
-            IDrawImageRenderer2D drawImage, IDrawMeshRenderer2D drawMesh);
+        public DrawingCommandRenderers2D(IFillRectRenderer2D fillRect,
+            IFillRoundedRectRenderer2D fillRoundedRect, IFillCircleRenderer2D fillCircle,
+            IDrawLineRenderer2D drawLine, IFillPathRenderer2D fillPath, IStrokePathRenderer2D strokePath,
+            IDrawImageRenderer2D drawImage, IDrawCoverageMaskRenderer2D drawCoverageMask,
+            IDrawDistanceFieldRenderer2D drawDistanceField, IDrawMeshRenderer2D drawMesh,
+            IDrawPaintGraphRenderer2D drawPaintGraph);
+        public IDrawCoverageMaskRenderer2D DrawCoverageMask { get; }
+        public IDrawDistanceFieldRenderer2D DrawDistanceField { get; }
+        public IDrawPaintGraphRenderer2D DrawPaintGraph { get; }
     }
 }
```

```csharp
// font adapterが解決したoutlineと色を使用する。shapingやpalette解決は2Dの外側。
var source = PaintGraph2D.Clip(outline,
    PaintGraph2D.Solid(new Color2D(1, 0, 0, 0.8f)));
var backdrop = PaintGraph2D.Clip(otherOutline,
    PaintGraph2D.Solid(new Color2D(0, 0, 1, 1)));
var paint = PaintGraph2D.Opacity(0.5f,
    PaintGraph2D.Composite(source, backdrop, CompositeMode2D.SrcOver));
canvas.Begin();
canvas.DrawPaintGraph(paint);
canvas.DrawCoverageMask(mask, atlasRect, glyphRect,
    PaintGraph2D.Solid(new Color2D(1, 1, 1, 1)));
DrawList2D list = canvas.End();
// 同じrendererへ渡せる。maskのatlasとsamplerはGPU完了まで保持する。
```
