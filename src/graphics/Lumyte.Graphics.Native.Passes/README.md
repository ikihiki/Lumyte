# Native image passes

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
