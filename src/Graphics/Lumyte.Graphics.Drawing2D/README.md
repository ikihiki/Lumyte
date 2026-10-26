# Lumyte.Graphics.Drawing2D

通常のCanvas2DとRenderer2DはRenderGraph・ImageEffectsへ依存しません。
塗りとclipをCPUに記録し、Graphics command bufferへ直接描画します。
この段階で利用できる命令は FillRect, FillRoundedRect, FillCircle, DrawLine, FillPath, StrokePath, DrawImage, DrawCoverageMask, DrawDistanceField, DrawMesh, DrawPaintGraph です。
レイヤーと画像エフェクトは後続PRで別パッケージのDrawing2D.RenderGraphに追加します。

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

塗りは単色、linear/radial/sweep gradient、画像pattern、path clip、変換、隔離合成、COLRv1の28合成modeを扱います。
文字組みは外部ライブラリが担当し、パス・mesh・coverage・SDF/MSDF/MTSDF・画像で描画を依頼できます。
設計・APIは [GRAPHICS-0018](../../../docs/adr/graphics/GRAPHICS-0018-2d-graphics-engine.md) を参照してください。

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
