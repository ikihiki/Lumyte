# Lumyte.Graphics.ImageEffects

2Dと3Dで共有する、linear premultiplied RGBA画像のエフェクトDAGとRenderGraphパス生成。

`ImageEffectGraph` の `Source`、標準4演算（`ColorMatrix`、`GaussianBlur`、`Opacity`、`Composite`）、利用者定義の `Custom` を組み合わせ、
`ImageEffectCompiler.AppendPasses` で呼び出し側のRenderGraphへ追加します。
入力は単一mip/layerの `Rgba16Float`、結果の座標は `EffectImage.Bounds` が保持します。
Gaussian blurはdevice pixel単位の分離カーネルで、各軸の半径は `ceil(3 * sigma)` です。

`ImageEffectRenderers` は演算ごとに実装を固定します。`CreateDefault()` はDIなしで標準実装を生成します。
各準備結果のパスは一度だけ記録できます。呼び出し側がSubmitと完了待機を行い、
コマンドバッファとコンパイル済みRenderGraphを破棄した後で `PreparedImageEffect.Dispose()` を呼びます。
失敗したAppend後はRenderGraph全体を破棄してください。

設計判断、公開APIと演算・領域・所有権の詳細契約は [GRAPHICS-0016](../../../docs/adr/graphics/GRAPHICS-0016-image-effects.md) を参照してください。

利用者定義のエフェクトは、不変recordの `ImageEffectDefinition` と
`IImageEffectRenderer<TDefinition>` の組で追加します。定義は入力数・出力範囲・入力ごとの必要領域を計算し、
rendererはRenderGraphへ任意のrender／compute／copyパスを宣言します。

```csharp
var custom = ImageEffectGraph.Custom(definition, renderer, ImageEffectGraph.Source());
var combined = ImageEffectGraph.GaussianBlur(3, 3, custom);
var owner = compiler.AppendPasses(graph, combined, source);
```

独自rendererは通常のDIサービスとして登録しても、直接生成しても使えます。
標準4演算の登録表やcompilerを変更する必要はありません。定義とrendererはgraphの構築時に固定します。
子graphは0個以上で、0入力では画像生成もできます。出力は定義が宣言した範囲と
`EffectPreparationContext.ColorSemantic` を保持します。子列のspanをcallbackへ保持せず、
必要なhandle・値をコピーし、固定GPU資源は `context.Own` へ移してください。

新しい効果はこの拡張経路で追加し、LUT・畳み込み・morphologyなどの標準効果も段階的に増やせます。
画面への適用は2D側のレイヤー・背景API、または直接RenderGraphへの追加で行います。
