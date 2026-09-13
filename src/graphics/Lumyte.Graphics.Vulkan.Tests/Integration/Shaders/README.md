# Native Vulkan compute fixtures

`NativeCompute.slang` は Native Vulkan 用の独立した検証入力である。Slang 2026.17 で
SPIR-V 1.6 にコンパイルした小さな `.spv` を埋込み resource として保持し、これらの
fixture の build／test のために Slang の導入や実行を追加しない。既存の legacy shader
package 試験は従来の compiler を使う。製品 backend は raw SPIR-V を受け取り、
compiler、shader package、従来の descriptor set ABI に依存しない。

## 再生成

repository root で以下を実行する。`$slangCompiler` は Slang 2026.17 の `slangc.exe` とする。
`SLANG_RUN_SPIRV_VALIDATION=1` により Slang 内蔵の SPIR-V Tools validator も実行する。

```powershell
$slangCompiler = 'artifacts/experiments/slang-wgsl-root/compiler/slang-2026.17/bin/slangc.exe'
$fixtureDirectory = 'src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/Shaders'
$env:SLANG_RUN_SPIRV_VALIDATION = '1'
$entries = @(
    @('rawMain', 'NativeRawCompute'),
    @('argumentsMain', 'NativeArgumentsCompute'),
    @('heapMain', 'NativeHeapCompute'),
    @('emptyRootMain', 'NativeEmptyRootCompute'),
    @('storageMain', 'NativeStorageCompute'),
    @('readOnlyMain', 'NativeReadOnlyCompute'),
    @('groupsMain', 'NativeGroupsCompute')
)
foreach ($entry in $entries) {
    & $slangCompiler "$fixtureDirectory/NativeCompute.slang" `
        -target spirv -profile spirv_1_6 -entry $entry[0] -fvk-use-entrypoint-name `
        -capability spvDescriptorHeapEXT -spirv-unified-descriptor-heap-stride `
        -o "$fixtureDirectory/$($entry[1]).spv"
    if ($LASTEXITCODE -ne 0) { throw "Shader compilation or SPIR-V validation failed: $($entry[0])" }
}
```

`-target spirv-asm` と `.spvasm` 出力を使うと ABI を確認できる。

## ABI と試験対象

`Root` は 80 byte。output address は offset 0 の uint64、value は 8、output index は
12、buffer／texture／sampler index は 16／20／24、padding は 28〜63、最後の uint4 は
64〜79 である。試験は最後の uint を offset 76 から読み、各 dispatch が 64 byte を超える
root を直接受け取ることを GPU で確認する。root は `PushConstant` storage class を使い、
backend が `vkCmdPushDataEXT` で渡す。raw pointer は `PhysicalStorageBuffer64` と実際の
device address を使う。buffer への root fallback と Parameter Data の生成はない。

resource heap 内の buffer／image の配列は、`OpConstantSizeOfEXT` の双方の結果から
大きい値を選ぶ `ArrayStrideIdEXT` を共有する。この PC ではこの値と backend が公開する
resource slot stride が一致する。shader package の `VulkanUnified` ABI は device ごとに
この式の結果と実際の slot stride を照合する。alignment からの丸めが shader に追加されるとは
仮定しない。[Slang の compiler option](https://docs.shader-slang.org/en/stable/external/slang/docs/command-line-slangc-reference.html#spirv-unified-descriptor-heap-stride)。
sampler は独立した native sampler size による stride を使う。特定 GPU の 32 byte 等を固定せず、
root の lookup table と型別 descriptor heap も導入しない。shader に `DescriptorSet`／
`Binding` は生成されない。

| entry | GPU で確認する内容 |
| --- | --- |
| `rawMain` | 非ゼロ region offset からの raw pointer 書込み、各 call の root コピー、shader byte 入力の寿命。 |
| `argumentsMain` | 1 件の X／Y／Z を GPU memory に書き、address による indirect dispatch が読む。 |
| `heapMain` | 同一 resource heap の buffer slot 3 と texture slot 5、sampler heap の slot 2 を使用。最初の texture 初期化による native command buffer 分割後も選択済み pipeline／heap を使用。 |
| `emptyRootMain` | 空の root と固定 buffer slot 3、heap の切替。 |
| `storageMain` | 明示 discard 後に storage texture descriptor から書込み、copy で readback。 |
| `readOnlyMain` | 非ゼロ slot と range offset の read-only buffer descriptor。 |
| `groupsMain` | X／Y／Z 全ての direct group count。 |

`storageMain` の `StorageImageReadWithoutFormat`／`StorageImageWriteWithoutFormat`
capability は Vulkan 1.3 以降で許可される。Native baseline は 1.4 であり、古い Vulkan
用の同名 feature を追加要件にはしない。実際の image read/write の format 条件は native の
仕様に従う。[SPIR-V environment](https://docs.vulkan.org/spec/latest/appendices/spirvenv.html)

shader の生成・validator 成功は実 GPU conformance とは別であり、GPU 試験は隣の
`Pipelines/VulkanNativeComputeTests*.cs` の `VulkanNativeConformance` trait に分離する。
validation layer 有効時の実行には別途利用可能な Khronos validation layer が必要である。
