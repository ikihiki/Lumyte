# Portable の機能 pass

`PortableRenderPassRegistry.AddImageProcessing()` が Clear／Texture Copy／Output、`Add2DRendering()` が共通の 2D 契約を登録する。GenericHost では `Lumyte.Graphics.Passes.Hosting` の `Add2DRendering()` が Native／Portable の双方を登録する。アプリケーションは共通 RenderGraph へ scene を渡し、backend の binding や shader を管理しない。

2D は `TwoD/PortableDraw2DPass.cs` が自身の CPU 準備、明示 binding、通常の buffer／texture、直接 root 入力を使う。Portable に Bindless や allocation heap を追加しない。組み込みの WGSL package を使う登録と、展開済み `PortableShaderPackage` を渡す登録がある。登録時点では GPU object を作らず、選択された runtime が必要なものを準備する。

保持 scene の共有 content、prepared packet と画像を bounded cache で再利用する。upload は内部 graph に登録し、結果が受理された内容世代だけを後続の cache hit として利用する。eviction は owner を返すが、提出済み使用の終了まで実体を保持する。shader、path／glyph data と中間 layer は本体が所有し、元 asset を再ロードしない。

現在の描画経路は図形／path／stroke の CPU 準備と GPU coverage、gradient、画像、layer／composite、準備済み text／distance field を含む。全画面の要素別 pass と中間 texture を使うため、tile 除外、batch 化、atlas、自動距離場生成は後続の最適化である。大きな scene の性能は workload ごとの測定が必要になる。

隣接する `Lumyte.Graphics.Portable.Passes.Tests` では fake backend の準備・再利用を確認し、WebGPU の `Integration/TwoD/` は同一 consumer assembly の描画を [SkiaSharp の参照画像](../Lumyte.Graphics.RenderGraph.Conformance/TwoD/README.md) と比較する。API と部分採用の範囲は [ADR 0036](../../../docs/adr/0036-2d-render-passes.md) を参照。
