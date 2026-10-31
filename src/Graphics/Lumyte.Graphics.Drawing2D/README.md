# Lumyte.Graphics.Drawing2D

通常のCanvas2DとRenderer2DはRenderGraph・ImageEffectsへ依存しません。
図形、パス、画像、外部文字の描画形式、塗りとclipをCPUに記録し、Graphics command bufferへ直接描画します。
レイヤーと画像エフェクトは別パッケージのDrawing2D.RenderGraphに分離しています。

Color2Dは生成時と各成分のinit（withを含む）で、RGBAがfiniteかつ0〜1であることを検査します。不正値はArgumentOutOfRangeExceptionです。defaultは透明色として有効で、従来の名前付き引数・分解・値比較も使えます。UI側で同じ色検証を繰り返す必要はありません。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;

var canvas = new Canvas2D();
canvas.Begin();
canvas.FillRect(new Rect2D(20, 20, 100, 50), new Color2D(1, 0, 0, 1));
DrawList2D list = canvas.End();

using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new RenderTarget2D
{
    View = targetView,
    LoadOp = AttachmentLoadOp.Clear,
});
using (IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new()))
{
    prepared.Record(commands);
    commands.Finish();
    using IGraphicsSubmission submission = device.Queue.Submit([commands]);
    await submission.WaitAsync();
}
```

targetViewは同一deviceの単一mip/layerのRgba8SrgbまたはRgba16Float attachmentです。
初期target状態はColorAttachment、入力画像はSampledへ可視化済みとします。
PrepareAsyncはgraphを作らず、固定資源とbatchを準備します。Recordは一回だけ使い、GPU完了後に
command buffer、準備ownerの順に解放します。

11描画命令には専用interfaceがあり、DrawingCommandRenderers2Dへ実装を直接渡します。
CreateDefault()はdeviceやDIを使わず既定構成を作ります。UseGraphics2Dによる登録と後勝ち上書きも可能です。
外部batchはRecordでupload・compute・render等の複数コマンドを発行し、内部同期を記録できます。
DeclareResourcesでは外部共有texture・bufferのアクセスを宣言し、graph側のadapterが依存へ変換します。
batch私有資源はbatchが所有・同期し、外部資源の入口・出口のstateと可視性を維持します。

塗りは単色、linear/radial/sweep gradient、画像pattern、path clip、変換、隔離合成を扱います。
`PaintGraph2D.Composite(source, backdrop, mode)`はCOLR v1の全28種類の演算を提供します。
sourceとbackdropを同じsubpixel位置で評価して合成し、描画先attachmentの内容は読みません。
色stopの補間とblendはlinear-lightのpremultiplied RGBAです。

COLRの退化したgradientも表現できます。空のstop列は透明、1 stopは一定色です。
3点linearの投影が退化する場合と、同一の2円radialは透明になります。
2円radialの半径は符号付きfinite値を保持し、描画点では補間半径が正の解だけを使います。
簡略形の単一中心radialのradiusは引き続き正数を要求します。
Repeat/Reflectで2 stop以上のoffset spanが0なら透明です。
開始角と終了角が同じsweepは、Padではその角の前後で端色を切り替え、Repeat/Reflectでは透明になります。
`PaintGraph2D.DistanceField`はMSDF/MTSDFで任意のpaintをmaskします。
sourceはsRGBでないRgba8UnormまたはRgba16Floatのatlasとし、0.5を輪郭、値の増加側を内側とします。
DistanceRangeはsource texelで表す距離の総幅です。MSDFはRGB median、MTSDFは拡大時にRGB median、
縮小時にalphaのtrue distanceも使います。sourceのalphaは通常の透明度ではありません。
画像patternのsamplingはraw channel・linear filter・透明な領域外へ正規化されます。
atlasをSampledへ可視化し、GPU完了まで保持する契約は通常の画像patternと同じです。
FillPathはNonZero/EvenOddの複合輪郭を塗り、開いたcontourは塗り時に閉じます。
文字描画には別パッケージの[Lumyte.Graphics.Text](../Lumyte.Graphics.Text/README.md)を使います。
TextLayout2Dをcanvas.DrawTextで記録すると、文字輪郭と装飾をFillPathへ展開し、
同じpaint・変換・clipを適用できます。COLR v1のTextPaintGeometry2Dは、そのpaint graphとboundsを
FillRectへ展開して2Dバックエンドで直接評価します。カラーglyph用のbitmapやtexture uploadは不要です。
MSDF/MTSDFを選ぶ場合はTextパッケージのDistanceFieldText2Dで通常輪郭のatlasを準備し、
uploadを記録してからDrawで矩形へ展開します。混在するCOLR paintはvector graphを維持します。
基本Drawing2Dへフォントライブラリの依存は加わりません。

文字組みは外部ライブラリが担当し、パス・mesh・coverage・SDF/MSDF/MTSDF・画像で描画を依頼できます。
既存のsampler付き`DrawDistanceField`も引き続き利用できます。
Textのatlasでは、任意のpaintと組み合わせられる`PaintGraph2D.DistanceField`を使います。
設計・APIは以下を参照してください。

- [基本描画・paint・資源管理](../../../docs/adr/graphics/GRAPHICS-0018-2d-graphics-engine.md)
- [線・パス](../../../docs/adr/graphics/GRAPHICS-0019-2d-paths-and-strokes.md)
- [画像・mesh](../../../docs/adr/graphics/GRAPHICS-0020-2d-images-and-meshes.md)
- [文字向け形式・COLRv1](../../../docs/adr/graphics/GRAPHICS-0021-2d-glyph-representations.md)
- [Parleyによる文字組み・COLR対応の拡張](../../../docs/adr/graphics/GRAPHICS-0024-text-rendering.md)

## 外部の描画資源

ResourceManagerなしでは、生成済みのpipelineと任意のsamplerをまとめて渡します。

```csharp
using var vertex = device.CreateShader(vertexArtifact);
using var fragment = device.CreateShader(fragmentArtifact);
using var pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
var resources = new DrawingResources2D(device, pipeline);
using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), resources);
using var prepared = await renderer.PrepareAsync(drawList, target);
// Record/SubmitとGPU完了の後に、上記所有者を逆順に解放する。
```

DrawingResources2DはDevice/Pipeline/Samplerの借用参照で、fragment artifactはpipelineのshaderから取得します。
resourcesを省略するとDrawingShaders2Dの埋め込みartifactから必要時に生成し、renderer内で共有します。
既定artifactはDrawingShaders2D.Vertex/Fragmentからも参照できます。

外部資源の所有者をrendererへ移譲する場合はresourceOwnerへIDisposableを渡します。
rendererとpreparedは内部でその所有者を共有し、最後の使用が返った時に一度だけ解放します。
validateResourcesにはResourceLease.Value等の有効性検査を接続できます。検査失敗時に埋め込みへ切り替えません。

Resources連携はLumyte.Resources.Graphics.Drawing2Dを使います。
AddGraphics2Dで登録し、AcquireRenderer2DAsync→CreateRendererで生成します。
元のResourceLeaseを所有者として一つだけ保持し、描画用のproviderや別のpipeline leaseは作りません。
