# ADR 0010: Native Pipeline State API

## 状態

採用（目標設計）。現行実装の完了を示さず、旧 API との互換経路は設けない。

## 依存 ADR

[ADR 0009: Native Shader API](0009-native-shader-api.md) の raw program と、[ADR 0001: Graphics の層構造と共通契約](0001-graphics-api.md) の format、compare、座標規約に依存する。

## 決定

NoGraphicsAPI を基礎に、raster pipeline と depth/stencil の値を分ける。blend と rasterization はこの最小 Native 契約では pipeline の固定 state に残す。viewport/scissor と stencil reference は動的な値とする。

vertex と mesh は raster pipeline の二つの入力経路とし、同じ handle、生成 API、depth/stencil 分離と command の `SetPipeline` を使う。mesh 専用の PSO 管理 API や事前準備 API は追加しない。

DirectX 12 は depth/stencil を native PSO に含むため、実際の提出内で必要な組を解決する。caller に準備操作や事前の組の列挙を要求しない。

## API

生成・破棄操作は `INativeGpuBackend` の member とする。この文書は pipeline と state 値の定義を担当する。

| API | 契約 |
| --- | --- |
| `NativeGpuPrimitiveTopology`／`NativeGpuCullMode`／`NativeGpuFrontFace` | topology は `TriangleList/TriangleStrip`、cull は `None/Front/Back`、front face は `Clockwise/CounterClockwise`。 |
| `NativeGpuMeshOutputTopology` | `Line/Triangle`。mesh shader が出力する primitive の種類であり、input assembler の頂点列の組立て方ではない。 |
| `NativeGpuBlendOperation` | `Add/Subtract/ReverseSubtract/Minimum/Maximum`。 |
| `NativeGpuBlendFactor` | `Zero/One`、source/destination の color/alpha とその反転、`SourceAlphaSaturate`。 |
| `NativeGpuBlendDescription` | `Enabled`、`ColorOperation`／`AlphaOperation`、`SourceColorFactor`／`DestinationColorFactor` と `SourceAlphaFactor`／`DestinationAlphaFactor`。constructor の既定値は blend 無効、Add、source One／destination Zero。 |
| `NativeGpuColorTargetDescription` | `Format`、`GpuColorWriteMask WriteMask`（既定 All）、`Blend`（既定無効）。 |
| `NativeGpuRasterPipelineDescription` | init-only record。`ColorTargets` 配列（既定空）、optional `DepthStencilFormat`、nullable `Topology`（既定 TriangleList）／`MeshOutputTopology`（既定 null）、`CullMode`（None）、`FrontFace`（CounterClockwise）、`SampleCount`（1）。blend は target に含む。vertex program は `Topology` だけ、mesh program は `MeshOutputTopology` だけを指定する。 |
| `NativeGpuStencilOperation` | `Keep/Zero/Replace/IncrementClamp/DecrementClamp/Invert/IncrementWrap/DecrementWrap`。 |
| `NativeGpuStencilFaceState` | `Compare`、`FailOp`／`DepthFailOp`／`PassOp` と uint `Reference`。constructor の既定値は Always、全 operation Keep、reference 0。 |
| `NativeGpuDepthStencilState` | `DepthTest`／`DepthWrite`／`DepthCompare`、`StencilTest`、byte `StencilReadMask`／`StencilWriteMask` と `Front`／`Back`。constructor の既定値は test/write 無効、depth LessEqual、mask 255、両 face は Always／Keep。`default` 値も test/write 無効とする。 |
| `NativeGpuRasterPipelineHandle`／`NativeGpuComputePipelineHandle` | caller-owned pipeline identity。public abstract 基底型と protected constructor を持ち、別 assembly の backend が非公開派生型として実装する。 |
| `CreateRasterPipeline(description, program)` | 固定 state と raw shader から論理 pipeline を生成する。DirectX 12 は後の native PSO 生成に必要な description、raw code と entry point を自身で保持する。Vulkan の dynamic state 経路はこの時点で native 生成を完了する。 |
| `CreateComputePipeline(program)` | native compute pipeline の生成を完了して返す。 |
| `DestroyRasterPipeline`／`DestroyComputePipeline` | caller が全利用を解消してから pipeline と所有する native object を解放する。 |
| `NativeGpuViewport` | float `X/Y/Width/Height` と `MinDepth/MaxDepth`（既定 0/1）。framebuffer 左上を原点とする。 |
| `NativeGpuScissorRect` | int `X/Y` と uint `Width/Height`。 |

vertex pipeline は shader による頂点取得と native index fetch を使う `Draw`／`DrawIndexed`、mesh pipeline は amplification／mesh が生成する geometry と `DispatchMesh` を使う。mesh の primitive indices は shader が出力し、input assembler 用の index buffer は使わない。`MeshOutputTopology` は raw shader の出力宣言に対応する caller の値で、DirectX 12 の PSO primitive type 等へ渡す。mesh program に vertex `Topology` を残して無視したり、shader を解析して値を推測したりしない。native API に渡される出力宣言との適合、payload と stage linkage は compiler／native 診断に委ねる。Vulkan では SPIR-V が実際の出力 topology を決め、C# 側の `MeshOutputTopology` との一致は caller が保証する。native がこの metadata を照合するとは扱わない。[DirectX mesh shader specification](https://microsoft.github.io/DirectX-Specs/d3d/MeshShader.html)

DirectX 12 の raster pipeline は、description、raw code と entry point を自身で保持する論理 object である。caller の入力領域を後から読み直さず、提出の中で実際の draw/indexed draw/mesh dispatch とそれぞれの indirect 版に使う pipeline/depth-stencil の組を解決する。設定されても raster work に使われない組は生成しない。

不足する native PSO だけを生成し、成功した PSO はその pipeline が Destroy まで所有して再利用する。key は native PSO に固定される意味上の state 値だけとし、動的 stencil reference、viewport/scissor、descriptor index、resource identity と root bytes は含めない。global cache と自動 eviction は設けない。

Vulkan の dynamic depth/stencil 経路は vertex／mesh とも raster pipeline の作成時に native 生成を完了し、depth/stencil の組ごとの追加 PSO を作らない。mesh 経路は vertex input／input assembly state を使わず、mesh の出力宣言に従う。compute pipeline は双方で作成時に native 生成を完了する。[VK_EXT_mesh_shader](https://docs.vulkan.org/features/latest/features/proposals/VK_EXT_mesh_shader.html)

pipeline と state の意味上の適合は native の生成・診断に従う。caller は pipeline を参照する全未提出記録と GPU 利用を解消してから破棄する。

この最小契約の rasterization は solid fill、depth clipping 有効、depth bias なし、primitive restart 無効とする。wireframe、depth bias、depth clamp、strip の restart index を表す API は設けていない。未指定の機能を実装済みとは扱わない。

## コード配置

パスは repository root 相対とし、未実装機能の目標配置を含む。`Lumyte.Graphics.Native` と隣の `.Tests` は作成済みで、DirectX 12／Vulkan と各 `.Tests` は既存 project 内へ実装を追加する。テストは xUnit を使う。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native/Pipelines/` | raster／compute pipeline handle、vertex／mesh の topology を持つ description、blend・raster・depth/stencil・viewport/scissor の値。生成・破棄 member は `Device/INativeGpuBackend.cs` に宣言する。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Pipelines/` | 入力 code を保持する論理 raster pipeline、depth/stencil の意味上の key、pipeline-owned PSO と compute pipeline。PSO の不足分を解決する入口は `Submission/` から呼ぶ。 |
| `src/graphics/Lumyte.Graphics.Vulkan/Pipelines/` | 作成時に完成する native raster／compute pipeline、固定 state の写像と解放。 |
| `src/graphics/Lumyte.Graphics.Native.Tests/Pipelines/`、`src/graphics/Lumyte.Graphics.DirectX12.Tests/Pipelines/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Pipelines/` | state の写像、PSO key が動的値を除くこと、code の保持と PSO 再利用を検証する unit test。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Integration/Pipelines/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/Pipelines/` | 実際の vertex／mesh pipeline 生成、描画と depth/stencil の変化を確認する試験。mesh は対応 GPU に分離する。 |

事前 PSO 準備の公開 API や global cache 用の project は設けない。

## 使用例

compute は raw code の program から native pipeline を完成させて返す。以下では GPU に使用しない。

```csharp
var compute = native.CreateComputePipeline(new NativeGpuShaderProgram(computeShader));
native.DestroyComputePipeline(compute);
```

`description` と `program` は raster の固定 state と raw shader 値とする。GPU に使用させない例である。

```csharp
var pipeline = native.CreateRasterPipeline(description, program);
native.DestroyRasterPipeline(pipeline);
```

DirectX 12 では論理定義だけを生成・破棄し、未使用の depth/stencil の組は生成しない。Vulkan は作成時に native pipeline を生成する。

同じ生成 API に mesh program を渡す。`meshProgram` は `Mesh` と optional `Amplification/Pixel` を持ち、三角形を出力する。`meshDescription` の attachment、sample count 等も対象描画に合わせて指定済みとする。

```csharp
meshDescription = meshDescription with
{
    Topology = null,
    MeshOutputTopology = NativeGpuMeshOutputTopology.Triangle
};
var meshPipeline = native.CreateRasterPipeline(meshDescription, meshProgram);
native.DestroyRasterPipeline(meshPipeline);
```

## 検証方針

state の native 表現、意味上の key と pipeline-owned object の回収を確認する。format、sample count、shader linkage と attachment 条件の独自 validator は作らない。

## 採用差分と未実装範囲

raster pipeline と depth/stencil の分離は部分採用である。DirectX 12 の提出時 PSO 解決は Lumyte の補足で、native PSO から depth/stencil を完全分離したとは扱わない。mesh は line／triangle 出力を採用し、DirectX 12 と共通に扱えない point 出力は未採用。rasterization/blend の全面分離、dual-source blend、alpha-to-coverage など本定義にない機能は未提供。

公開の compute／vertex・mesh raster pipeline handle、生成・独立破棄、command への選択と実行を実装した。DirectX 12 の raster は入力を保持し、実際の draw／mesh dispatch の Submit 内で不足する PSO を生成して pipeline ごとに再利用する。mesh の native stream は MS／optional AS／PS と固定 state を持ち、vertex input を含めない。Vulkan は作成時に raster pipeline を完成させ、depth/stencil を dynamic state へ渡す。viewport／scissor／stencil reference は PSO key に含めない。全 format／sample count と mesh 固有機能の組合せを網羅した検証は未実施である。
