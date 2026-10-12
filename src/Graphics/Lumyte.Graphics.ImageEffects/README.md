# Lumyte.Graphics.ImageEffects

2Dと3Dで共有する、linear premultiplied RGBA画像のエフェクトDAGとRenderGraphパス生成。
すべての効果は不変の `ImageEffectDefinition` と `IImageEffectRenderer<TDefinition>` の組で表します。
ライブラリが提供するColorMatrix・GaussianBlur・Opacity・Compositeも同じ仕組みを使います。

```csharp
var compiler = new ImageEffectCompiler(device);
var effect = compiler.Effect(definition, renderer, ImageEffectGraph.Source());
var owner = compiler.AppendPasses(graph, effect, source);
```

定義は入力数・出力範囲・各入力の必要領域を計算し、rendererは任意のrender／compute／copy passを追加します。
入力は単一mip/layerの `Rgba16Float`、原点と範囲は `EffectImage.Bounds` で保持します。
子graphは0個以上で、0入力では画像生成もできます。出力は定義の範囲と
`EffectPreparationContext.ColorSemantic` を保持します。共有nodeは一度だけ展開します。

提供する4種類もcompilerの拡張メソッドで組み立てます。rendererを省略すると提供実装を新しく生成し、
直接渡すとそのrendererをgraph構築時に固定します。設定用のDIパッケージや登録APIはありません。
新しい効果は定義・rendererとcompilerをreceiverにした拡張メソッドを追加して提供できます。

```csharp
var faded = compiler.Opacity(0.5f, effect, new DefaultOpacityEffectRenderer());
var blurred = compiler.GaussianBlur(3, 3, faded);
var owner = compiler.AppendPasses(graph, blurred, source);
```

拡張メソッドはgraphの組立だけを行います。GPUパスはAppendPassesが追加し、compilerはSubmitしません。
spanをcallbackへ保持せず、必要なhandle・値をコピーし、固定GPU資源は `context.Own` へ移してください。
呼び出し側がSubmitと完了待機を行い、commandとcompiled planの解放後に `PreparedImageEffect.Dispose()` を呼びます。
失敗したAppend後はRenderGraph全体を破棄してください。

LUT・畳み込み・morphologyなども同じ仕組みで段階的に追加できます。
設計判断、公開APIと演算・領域・所有権の詳細契約は [GRAPHICS-0016](../../../docs/adr/graphics/GRAPHICS-0016-image-effects.md) を参照してください。
