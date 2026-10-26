# Lumyte.Graphics.ImageEffects

2Dと3Dで共有するlinear premultiplied RGBA画像処理を、RenderGraphの拡張メソッドとして提供します。
すべての効果はImageEffectDefinitionを継承し、パラメーター・領域計算・固定のAppendPassesを持ちます。
提供するColorMatrix・GaussianBlur・Opacity・Compositeも同じ契約を使います。

```csharp
var blurred = graph.AppendGaussianBlur(source, 3, 3);
var faded = graph.AppendOpacity(blurred.Output, 0.5f);
```

RenderGraphのAppend拡張メソッドは入力画像と数値・行列などのパラメーターを受け取り、パスだけを追加します。
効果やDAGを返しません。利用者が効果をnewする必要はありません。レンダラーの差し替え・DI登録はなく、処理方式を変えるときは新しい効果型を追加します。
利用者はImageEffectDefinitionの領域計算とAppendPassesを実装し、独自のAppend拡張メソッドから
graph.AppendEffect(effect, inputs)を呼べます。

再利用するDAGや2Dへの適用には、別途明示的に効果とgraphを組み立てます。

```csharp
var blur = new GaussianBlurEffect(3, 3);
var effect = ImageEffectGraph.Create(blur, ImageEffectGraph.Source());
var owner = graph.AppendEffects(effect, source);
```

入力は単一mip/layerのRgba16Floatで、原点と範囲をEffectImage.Boundsに保持します。
入力数・領域・色semanticを検証し、共有nodeは一度だけ展開します。0入力の画像生成も可能です。
DAG経路ではSourceのsemanticを、直接AppendEffectの0入力生成ではDisplayLinearを使います。

効果は複数のrender／compute／copy passを追加できます。spanをcallbackに保存せず、
必要な値をコピーし、固定GPU資源はcontext.Ownへ移してください。
呼び出し側がCompile・Record・Submitと完了待機を行い、commandとcompiled planの解放後に
すべてのPreparedImageEffectをDisposeします。Append失敗後はRenderGraph全体を破棄してください。

設計と公開APIは [GRAPHICS-0017](../../../docs/adr/graphics/GRAPHICS-0017-image-effects.md) を参照してください。

画像処理はRenderGraph.Deviceの借用deviceを使い、CreateTexture・AddPass・UseTextureで宣言します。
画像・依存順・barrier・一時allocationはRenderGraphが管理します。別のcompilerや実行器はありません。
ImageEffectGraph.GetRequiredSourceBounds(output)は遅延設定のCPU上の領域照会として使えます。

ImageEffectSubgraphは親graphとSource画像を持つ部分ビューです。レイヤー等のコールバックで
scope.AppendGaussianBlur(scope.Source, 3, 3)を呼ぶとEffectImageを返し、固定資源のownerはscopeが保持します。
独立したgraphやplanは生成せず、すべての処理を親graphへ追加します。

## 外部からロードしたシェーダ

`AppendEffects(graph, source, shaders)`または`AppendEffectWithShaders(effect, shaders, inputs)`へ、`ImageEffectShaders`を渡せます。二つのartifactはfullscreen vertexとeffect fragmentの既存shader ABIを満たす必要があります。省略した既存APIはDLL埋め込み版を使い、共通artifactは初回だけロードします。

直接ロードでもResourceManagerからの取得でも同じ型を使います。ResourceManagerとの接続には、任意の[Lumyte.Resources.Graphics.ImageEffects](../Lumyte.Resources.Graphics.ImageEffects/README.md)が既定シェーダの取得とEffectの所有をまとめます。本パッケージ単独の利用にResourcesは不要です。

手動で接続する場合はartifactのLeaseをGPU処理とprepared effectの解放まで保持してください。`compiled.Retain(prepared)`へ移譲した後は、PreparedImageEffectを別のusingやDisposeから解放しません。artifactのLeaseをplanへ渡した後にprepared effectをRetainすれば、固定GPU資源を先に解放できます。固定資源の解放が失敗した場合は依存側の解放を止め、同じownerで再試行できます。

連携ライブラリは`EffectPreparationContext`の所有を先に確立し、`AppendEffectsWithContext`または`AppendEffectWithContext`で画像だけを受け取れます。構築途中で失敗してもcontextにcleanupが残るため、解放自体の失敗も再試行できます。失敗したcontextでの追加や記録は拒否します。

`ImageEffectShaders`には`IGraphicsShader`を二つ渡すconstructorもあります。同じdevice上の既存moduleを借用し、各passでのmodule再生成を避けます。prepared effectは借用moduleをDisposeしません。直接所有する側、またはResourceManagerのLeaseが、GPU完了とprepared effect解放まで保持します。
