# Lumyte.Graphics.ImageEffects

2Dと3Dで共有するlinear premultiplied RGBA画像処理を、RenderGraphの拡張メソッドとして提供します。
すべての効果はImageEffectDefinitionを継承し、パラメーター・領域計算・固定のAppendPassesを持ちます。
提供するColorMatrix・GaussianBlur・Opacity・Composite・PostProcessも同じ契約を使います。

```csharp
EffectImage blurred = graph.AppendGaussianBlur(source, 3, 3);
EffectImage faded = graph.AppendOpacity(blurred, 0.5f);
```

RenderGraphのAppend拡張メソッドは入力画像と数値・行列などのパラメーターを受け取り、パスだけを追加します。
効果やDAGを返しません。利用者が効果をnewする必要はありません。レンダラーの差し替え・DI登録はなく、処理方式を変えるときは新しい効果型を追加します。
利用者はImageEffectDefinitionの領域計算とAppendPassesを実装し、独自のAppend拡張メソッドから
graph.AppendEffect(effect, inputs)を呼べます。

再利用するDAGや2Dへの適用には、別途明示的に効果とgraphを組み立てます。

```csharp
var blur = new GaussianBlurEffect(3, 3);
var effect = ImageEffectGraph.Create(blur, ImageEffectGraph.Source());
EffectImage output = graph.AppendEffects(effect, source);
```

入力は単一mip/layerのRgba16Floatで、原点と範囲をEffectImage.Boundsに保持します。
入力数・領域・色semanticを検証し、共有nodeは一度だけ展開します。0入力の画像生成も可能です。
DAG経路ではSourceのsemanticを、直接AppendEffectの0入力生成ではDisplayLinearを使います。

効果は複数のrender／compute／copy passを追加できます。spanをcallbackに保存せず、
必要な値をコピーし、固定GPU資源はcontext.Ownへ移してください。
標準のAppendは画像だけを返し、準備開始前からRenderGraphが固定資源を所有します。
呼び出し側はgraph.Compile・Record・Submitと完了待機を行い、GPU完了後にcompiled planを解放します。
Compile前の放棄やAppend失敗ではgraphをDisposeし、解放に失敗した場合は同じ所有者から再試行します。
成功したCompileでgraphの構築は閉じるため、次のフレームは新しいgraphへ追加します。

設計と公開APIは [GRAPHICS-0017](../../../docs/adr/graphics/GRAPHICS-0017-image-effects.md) を参照してください。

画像処理はRenderGraph.Deviceの借用deviceを使い、CreateTexture・AddPass・UseTextureで宣言します。
画像・依存順・barrier・一時allocationはRenderGraphが管理します。別のcompilerや実行器はありません。
ImageEffectGraph.GetRequiredSourceBounds(output)は遅延設定のCPU上の領域照会として使えます。

ImageEffectSubgraphは親graphとSource画像を持つ部分ビューです。レイヤー等のコールバックで
scope.AppendGaussianBlur(scope.Source, 3, 3)を呼ぶとEffectImageを返し、親graphの拡張状態と所有権を共有します。
独立したgraphやplanは生成せず、すべての処理を親graphへ追加します。scope.Disposeはビューを閉じるだけで、固定GPU資源を解放しません。

## Graphの拡張としての利用

`graph.AppendEffects(effectGraph, source)`はgraph内で一つの`ImageEffectsGraphContext`を取得してパスを追加します。
2Dのコールバックや他の拡張からも同じメソッドを呼べます。拡張未登録でも既定の埋め込みシェーダで動き、
ResourceManagerは不要です。複数拡張を明示的に構成するときは、初期化時に次のように登録できます。

```csharp
using var extensions = await new RenderGraphExtensionsBuilder()
    .AddImageEffects()
    .Add(static graph => new AppLayerImports(graph))
    .BuildAsync();

using var graph = new RenderGraph(device, extensions: extensions);
EffectImage output = graph.AppendEffects(effectGraph, source);
graph.MarkOutput(output.Texture);
using var plan = graph.Compile();
// Record・Submitし、GPU完了後にplanをDisposeする。
```

`AppLayerImports`は利用者が作る拡張型の例です。シェーダをAppendへ渡す必要はなく、
BuildAsyncが返す構成は変更せずフレーム間で再利用し、factoryの生成結果はGraphごとに共有します。各Appendの準備ContextはGraphへ登録し、
すべての拡張の所有権をCompile時に同じPlanへ移します。

## 外部からロードしたシェーダ

`builder.AddImageEffects(shaders)`へ、取得済みの`ImageEffectShaders`を渡せます。二つのartifactはfullscreen vertexとeffect fragmentの既存shader ABIを満たす必要があります。省略した既定構成はDLL埋め込み版を使い、共通artifactは初回だけロードします。

直接ロードでもResourceManagerからの取得でも同じ型を使います。ResourceManagerとの接続には、任意の[Lumyte.Resources.Graphics.ImageEffects](../Lumyte.Resources.Graphics.ImageEffects/README.md)が既定シェーダの取得とEffectの所有をまとめます。本パッケージ単独の利用にResourcesは不要です。

直接渡したシェーダは借用です。外部のLeaseやmodule所有者を固定GPU資源より長く保持してください。独自の連携factoryでは、Leaseの寿命を先にgraph.Retainへ登録してからImageEffectsGraphContextを返すと、逆順解放で固定GPU資源を先に返せます。ResourceManagerとの接続パッケージはこの順序を引き受けます。

低水準の`AppendEffectsWithContext`または`AppendEffectWithContext`を使う場合は、呼び出す前にEffectPreparationContextをgraph.Retainへ渡すか、明示的な所有者で保持します。構築途中で失敗してもcontextにcleanupが残るため、解放自体の失敗も再試行できます。失敗したcontextでの追加や記録は拒否します。

`ImageEffectShaders`には`IGraphicsShader`を二つ渡すconstructorもあります。同じdevice上の既存moduleを借用し、各passでのmodule再生成を避けます。Effectの準備Contextは借用moduleをDisposeしません。直接所有する側、またはResourceManagerのLeaseが、GPU完了と固定GPU資源の解放まで保持します。

## HDR の色調整と tone mapping

`graph.AppendPostProcess(source, exposure: 1, saturation: 0.8f, vignette: 0.2f, toneMap: true)` は
露出・彩度・ビネットを処理します。縦横比は画像の範囲から求め、alpha を保ったまま straight RGB を処理して
premultiply し直します。alpha zero の RGB は zero です。DAG には `PostProcessEffect` を使用します。

`toneMap: true` の Reinhard mapping は SceneLinear 入力を要求し、DisplayLinear 出力へ変換します。
false なら入力 semantic を保持します。各効果は `GetOutputColorSemantic` で変換を宣言し、既定は入力を保持します。
合成の各入力は同じ semantic が必要です。単一 effect、DAG、subgraph で同じ規則を使います。
Resources で共有する場合も既存の `AddImageEffects(manager)` を使用します。
カスタム effect fragment は追加された operation 6 の処理にも対応してください。

DAG 内の各ノードには、その入力 semantic に合う EffectPreparationContext のビューを渡します。
tone mapping 後の独自効果でも AppendEffectWithContext への委譲が可能です。
ビューは固定資源の所有者と失敗状態を共有し、Graph／Plan がまとめて一度だけ解放します。
