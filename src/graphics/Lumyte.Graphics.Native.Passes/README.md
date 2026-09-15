# Native feature passes

`NativeRenderPassRegistry.AddImageProcessing()` が Clear／Texture Copy／Output を登録します。
Clear と Copy は Native command だけを使い、shader の作成を要求しません。
Output は fullscreen raster で Linear／sRGB と Opaque／Premultiplied を処理し、sRGB attachment の
自動符号化と二重に encode しないようにします。root は compiler reflection で生成した Native の
構造体を直接渡します。

`ImageProcessing/Shaders/Output.slang` は Native offline compiler で DXIL／SPIR-V と package factory を
生成します。通常の実行時に shader compiler、ファイル loader や shader 管理は不要です。
build 時は Slang 2026.17 を `LUMYTE_SLANGC`／`LumyteNativeSlangCompiler` で指定します。
この workspace の実験用 pinned compiler も既定候補です。DXC は NuGet package の build 専用依存です。

単体テストは隣接する `Lumyte.Graphics.Native.Passes.Tests`、実機の画素・Host・presentation 適合試験は
DirectX12／Vulkan の `Integration/RenderGraph/` に置き、両方が同じコンパイル済み
`Lumyte.Graphics.RenderGraph.Conformance` の consumer を使います。DirectX 12 の画素試験は、
Windows Graphics Tools の debug layer がない環境でも実行できるよう通常の device を使います。

## 2D 描画

`NativeRenderPassRegistry.Add2DRendering()` は `lumyte.draw.2d` version 1 を登録する。利用者は共通の `Draw2DScene` を `graph.Add2DPass(...)` へ渡す。GenericHost では `Lumyte.Graphics.Passes.Hosting` の `Add2DRendering()` が両系統の本体を登録する。

`TwoD/` の本体が CPU scene を Native 専用の path／paint data へ変換し、統一メモリ、Bindless descriptor と直接 root data で GPU coverage と合成を実行する。`TwoD/Shaders/TwoD.slang` は build 時に DXIL／SPIR-V と package factory を作る。実行時に caller が shader や glyph atlas を用意する必要はない。画像の decode と文字の shaping は外部で済ませた upload data を受け取る。

未変更 scene、draw data と画像は bounded cache で共有し、提出中の使用は Resources／RenderGraph が保持する。clip、layer、blur、mask、shadow と backdrop の依存は内部 graph に展開する。現在は描画要素ごとの全画面 pass と中間画像を使う最初の経路で、tile による除外や複数要素の batch 化、glyph atlas は未実装である。保持 scene の再利用だけで 60 FPS を保証しない。

2D の実機試験は `Integration/TwoD/` に分離し、DirectX 12／Vulkan の検証レイヤーを有効にして [SkiaSharp 参照](../Lumyte.Graphics.RenderGraph.Conformance/TwoD/README.md) と比較する。CPU API、対象 format と残る最適化は [ADR 0036](../../../docs/adr/0036-2d-render-passes.md) を参照。

`NativeRenderPassRegistry.Add2DRendering()` は共通 `Draw2DPassContract` の本体を登録します。
利用側は `Draw2DScene` を `graph.Add2DPass` に渡します。図形、曲線、全 cap／join と dash、gradient、
入れ子の clip／layer、画像、準備済み glyph を Native の graph に展開します。元の layer と backdrop、
Gaussian blur の各軸、mask、影は依存のある内部 pass として記録します。

`TwoD/` は Native 用の曲線分割、stroke の輪郭化、scene data と image の準備を所有します。
Slang shader は 8×8 の geometric coverage と画素中心の paint を使い、線形 premultiplied 色で合成します。
root は descriptor index と操作値を直接渡し、scene buffer は通常の描画データだけを保持します。
SkiaSharp は本番経路に参照しません。

準備済み scene を最大 32、描画データを 1,024、画像を 64 個保持します。内容と transform／clip が同じ
leaf は更新後の scene でも GPU data を共有します。追い出した cache の資源は提出済み execution の
使用保持が終わるまで回収されません。CPU scene は cache 回収後にも再準備できます。
atlas packing や tile ごとの大量 path の最適化は現時点の描画方式ではなく、後続の性能改善です。

2D の実機比較は DirectX12／Vulkan の `Integration/TwoD/` にあり、共通 consumer の scene を
SkiaSharp の独立した図形・path・paint 操作で作る比較画像と照合します。検証 layer のメッセージも確認します。
