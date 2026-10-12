# Lumyte.Graphics.ImageEffects

2Dと3Dで共有する、linear premultiplied RGBA画像のエフェクトDAGとRenderGraphパス生成。

`ImageEffectGraph` の `Source`、`ColorMatrix`、`GaussianBlur`、`Opacity`、`Composite` を組み合わせ、
`ImageEffectCompiler.AppendPasses` で呼び出し側のRenderGraphへ追加します。
入力は単一mip/layerの `Rgba16Float`、結果の座標は `EffectImage.Bounds` が保持します。
Gaussian blurはdevice pixel単位の分離カーネルで、各軸の半径は `ceil(3 * sigma)` です。

`ImageEffectRenderers` は演算ごとに実装を固定します。`CreateDefault()` はDIなしで標準実装を生成します。
各準備結果のパスは一度だけ記録できます。呼び出し側がSubmitと完了待機を行い、
コマンドバッファとコンパイル済みRenderGraphを破棄した後で `PreparedImageEffect.Dispose()` を呼びます。
失敗したAppend後はRenderGraph全体を破棄してください。

設計判断、公開APIと演算・領域・所有権の詳細契約は [GRAPHICS-0016](../../../docs/adr/graphics/GRAPHICS-0016-image-effects.md) を参照してください。
