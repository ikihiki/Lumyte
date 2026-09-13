# Native Vulkan mesh fixtures

`NativeMesh.slang` は `NativeRaster.slang` の 80 byte root 定義を共有する。
製品 backend は raw SPIR-V を受け取り、compiler や反射の依存を持たない。

## 再生成

repository root で Slang 2026.17 を使う。内蔵 SPIR-V Tools validator を必ず有効にする。
生成器が `MeshShadingEXT` capability と `MeshEXT`／`TaskEXT` entry point を出力する。
Y 反転 option は指定せず、既存 raster と同じ負の viewport height を使用する。

```powershell
$slangCompiler = 'artifacts/experiments/slang-wgsl-root/compiler/slang-2026.17/bin/slangc.exe'
$fixtureDirectory = 'src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/Shaders'
$env:SLANG_RUN_SPIRV_VALIDATION = '1'
$meshEntries = @(
    @('meshMain', 'NativeMesh'),
    @('taskMain', 'NativeTask'),
    @('payloadMeshMain', 'NativePayloadMesh'),
    @('groupMeshMain', 'NativeGroupMesh'),
    @('lineMeshMain', 'NativeLineMesh'),
    @('meshPixelMain', 'NativeMeshPixel'),
    @('meshArgumentsMain', 'NativeMeshArguments')
)
foreach ($meshEntry in $meshEntries) {
    & $slangCompiler "$fixtureDirectory/NativeMesh.slang" `
        -target spirv -profile spirv_1_6 -entry $meshEntry[0] -fvk-use-entrypoint-name `
        -capability spvDescriptorHeapEXT -spirv-unified-descriptor-heap-stride `
        -o "$fixtureDirectory/$($meshEntry[1]).spv"
    if ($LASTEXITCODE -ne 0) { throw "Shader compilation or validation failed: $($meshEntry[0])" }
}
```

## ABI と実行条件

root の位置は [raster fixtures](RASTER.md) と同じである。`meshMain` は実 GPU address
から頂点を読み、`taskMain` は root の color と offset を payload に書く。
`payloadMeshMain` がそれを受け取って root の頂点 address と depth を使い、pixel が
同じ root の最後の uint（offset 76）を参照する。全 stage へ `vkCmdPushDataEXT` の
直接入力を渡し、root 用 GPU buffer や command 側の parameter 解釈は追加しない。

task payload は shader の `TaskPayload` 構造体で、`DispatchMesh` intrinsic に渡す。
caller はこの shader 同士の payload ABI を一致させる。API の root と payload を
混同せず、backend は payload の転送や allocation を管理しない。
[Slang の mesh/task 構文](https://docs.shader-slang.org/en/stable/coming-from-glsl.html)

`groupMeshMain` は group ID `(1, 2, 3)` だけで描画し、直接・間接とも `(2, 3, 4)` の
3軸の伝達を検証する。`meshArgumentsMain` が GPU memory の offset 64 に12 byteの
uint32 X／Y／Z を書き、`vkCmdDrawMeshTasksIndirect2EXT` が同じ address range を
1回分読み込む。CPU readback による draw への展開は行わない。
[address による mesh indirect](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdDrawMeshTasksIndirect2EXT.html)

line／triangle は別の raw mesh entry point とする。SPIR-V が実際の output topology を
持つため、caller が `MeshOutputTopology` と一致させる。pipeline 作成時に SPIR-V を
解析して metadata の一致を検証する機能は作らない。

GPU 試験は `Pipelines/VulkanNativeRasterTests.Mesh.cs` の `VulkanNativeConformance`
trait に分離する。mesh 非対応時は具体的な理由付きで skip し、task のみ非対応なら
task 試験だけ skip する。対応時は root snapshot、3軸 dispatch、GPU indirect、
task payload、line、vertex／mesh の混在、dynamic depth、pixel なしの depth 描画、
descriptor heap の command segment 間再設定を readback で確認する。
SPIR-V validator と通常の GPU 実行結果は Khronos validation layer による検証とは別である。
