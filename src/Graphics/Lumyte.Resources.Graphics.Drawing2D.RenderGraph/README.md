# Lumyte.Resources.Graphics.Drawing2D.RenderGraph

純粋な2D描画と同じ生成用リソースから、親RenderGraphへ追加するレンダラーを作ります。
グラフを使わない2D描画はResources.Graphics.Drawing2Dだけで構成できます。

```csharp
using var rendererResource = await manager.AcquireRenderer2DAsync();
using var extensions = await new RenderGraphExtensionsBuilder()
    .AddResources(manager).AddImageEffects(manager).BuildAsync();

using var parent = new RenderGraph(device, extensions: extensions);
// layeredListはEffects = graph => graph.AppendGaussianBlur(graph.Source, 3, 3)等を含む。
using var renderer = rendererResource.CreateRenderGraphRenderer(
    commandRenderers, layeredList, graphTarget, bindings);
using var drawing = await parent.Append2D(renderer);
parent.MarkOutput(graphTarget.Texture);
using var plan = parent.Compile();
plan.Record(commands);
// submitとGPU完了を確認し、command、plan、drawingの順に解放する。
```

起動時の`AddGraphics2D()`と`UseGraphicsShaders<Renderer2DResource>(vertex, fragment)`は純粋な2Dと共通です。
Effectは`UseGraphicsShaders<ImageEffectShaderResource>(vertex, fragment)`で設定します。
利用側でprogram IDやproviderを構成する必要はありません。Effectの資源は親graphが保持し、Compileでplanへ移譲します。

レンダラーは生成時の世代を固定し、prepared passesはGPU使用終了まで独立して保持します。
HotReload後やdevice復旧後は同じReferenceからレンダラーを生成し直します。
複数入力とEffectsを同じpublicationに揃える場合だけ、生成のsnapshot引数と
`parent.ImportResources().UseSnapshot(frameSnapshot)`へ同じSnapshotを渡します。
Snapshotはgraph構築まで保持します。親graphとレンダラー資源は同じphysical deviceを使ってください。

共有GPU資源の管理は汎用の`GraphicsResourceDefinition<T>`に従います。DrawingGraphicsResources2DとImageEffectGraphicsResourcesの公開キーに定義を登録すると、pipeline生成設定やsamplerも変更できます。module・pipeline・samplerはManager、フレームごとのbindingと一時画像は描画側/graphが所有します。

CreateRenderGraphRendererはDrawingResources2Dと一つのResourceLeaseを引き渡します。
元leaseのValueを記録前にも検証し、最後のprepared tokenが返った後にそのleaseをDisposeします。
pipeline用の別leaseやproviderは作りません。
