# Lumyte.Graphics.Drawing2D.RenderGraph

RenderGraphCanvas2Dはレイヤーと背景効果を記録し、親RenderGraph.Append2D(renderer)へ展開します。
通常描画は別パッケージDrawing2DのCanvas2D・Renderer2Dで直接記録できます。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

var canvas = new RenderGraphCanvas2D();
canvas.Begin();
canvas.BeginLayer(new LayerOptions2D
{
    ContentBounds = new Rect2D(20, 20, 200, 100),
    Effects = graph => graph.AppendGaussianBlur(graph.Source, 3, 3),
    Opacity = 0.8f,
});
canvas.FillRoundedRect(new Rect2D(40, 40, 100, 50), 8, new Color2D(1, 0, 0, 1));
canvas.EndLayer();
RenderGraphDrawList2D list = canvas.End();

var parent = new RenderGraphBuilder(device, "frame");
var description = new TextureDesc
{
    Width = 512,
    Height = 256,
    Format = TextureFormat.Rgba16Float,
    Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled,
};
var target = new RenderTargetBinding2D
{
    Texture = parent.CreateTexture(description, "2d-output"),
    Description = description,
    LoadOp = AttachmentLoadOp.Clear,
};
using var renderer = new RenderGraphRenderer2D(
    DrawingCommandRenderers2D.CreateDefault(), list, target,
    new DrawingGraphBindings2D(parent));
using PreparedDrawingPasses2D drawing = await parent.Append2D(renderer);
parent.MarkOutput(target.Texture);
// 同じparentへ3Dや後処理のpassを追加できる。
using (var plan = parent.Compile())
using (IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new()))
{
    plan.Record(commands);
    commands.Finish();
    using IGraphicsSubmission submission = device.Queue.Submit([commands]);
    await submission.WaitAsync();
}
```

EffectsのgraphはImageEffectSubgraphです。Sourceは適用対象の入力画像、Graphは親graphの借用参照です。
AppendGaussianBlur等は数値を直接受け取り、EffectImageを返します。内部の固定資源を部分ビューが所有し、
Append2Dの準備ownerが保持します。独立したgraph、再import、Compile、Record、Submitはありません。
独自効果はscope.AppendEffect、新しい便利拡張メソッド、またはscope.Graphへのpass宣言で追加できます。

背景への適用は記録中に次のように指定します。

```csharp
canvas.DrawBackdropEffect(new BackdropEffectOptions2D
{
    OutputBounds = new Rect2D(250, 20, 200, 100),
    Effects = graph =>
    {
        var source = graph.Source;
        var blurred = graph.AppendGaussianBlur(source, 3, 3);
        return graph.AppendOpacity(blurred, 0.8f);
    },
});
```

任意のコールバックには必要入力ROIの照会契約がないため、背景は現在描画先の有限領域全体を取得します。
効果の出力は最後にclipされ、元背景との置換はeffect*c + original*(1-c)です。
Append失敗時は親graphを捨てて再構築します。GPU完了後にcommand、compiled plan、準備ownerの順で解放します。

設計・APIは [GRAPHICS-0018](../../../docs/adr/graphics/GRAPHICS-0018-2d-graphics-engine.md)、
共通の部分グラフは [GRAPHICS-0017](../../../docs/adr/graphics/GRAPHICS-0017-image-effects.md) を参照してください。
