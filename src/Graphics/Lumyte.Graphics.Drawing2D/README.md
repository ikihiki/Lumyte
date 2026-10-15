# Lumyte.Graphics.Drawing2D

通常のCanvas2DとRenderer2DはRenderGraph・ImageEffectsへ依存しません。
塗りとclipをCPUに記録し、Graphics command bufferへ直接描画します。
この段階で利用できる命令は FillRect, FillRoundedRect, FillCircle, DrawLine, FillPath, StrokePath です。
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

6描画命令には専用interfaceがあり、DrawingCommandRenderers2Dへ実装を直接渡します。
CreateDefault()はdeviceやDIを使わず既定構成を作ります。UseGraphics2Dによる登録と後勝ち上書きも可能です。
外部batchはRecordでupload・compute・render等の複数コマンドを発行し、内部同期を記録できます。
DeclareResourcesでは外部共有texture・bufferのアクセスを宣言し、graph側のadapterが依存へ変換します。
batch私有資源はbatchが所有・同期し、外部資源の入口・出口のstateと可視性を維持します。

塗りは単色、linear/radial/sweep gradient、画像pattern、path clip、変換を扱います。
文字向け描画形式は後続PRで追加します。
設計・APIは [GRAPHICS-0018](../../../docs/adr/graphics/GRAPHICS-0018-2d-graphics-engine.md) を参照してください。

## 外部プログラムの取得

```csharp
using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), programs);
```

programsは事前に準備した資源を返す `IGraphicsProgramProvider` です。
ABI識別子は `DrawingPrograms2D.ProgramId` で、argument・instruction配置と演算の意味を含みます。
program leaseはprepared batchが保持し、GPU使用とcommandの解放後にPreparedDraw2Dとともに返却します。
providerを省略すると `DrawingPrograms2D.DefaultProvider` が埋め込みartifactから生成します。外部providerで内部fallbackは行いません。
Resources adapterはread scopeを一回のPrepareAsync全体で固定し、同じ内容世代を返します。
デバイスロスト後は旧batchを解放し、新device・復元済みprogramで準備し直します。
