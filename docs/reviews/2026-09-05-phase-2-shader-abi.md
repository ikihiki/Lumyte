# E2 ADR: 共通shader ABI v4

状態: 採用。root dataのbuffer fallbackを禁止するユーザー指定を反映。

## Root data

raster/compute共通で4〜64 bytes、4-byte単位。68/128 bytes、空、4-byte未満・端数付きの入力は共通APIで拒否する。書き込みは常に64 bytesのsnapshotとして扱い、短い入力の後ろをゼロで埋める。

pipeline設定でroot dataをゼロに戻す。描画pass開始後はpipelineを再設定する必要がある。computeも描画passまたはbarrierの後はpipelineを再設定する。入力設定・draw/dispatchの順序は共通APIで検証する。

| backend | 転送先 |
| --- | --- |
| DirectX 12 | b0/space0、root parameter 5の16個の32-bit root constants |
| Vulkan | 64-byte push constant range |
| WebGPU | pipeline layoutのImmediateSize=64、raster/computeのSetImmediates、WGSLのvar&lt;immediate&gt; |

root dataはuniform/storage bufferへフォールバックしない。WebGPUの64-byte immediates要件を満たさないdeviceは初期化で拒否する。

WebGPUはWebGPUSharp 0.5.7に付属するDawn runtimeを使用する。Silk.NET.WebGPU 2.23.0は既存内部descriptorのmanaged型定義にのみ使用し、旧wgpu-nativeとWGPU extensionのnative呼び出しは除いた。ModernWebGpuApiが各descriptor・enumを新FFIへ明示変換し、旧structを新ABIへreinterpret castしない。native handleだけをopaque pointerとして保持する。

## Rootからのshader入力算出

command APIはparameter dataを特別扱いせず、SetParameterData/SetComputeParameterData、専用uniform binding、内部upload pageを持たない。64 bytesを超えるデータは通常のresource-table bufferへ置き、そのelement index/byte offsetをroot dataに格納する。shaderはroot dataから参照位置を算出して読む。descriptor slotは通常どおりshader宣言とresource tableで対応付ける。bufferの更新、graph依存、GPU完了までの寿命は他のshader bufferと同じ契約に従う。

AddDrawはWorld × ViewProjectionをCPUで算出し、行列の4行をfloat4として64-byte root dataへ格納する。shaderは行ベクトルとの積を算出する。WebGPU immediate address spaceではarray/matrixを直接宣言できないため、4本のfloat4というABI表現を使う。

## 共通desktop profile

| 対象 | 共通範囲 |
| --- | --- |
| color attachments / samples | 1 / 1 |
| alpha-to-coverage / dual-source blending | 無効 |
| depthとstencil | 同時使用時は同一format |
| textureサイズ | 各辺8192以下、1 mip、1 layer |
| sampled textures / samplers | 各16 slots |
| read-only + writable storage buffers | 合計8 slots |
| storage textures | 4 slots、Rgba8UnormまたはR32Float |
| bufferサイズ | 256 MiB以下 |
| dispatch group count | 各軸1〜65535 |

Color/sampled用formatは既存GpuFormatのcolor形式。depth形式はdepth/stencil attachment用で、color/storage/copy用途には使わない。Unknown enum・usageのbit、depth/colorの不一致、sRGB storage等はGpuCommonLimitsで拒否する。storageへの書き込みはfragment/computeに限る。

shaderの論理tableはtexture0、sampler1、read-buffer2、storage-texture3、write-buffer4を維持する。共通slot数を超えるtable生成はすべてのbackendで同じ例外になる。DirectX12だけで可能だった複数color targetも共通入口では拒否する。将来のprofile拡張は全backendのconformanceと合わせて行う。

## Slangと移行手順

Slangでroot用のstructを宣言し、WGSL生成時にはb0/space6を予約markerとして使う。DXIL/SPIR-Vにはb0/space0 + vk::push_constantを使う。offline compilerはWGSLの予約markerをvar&lt;immediate&gt;に変換する。このmarkerはruntimeのbuffer bindingではない。

```slang
struct Root { uint byteOffset; };
#if defined(LUMYTE_SHADER_TARGET_WGSL)
ConstantBuffer<Root> root : register(b0, space6);
#else
[[vk::push_constant]] ConstantBuffer<Root> root : register(b0, space0);
#endif
ByteAddressBuffer data : register(t0, space2);
float4 loadParameter() { return data.Load<float4>(root.byteOffset); }
```

1. ABI v3のparameter bindingを通常のresource-table bufferへ移し、参照するelement index/byte offsetをroot dataへ格納する。
2. Lumyte.Graphics.Shader.OfflineのtargetsをimportしたprojectをRebuildして、DXIL/SPIR-V/WGSLをまとめて再生成する。CLIの場合は `dotnet run --project src/graphics/Lumyte.Graphics.Shader.Offline -- --source shader.slang --output shader.lshp --cache .packages/slang`。
3. pipeline作成にGpuShaderBindingConvention.AbiHashを渡す。ABI v2/v3 packageは旧hashを期待値に渡しても明示的に拒否され、再生成手順が例外に含まれる。

DawnのWGSL validationへ合わせ、距離場のfwidthは条件分岐の外で計算する。shader検証を無効にして通す変更は行わない。

## 検証

2026-09-06、Windows上で `dotnet test Lumyte.slnx --no-restore -m:1 -v minimal` が終了コード0。
24 test projects、1014件成功、失敗0、skip0。内訳のGPU conformanceを含むbackend test projectsは
DirectX12 155件、Vulkan 156件、WebGPU 154件。共通Graphics 137件、Library 13件も成功した。
この実行ではBrowser、他OS、長時間性能・メモリ試験は検証していない。

共通のSlang packageとconsumerでVulkan/DirectX12/WebGPUを実行する。検証対象は入力の違うdraw、pipeline切替、64-byte末尾と4-byte短い書き込み、複数submissionのcompute結果、合成行列の適用。WebGPU固有の追加試験ではimmediate-only shaderの出力とpipeline切替でのゼロ化を確認する。旧ABI拒否と共通範囲はxUnit単体テストで検証する。

BrowserはE7でhost接続とcompletionを検証するまで、このdesktop結果の対象に含めない。
