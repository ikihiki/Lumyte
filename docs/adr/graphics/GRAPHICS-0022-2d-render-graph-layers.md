# ADR-GRAPHICS-0022: RenderGraph用2Dレイヤーと画像エフェクトの部分グラフ接続

- 状態: 採用
- 日付: 2026-10-26

## 背景

2Dの内容全体をぼかす、group opacityを適用する、直前の背景へ効果を掛ける処理には画像化と複数passが必要になる。一方、純粋な2D描画をRenderGraph必須にはしたくない。親の3D・後処理と同じgraphで依存・barrier・一時画像を管理し、効果前後のclipと描画順を維持する必要がある。

基本描画と資源所有は[GRAPHICS-0018](GRAPHICS-0018-2d-graphics-engine.md)、追加命令は[線・パス](GRAPHICS-0019-2d-paths-and-strokes.md)、[画像・mesh](GRAPHICS-0020-2d-images-and-meshes.md)、[文字向け形式](GRAPHICS-0021-2d-glyph-representations.md)を参照する。本ADRはそれらを親graphへ接続する判断を扱う。RenderGraphのcompile/同期は[GRAPHICS-0015](GRAPHICS-0015-render-graph.md)、効果の定義・Append拡張は[GRAPHICS-0017](GRAPHICS-0017-image-effects.md)へ委ねる。

## 決定

### 通常描画とレイヤーCanvasを型で分ける

```text
通常: Canvas2D → DrawList2D → Renderer2D → command buffer

graph: RenderGraphCanvas2D → RenderGraphDrawList2D → RenderGraphRenderer2D
                                              ↓ 親graph.Append2D(renderer)
                  同じ親graph内の通常batch・中間画像・effect・composite pass
                                              ↓ 親graph.Compile / plan.Record
```

通常のCanvas2DにBeginLayer・EndLayer・DrawBackdropEffectを追加しない。共通の変換・clip・描画操作はDrawingCanvas2Dを継承し、RenderGraphCanvas2Dだけが構造命令を記録する。Endの戻り値も別型とし、レイヤー記録を通常Renderer2Dへ渡せないようにする。

`Lumyte.Graphics.Drawing2D.RenderGraph`はDrawing2D・RenderGraph・ImageEffectsへ依存する別パッケージとする。内部に別のgraphやcompiled planを作らず、親graphへpassを追加する。親への3D pass追加、MarkOutput/Export、Compile、Submit、完了確認はアプリが行う。レイヤー・背景はホストが展開する構造命令であり、通常11命令のrenderer interfaceを増やさない。

### 記録・準備・実行の三段階

記録中のBeginLayerはoptions、変換、親clipをsnapshotし、ContentBoundsのclipだけを持つ子状態へ切り替える。親clipを入力描画へ持ち込まない。EndLayerは子のSaveが全てRestoreされていることを検証し、親状態を復元する。内側Restoreで外側Saveを消費できないようSaveBoundaryを設ける。Endは未完了のlayer/Saveを拒否し、Cancelは全構造を破棄する。

Append2Dはtargetとgraph/bindingsの一致を検証し、bindingsをsnapshotする。画像・mask・距離場・paintの全nodeを走査して外部画像のbindingを確認し、記録順を辿って通常命令の連続batch、layer、backdropを展開する。Effects delegateは記録時には実行せず、非空layerまたは背景の展開時に一回だけ呼ぶ。closureの値は利用側が不変に保つ。同じDrawListを次のframeへ追加する場合は新たなAppendと準備ownerを作る。

Recordは追加済みcallbackだけを実行し、追加依存や未宣言の一時資源を作らない。先頭のKeepAlive passが準備ownerの一回実行を検査する。記録中の失敗も実行済みとして扱い、同じpreparedを再Recordしない。

### layerの画像化とclipの順序

| 段階 | 処理と範囲 |
| --- | --- |
| 入力 | ContentBoundsを現在変換して有限のpixel外接矩形を外向きに丸め、透明のRgba16Float中間画像を作る |
| 内容描画 | ContentBoundsの幾何clipとlayer内で追加したclipを適用する |
| Effects | 画像をSourceとして渡し、効果で拡張したBoundsと原点を保持する |
| group opacity | 合成済み画像全体へ一回適用する |
| 出力 | 保存した親clip、OutputClip、親target範囲を適用しSrcOverする |

ContentBoundsはcullingヒントではなく、効果前の幾何clipである。最終target外の内容も効果で画面内へ影響し得るため、入力を親targetへ先に切らない。効果で広がった出力をContentBoundsで再度切らない。空boundsでは0寸法textureを生成せず、出力を省略する。親clipのcoverageは出力時に一回だけ適用する。

4×4 sampleの相関を保つのは同じ幾何段階内であり、入力のclipと画像化後のclipは別演算となる。同じ境界でも入力0.5×出力0.5が0.25になり得る。group opacityは子へ分配せず、例えば不透明な子が重なった画像でもOpacity=0.5なら重なりのalphaは0.5となる。

PixelOriginはルートtargetと共通のdevice pixel原点で、画像のBoundsには負の原点も残す。sample時にlocal画像座標とglobal pixelを対応付ける。効果のsigmaや移動量はdevice pixel単位であり、CanvasのPixelScaleから自動変換しない。論理座標で同じ幅を求めるアプリは明示変換する。Gaussianは出力pixelのX/Y軸で評価し、Canvasの回転・せん断でkernelの向きを暗黙に変えない。

### Effectsを親RenderGraphの部分ビューとして渡す

LayerOptions2D.EffectsとBackdropEffectOptions2D.Effectsは `Func<ImageEffectSubgraph, EffectImage>` とする。部分ビューは親Graphの借用参照と、その時点の入力Sourceを持つ。利用側は `Effects = graph => graph.AppendGaussianBlur(graph.Source, 3, 3)` のように直接パラメーターを渡す。layerのnullはidentity、backdropのnullは入力不備とする。

Append拡張は親と同じgraphへpassを追加し、EffectImageを返す。利用側へ独立した効果DAGの構築、効果オブジェクトのnew、独自Compile/Recordを要求しない。カスタム効果も同じ拡張とImageEffectDefinitionの契約を使い、標準/カスタムの区別やDIでの効果renderer差し替えを設けない。別方式が必要なら新しい効果として追加する。

2Dから渡すSourceはDisplayLinearのlinear premultiplied Rgba16Floatで、有限Boundsと原点を保持する。返された画像は非空・同じ色semanticでなければならず、利用側は同じgraphのhandleを返す。coverage・距離場をそのまま色画像として効果へ渡さず、先に共通paintで描く。

Effectsの固定GPU資源・カスタム準備資源は親graphのlifetime機構が保持し、Compile成功時にplanへ移譲する。ImageEffectSubgraph自体の寿命は2Dの準備ownerが保持する。2Dが独自の効果資源Managerを持つことはない。ResourceManagerのEffects接続は親graph側のAddImageEffects(manager)で構成する。

### backdropの取得と置換

DrawBackdropEffectは、その位置までに描いた現在layerのtargetだけをsnapshotする。祖先target、後続命令、自分自身の結果は含めない。targetはRenderAttachment | Sampledと有効内容を要求し、Clearまたは有効なLoadから開始する。Rgba8Srgbのsnapshotはdecode済みpremultiplied色をRgba16Floatへ描き、異形式のraw copyやalphaの再乗算で代用しない。

任意のEffectsコールバックから必要入力ROIを逆算できないため、現在targetの有限領域全体を一回取得する。OutputBoundsで入力を先に切らず、ぼかしや移動の参照元を残す。取得範囲外は透明とし、計画用にdelegateを再実行しない。必要ROIの宣言は将来の最適化とする。

OutputBoundsと現在clipのcoverageをcとして、`result = effect * c + original * (1 - c)`で置換する。originalもsnapshotから読み、attachmentのblendを無効にして書く。通常のSrcOverでは半透明の背景を二重合成してしまう。coverageが0ならdiscardし、clip外のtarget内容を保持する。依存順は先行描画→snapshot→effect→置換→後続描画となる。

### 通常batchとgraph compositorの二つの接続

通常11命令は既存の専用rendererへ配送し、返されたIPreparedCommandBatch2Dを一つのopaque passとして追加する。DeclareResourcesをgraphのUseへ変換し、私有uploadや内部barrierはbatchが記録する。通常rendererをgraphで再使用するための境界であり、内部の各dispatchをgraphが個別に最適化する契約ではない。

layer合成とbackdropのsnapshot/置換にはPreparedGraphGpuBatch2Dを使う。この経路はupload passとdraw pass、geometryの一時buffer、補助textureを親graphへ明示する。instructionとuploadは準備owner、graphの一時buffer・texture・生成viewはplanが所有する。drawing pipelineとsamplerは基本描画のDrawingResourceState2Dを共有し、artifactはpipelineのfragmentから取得する。

外部textureは親allocationごとに一度だけimportし、DrawingGraphBindings2Dへ同じhandleをBindする。viewごとに同じallocationを重複importしない。buffer/shader-dataもtyped handleへ対応付ける。実際の入口state/scopeを渡し、read-only importでも外部producerから最初の利用stageへの可視性を保証する。後でbindingsを変更しても追加済みcallbackの入力は変えない。

Load、部分更新、SrcOverはColorRead | ColorWriteを宣言し、全体ClearだけColorWriteにできる。初期RenderGraphのallocation全体の同期へ従い、同じallocationのsampleとattachmentの同時使用は拒否する。opaque batchは宣言した入口stateへ戻し、別exit stateを要求する経路はpassを分ける。未宣言アクセスを順序依存だけで補わない。

MarkOutputは結果の保持、Exportはさらにfinal state/scopeの指定に使う。DependsOnは順序、KeepAliveは副作用によるpass保持であり、resource accessの宣言やExportの代用ではない。exportしない画像が自動でimport時の状態へ戻るとはみなさない。

### 所有権・失敗・device世代

| 所有者 | 保持対象 |
| --- | --- |
| アプリ | import資源と親allocation、command、submission、完了確認 |
| PreparedDrawingPasses2D | CPU snapshotを参照するbatch、table・staging・instruction等の固定資源、部分ビュー |
| CompiledRenderGraph | graphが確保した一時allocation・生成view、graph lifetimeから移譲したEffects資源 |
| rendererとbatchの内部token | DrawingResources2Dの元所有者または一つのResourceLease |

準備ownerもcompiled planもGPU完了まで保持する。通常の終了順は完了確認→command→plan→prepared owner→submissionで、その後renderer・Reference・DI scopeを返す。既存preparedが内部tokenを持つ場合はrendererを先に返せるが、新規Appendはできない。後続submissionが出力画像を使うなら元planもその完了まで保持する。DisposeでGPUのwaitや完了照会を行わない。

Appendが失敗・取消した場合、新規の2D準備ownerは内部cleanupし、変更途中のgraph全体をアプリが破棄する。先行する成功preparedは利用側が保持・解放し、graphが持つEffects資源はgraph/planの破棄で返す。Compile失敗はGPU記録前なのでpreparedを解放できる。Record失敗ではcommandを先に破棄し、再記録しない。Submit後のWait取消やFailedをGPU終了とみなさない。

Resources.Graphics.Drawing2D.RenderGraphは別の接続パッケージとし、同じRenderer2DResourceのReferenceから一つのleaseを取得してCreateRenderGraphRendererへ移譲する。pipeline/samplerを別経路で取得し直さない。graph compositorはlifetime検証をplanへ接続し、通常batchはRecord直前に元leaseを検査する。HotReloadは次のrenderer生成、deviceロストは使用停止・退役・復旧・Resources再構築後の再生成で扱い、古いview/bindingsも作り直す。

## 検討した代替案

| 代替案 | 採用しない理由 |
| --- | --- |
| 通常Canvasへlayerを追加して内部graphを生成 | graphなしの描画境界と、親graph全体の最適化を損なう |
| 先に独立した効果DAGを作り2Dへ渡す | 親graphと別の構築・compile契約が必要になる |
| 親clipを入力画像へ先に適用 | clip外から広がるぼかしや影が欠ける |
| group opacityを子の命令へ分配 | 子の重なりでalphaが変わる |
| 背景効果を通常のSrcOverで合成 | 半透明の元背景が二重合成される |
| OutputBoundsだけsnapshotする | 任意の効果が読む領域を取りこぼす |

## 結果と影響

直接描画の依存を小さく保ち、layerと効果を親の3D・後処理と同じgraphへ組み込める。画像の原点と入力/出力clipを分けるため、画面外からの影や背景効果の順序を保持できる。

一方、中間画像と全targetの背景snapshotはメモリと帯域を使う。初期は単一queueで、内部Submit/Waitや非同期compute schedulingを追加しない。ROI宣言、subresource同期、pass融合、pool/alias、複数queueはRenderGraph側と整合させて別途設計する。

## 検証方針

CanvasSeparationTestsで通常描画の依存とAPI、delegateの遅延実行と同じ親graph、失敗時の資源保持を確認する。ModelTestsでnested layerとSave境界、optionsのsnapshotを確認する。ExtensionDispatchTestsで通常rendererの再利用、資源宣言、opaque batchの一回実行、Append失敗を確認する。GPUテストでは前後clip、画面外入力、group opacity、原点、半透明背景の置換、shader世代の保持をVulkan/wgpuで画素比較する。実測のテスト件数と結果はPRへ記録する。

## API・利用例

比較元はsplit/2d-glyph-formats。RenderGraphBuilderはLumyte.Graphics.RenderGraph.RenderGraphのaliasであり、新規型ではない。ImageEffectSubgraphとAppend効果のAPIはEffects ADRを参照する。

```diff
+namespace Lumyte.Graphics.Drawing2D.RenderGraph
+{
+    public sealed class RenderGraphCanvas2D : DrawingCanvas2D
+    {
+        public void BeginLayer(LayerOptions2D options);
+        public void EndLayer();
+        public void DrawBackdropEffect(BackdropEffectOptions2D options);
+        public RenderGraphDrawList2D End();
+    }
+    public sealed class RenderGraphDrawList2D;
+    public sealed record LayerOptions2D
+    {
+        public required Rect2D ContentBounds { get; init; }
+        public Func<ImageEffectSubgraph, EffectImage>? Effects { get; init; }
+        public float Opacity { get; init; } = 1;
+        public Rect2D? OutputClip { get; init; }
+    }
+    public sealed record BackdropEffectOptions2D
+    {
+        public required Rect2D OutputBounds { get; init; }
+        public required Func<ImageEffectSubgraph, EffectImage> Effects { get; init; }
+    }
+    public sealed record RenderTargetBinding2D : DrawingTargetInfo2D
+    {
+        public required RenderGraphTexture Texture { get; init; }
+        public TextureSubresourceRange Range { get; init; } = new(0, 1, 0, 1);
+    }
+    public sealed class DrawingGraphBindings2D
+    {
+        public DrawingGraphBindings2D(RenderGraphBuilder graph);
+        public void BindTexture(IGraphicsTexture texture, RenderGraphTexture resource);
+        public RenderGraphTexture GetTexture(IGraphicsTexture texture);
+        public void BindBuffer<T>(IGraphicsBuffer<T> buffer, RenderGraphBuffer<T> resource) where T : unmanaged;
+        public RenderGraphBuffer<T> GetBuffer<T>(IGraphicsBuffer<T> buffer) where T : unmanaged;
+        public void BindShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer,
+            RenderGraphShaderDataBuffer<T> resource) where T : struct, IShaderData;
+        public RenderGraphShaderDataBuffer<T> GetShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer)
+            where T : struct, IShaderData;
+    }
+    public sealed class RenderGraphRenderer2D : IDisposable
+    {
+        public RenderGraphRenderer2D(DrawingCommandRenderers2D renderers, RenderGraphDrawList2D list,
+            RenderTargetBinding2D target, DrawingGraphBindings2D bindings, DrawingResources2D? resources = null, IDisposable? resourceOwner = null, Action? validateResources = null);
+        public void Dispose();
+    }
+    public static class Drawing2DRenderGraphExtensions
+    {
+        public static ValueTask<PreparedDrawingPasses2D> Append2D(this RenderGraphBuilder graph,
+            RenderGraphRenderer2D renderer, CancellationToken cancellationToken = default);
+    }
+    public sealed class PreparedDrawingPasses2D : IDisposable
+    {
+        // batch・effect部分ビュー・compositorの固定資源を所有。親graphは所有しない。
+        public void Dispose();
+    }
+}
```

```diff
+namespace Lumyte.Resources.Graphics.Drawing2D.RenderGraph
+{
+    public static class ResourceGraphics2DRenderGraphExtensions
+    {
+        public static RenderGraphRenderer2D CreateRenderGraphRenderer(
+            this ResourceReference<Renderer2DResource> reference,
+            DrawingCommandRenderers2D renderers, RenderGraphDrawList2D list,
+            RenderTargetBinding2D target, DrawingGraphBindings2D bindings,
+            ResourceSnapshot? snapshot = null);
+    }
+}
```

### layerと親graphへの接続

```csharp
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

using var parent = new RenderGraphBuilder(device, "frame");
var description = new TextureDesc
{
    Width = 512, Height = 256, Format = TextureFormat.Rgba16Float,
    Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled,
};
var target = new RenderTargetBinding2D
{
    Texture = parent.CreateTexture(description, "2d-output"),
    Description = description,
    LoadOp = AttachmentLoadOp.Clear,
};
using var renderer = new RenderGraphRenderer2D(
    DrawingCommandRenderers2D.CreateDefault(), list, target, new DrawingGraphBindings2D(parent));
using var drawing = await parent.Append2D(renderer);
parent.MarkOutput(target.Texture);
// 同じparentへ3Dや後処理を追加できる。
using (var plan = parent.Compile())
using (var commands = device.CreateCommandBuffer(new()))
{
    plan.Record(commands);
    commands.Finish();
    using var submission = device.Queue.Submit([commands]);
    await submission.WaitAsync();
}
// GPU完了、command、planの解放後にdrawingを解放する。
```

### 背景効果の記録

```csharp
// CanvasのBegin中。ここまでの現在layerだけを入力にする。
canvas.DrawBackdropEffect(new BackdropEffectOptions2D
{
    OutputBounds = new Rect2D(250, 20, 200, 100),
    Effects = graph =>
    {
        var blurred = graph.AppendGaussianBlur(graph.Source, 3, 3);
        return graph.AppendOpacity(blurred, 0.8f);
    },
});
```

### ResourceManagerの共有定義を使う

```csharp
// Manager構築前に2DとEffectsを同じ汎用リソース定義の仕組みで設定する。
services.UseGraphicsShaders<Renderer2DResource>(new("shaders/2d-v"), new("shaders/2d-f"));
services.UseGraphicsShaders<ImageEffectShaderResource>(new("shaders/effect-v"), new("shaders/effect-f"));
services.AddResources(resources => resources.UseSource<AppSource>()
    .AddShaderPipeline().AddGraphics2D().AddImageEffects());
services.AddResourceGraphics();

// deviceとExecutionの起動後、非同期取得を済ませる。
using var reference = await manager.AcquireRenderer2DAsync();
using var extensions = await new RenderGraphExtensionsBuilder()
    .AddResources(manager).AddImageEffects(manager).BuildAsync();
using var parent = new RenderGraphBuilder(device, extensions: extensions);
var graphTarget = new RenderTargetBinding2D
{
    Texture = parent.CreateTexture(description, "2d-output"),
    Description = description,
    LoadOp = AttachmentLoadOp.Clear,
};
var bindings = new DrawingGraphBindings2D(parent);
using var renderer = reference.CreateRenderGraphRenderer(commandRenderers, list, graphTarget, bindings);
using var drawing = await parent.Append2D(renderer);
// Compile・Record・Submit。GPU使用・command・planの解放後にdrawingを返す。
```
