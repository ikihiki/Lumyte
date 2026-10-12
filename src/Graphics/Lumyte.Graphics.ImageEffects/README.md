# Lumyte.Graphics.ImageEffects

2Dと3Dで共有する、linear premultiplied RGBA画像のエフェクトDAGとRenderGraphパス生成。
すべての効果は不変の `ImageEffectDefinition` と `IImageEffectRenderer<TDefinition>` の組で表します。
ライブラリが提供するColorMatrix・GaussianBlur・Opacity・Compositeも同じ仕組みを使います。

```csharp
var effect = ImageEffectGraph.Create(definition, renderer, ImageEffectGraph.Source());
var compiler = new ImageEffectCompiler(device);
var owner = compiler.AppendPasses(graph, effect, source);
```

定義は入力数・出力範囲・各入力の必要領域を計算し、rendererは任意のrender／compute／copy passを追加します。
入力は単一mip/layerの `Rgba16Float`、原点と範囲は `EffectImage.Bounds` で保持します。
子graphは0個以上で、0入力では画像生成もできます。出力は定義の範囲と
`EffectPreparationContext.ColorSemantic` を保持します。共有nodeは一度だけ展開します。

提供する4種類の便利factoryも `Create` を呼びます。rendererを省略すると提供実装を新しく生成し、
明示的に渡すとそのrendererを構築時に固定します。新しい効果は定義・renderer・必要ならfactoryを追加して提供でき、
compilerの変更や固定登録表を必要としません。

```csharp
var renderer = scope.ServiceProvider.GetRequiredService<IImageEffectRenderer<OpacityEffect>>();
var effect = ImageEffectGraph.Create(new OpacityEffect(0.5f), renderer, ImageEffectGraph.Source());
// 同じ組立経路を使う便利factory。
var same = ImageEffectGraph.Opacity(0.5f, ImageEffectGraph.Source(), renderer);
```

DIなしでもrendererを直接生成して同じCreateへ渡します。型の探索、CanHandle、記録中のDI解決はありません。
spanをcallbackへ保持せず、必要なhandle・値をコピーし、固定GPU資源は `context.Own` へ移してください。
呼び出し側がSubmitと完了待機を行い、commandとcompiled planの解放後に `PreparedImageEffect.Dispose()` を呼びます。
失敗したAppend後はRenderGraph全体を破棄してください。

LUT・畳み込み・morphologyなども同じ仕組みで段階的に追加できます。
設計判断、公開APIと演算・領域・所有権の詳細契約は [GRAPHICS-0016](../../../docs/adr/graphics/GRAPHICS-0016-image-effects.md) を参照してください。
