# Lumyte.Graphics.ImageEffects

2Dと3Dで共有するlinear premultiplied RGBA画像のエフェクトとRenderGraphパス生成。
すべての効果はImageEffectDefinitionを継承し、パラメーター・領域計算・固定のAppendPassesを持ちます。
提供するColorMatrix・GaussianBlur・Opacity・Compositeも同じ契約を使います。

```csharp
var compiler = new ImageEffectCompiler(device);
var blur = new GaussianBlurEffect(3, 3);
var opacity = new OpacityEffect(0.5f);
var blurred = compiler.AppendGaussianBlur(graph, blur, source);
var faded = compiler.AppendOpacity(graph, opacity, blurred.Output);
```

Append拡張メソッドは作成済みの効果と入力画像を受け取り、パスだけを追加します。
効果やDAGを生成しません。レンダラーの差し替え・DI登録はなく、処理方式を変えるときは新しい効果型を追加します。
利用者はImageEffectDefinitionの領域計算とAppendPassesを実装し、独自のAppend拡張メソッドから
compiler.AppendEffect(graph, effect, inputs)を呼べます。

再利用するDAGや2Dへの適用には、別途明示的に効果とgraphを組み立てます。

```csharp
var effect = ImageEffectGraph.Create(blur, ImageEffectGraph.Source());
var owner = compiler.AppendPasses(graph, effect, source);
```

入力は単一mip/layerのRgba16Floatで、原点と範囲をEffectImage.Boundsに保持します。
入力数・領域・色semanticを検証し、共有nodeは一度だけ展開します。0入力の画像生成も可能です。
DAG経路ではSourceのsemanticを、直接AppendEffectの0入力生成ではDisplayLinearを使います。

効果は複数のrender／compute／copy passを追加できます。spanをcallbackに保存せず、
必要な値をコピーし、固定GPU資源はcontext.Ownへ移してください。
呼び出し側がCompile・Record・Submitと完了待機を行い、commandとcompiled planの解放後に
すべてのPreparedImageEffectをDisposeします。Append失敗後はRenderGraph全体を破棄してください。

設計と公開APIは [GRAPHICS-0016](../../../docs/adr/graphics/GRAPHICS-0016-image-effects.md) を参照してください。
