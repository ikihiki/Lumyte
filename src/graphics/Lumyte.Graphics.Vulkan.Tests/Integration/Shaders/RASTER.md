# Native Vulkan raster fixtures

`NativeRaster.slang` と埋込み `.spv` は Native の raw shader ABI を検証する小さな入力である。
製品 backend の shader compiler 依存は追加しない。既存の legacy package 試験とは独立する。

## 再生成

repository root で Slang 2026.17 を使う。`SLANG_RUN_SPIRV_VALIDATION=1` は内蔵 SPIR-V
Tools validator を有効にする。Y の反転 option は指定せず、backend の負の viewport height
が clip の上向き Y と framebuffer の下向き Y を一度だけ変換する。

```powershell
$slangCompiler = 'artifacts/experiments/slang-wgsl-root/compiler/slang-2026.17/bin/slangc.exe'
$fixtureDirectory = 'src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/Shaders'
$env:SLANG_RUN_SPIRV_VALIDATION = '1'
$entries = @(
    @('vertexMain', 'NativeVertex'),
    @('pixelMain', 'NativePixel'),
    @('sampledMain', 'NativeSampledPixel'),
    @('drawArgumentsMain', 'NativeDrawArguments'),
    @('indexedArgumentsMain', 'NativeIndexedArguments')
)
foreach ($entry in $entries) {
    & $slangCompiler "$fixtureDirectory/NativeRaster.slang" `
        -target spirv -profile spirv_1_6 -entry $entry[0] -fvk-use-entrypoint-name `
        -capability spvDescriptorHeapEXT -spirv-unified-descriptor-heap-stride `
        -o "$fixtureDirectory/$($entry[1]).spv"
    if ($LASTEXITCODE -ne 0) { throw "Shader compilation or SPIR-V validation failed: $($entry[0])" }
}
```

## Root と index の ABI

root は 80 byte で、position array の uint64 GPU address が offset 0、indirect argument
array の uint64 address が 8、float4 color が 16、float depth が 32、float instance offset
が 36、texture／sampler index が 40／44、最後の uint が 76 である。pixel shader もその
最後の値を使う。`vkCmdPushDataEXT` で渡した一つの root を vertex／pixel が直接読む。

vertex fixture は意図的に Slang の `SV_VulkanVertexID`／`SV_VulkanInstanceID` を使う。
これらは native の `VertexIndex`／`InstanceIndex` に対応し、first vertex、signed base
vertex、first instance の効果を含む。通常の Slang `SV_VertexID`／`SV_InstanceID` の
target 別変換とは区別する。backend は shader を解析して ID を補正せず、raw artifact の
ABI は caller が選ぶ。[Slang の SPIR-V semantics](https://docs.shader-slang.org/en/latest/external/slang/docs/user-guide/a2-01-spirv-target-specific.html)

`drawArgumentsMain` は GPU memory に4個の uint32、`indexedArgumentsMain` は5個の
native field（base vertex は -1）を書く。native address の単発 indirect 命令がこれを読み、
CPU readback による展開や parameter/root buffer を使わない。argument range と index
range は region の先頭から64 byteずらした範囲も試験する。

sampled pixel shader は resource slot 5 と sampler slot 2 を使う。
`-spirv-unified-descriptor-heap-stride` により buffer／image の最大 descriptor size を
使い、[compute fixtures](README.md) と同じ heap ABI を共有する。初回 attachment の
`GENERAL` 初期化に伴う native command 分割後も、pipeline と両 heap の選択を保つ。

GPU 試験は `Pipelines/VulkanNativeRasterTests*.cs` の `VulkanNativeConformance` trait に
分離する。root snapshot、vertex／uint16・uint32 index／GPU indirect、viewport／scissor、
front-face culling、blend／write mask、dynamic depth/stencil、read-only LOAD＋STORE_NONE、
vertex-only depth と複数の初回 attachment を readback で検証する。shader validator 成功と
GPU 試験成功を、Khronos validation layer を有効にした試験成功とは扱わない。
