# ADR 0024: Portable Pipeline State API

## 状態

採用（目標設計）。

## 依存 ADR

- [0020 View API](0020-view-api.md): format、sampler と comparison の値。
- [0023 Portable Shader](0023-shader-design-and-api.md): 低レベルの shader 構成値と binding layout。

## 決定

Portable の raster pipeline は shader、出力、rasterization、depth/stencil、blend の固定条件をまとめた immutable な論理 object とする。WebGPU が一体の pipeline で要求する状態を自然に表現し、Native の PSO 分解を低層の共通 API として強制しない。

論理 object の作成時には description と program を保持する。実際に提出する command が使用する pipeline だけを `Submit` 内部で実体化する。事前準備 API は設けず、未使用の pipeline を無条件に compile しない。

## API

| API | 説明 |
| --- | --- |
| `GpuPrimitiveTopology` | `TriangleList`、`TriangleStrip`、`LineList`、`LineStrip`、`PointList`。 |
| `GpuCullMode`／`GpuFrontFace` | `None/Front/Back` と `CounterClockwise/Clockwise`。culling 対象と front face の winding。 |
| `GpuIndexFormat` | `Uint16/Uint32`。strip pipeline の restart index と indexed draw の index 幅に用いる共有値。 |
| `GpuColorWriteMask` | `None`、`Red/Green/Blue/Alpha` の flags と `All`。 |
| `GpuBlendOperation` | `Add`、`Subtract`、`ReverseSubtract`、`Minimum`、`Maximum`。 |
| `GpuBlendFactor` | `Zero/One`、`SourceColor/DestinationColor/SourceAlpha/DestinationAlpha` とそれぞれの `OneMinus`、`SourceAlphaSaturated`、`Constant/OneMinusConstant`。第二出力の `Source1Color/Source1Alpha` とそれぞれの `OneMinus` は dual-source feature と shader の対応を要求する。 |
| `GpuBlendDescription` | readonly record struct。`ColorOperation/AlphaOperation`、`SourceColorFactor/DestinationColorFactor`、`SourceAlphaFactor/DestinationAlphaFactor`。target の `Blend = null` で blending 無効。 |
| `GpuColorTargetDescription(Format, WriteMask, Blend)` | readonly record struct。`WriteMask` の既定値は `All`、`Blend` は null。 |
| `GpuStencilOp` | `Keep/Zero/Replace/Invert`、`IncrementClamp/DecrementClamp/IncrementWrap/DecrementWrap`。 |
| `GpuStencilFaceState` | readonly record struct。共通の `GpuCompareOp` による `Compare` と、`FailOp/DepthFailOp/PassOp`。 |
| `GpuDepthStencilState` | readonly record struct。`DepthTest/DepthWrite/DepthCompare`、`StencilTest`、`Front/Back`、`uint StencilReadMask/StencilWriteMask`、`int DepthBias` と `float DepthBiasSlopeScale/DepthBiasClamp`。独立した device handle を作らない。 |
| `GpuRasterPipelineDescription(colorTargets, depthStencilFormat)` | color target の ReadOnlySpan をコピーした読取り専用 `ColorTargets` と optional な `DepthStencilFormat`。construction 時の `DepthStencil`、`Topology`、optional な `StripIndexFormat`、`CullMode/FrontFace`、`SampleCount/SampleMask/AlphaToCoverage` は init property とする。 |
| `GpuRasterPipelineHandle` | device に属する immutable な論理 raster pipeline の identity。 |
| `IPortableGpuBackend.CreateRasterPipeline(description, shaders)` | raster の description と GpuShaderProgramDescription を対応付ける。不変の構成値を保持し、上位 PortableShaderProgram は引数にしない。 |
| `DestroyRasterPipeline(pipeline)` | 未提出記録と全 GPU 利用終了後に論理 object と実体化済み pipeline を解放する。 |
| `GpuComputePipelineHandle` | device に属する immutable な論理 compute pipeline の identity。 |
| `CreateComputePipeline(shaders)` | compute entry を持つ GpuShaderProgramDescription に対応する論理 pipeline を作る。実 device pipeline は最初の提出時に生成する。 |
| `DestroyComputePipeline(pipeline)` | 全利用終了後に論理 object と実体化済み pipeline を解放する。 |

shader 構成値の binding layout と直接入力 byte 数も実 pipeline の layout に含まれる。pipeline は caller の application resource を保持しない。caller は module と layout を pipeline の寿命まで保持する。上位 PortableShaderProgram を使う場合は、そこから Description を渡し、program の所有を同じ期間維持する。低層の Portable project から Portable.Shaders project を参照しない。

両 pipeline handle は public abstract 型と protected constructor で外部 backend が実装する。raster の構成値は順不同の一つの Vertex と任意の一つの Pixel entry、compute は一つの Compute entry を受け入れる。複数の同一 stage、余分な stage、null の entry 名や、Pixel entry を省略したときの color target 宣言は native descriptor に損失なく接続できないため拒否する。空の color targets と Vertex entry による depth-only pipeline は表現できる。entry 名の存在、実 shader stage、binding layout と直接入力 size の適合性は runtime が検証する。

native host は実際の draw／dispatch を含む Submit で pipeline layout と native pipeline を生成し、同じ論理 handle の破棄まで再利用する。設定だけで work に使わなかった pipeline は実体化しない。生成診断は shader module と binding layout の診断とともに利用する batch へ帰属させる。vertex input layout と vertex buffer の暗黙 binding は設けず、vertex shader が builtin index と明示した storage binding から必要な入力を読む。

description の初期値は triangle list、culling 無効、反時計回りの front face、sample count 1、sample mask の全 bit 有効、alpha-to-coverage 無効である。`new GpuBlendDescription()` は color/alpha とも `source * One + destination * Zero`、`new GpuStencilFaceState()` は Always と Keep を指定する。struct の `default` は全 field がゼロの値であり、constructor の既定値に暗黙変換しない。`GpuDepthStencilState` は parameterless constructor と default のどちらでも depth/stencil test と write を無効にする。DepthTest が無効なら DepthCompare、StencilTest が無効なら face と stencil mask は処理に使わず、depth write は独立して設定できる。depth/stencil format を省略して有効な条件や depth bias を指定した場合も、それらを黙って捨てず runtime の診断へ渡す。

viewport、scissor、stencil reference、blend constant は command に設定する動的値とする。depth/stencil/blend の固定条件を変える場合は別の論理 pipeline を選ぶ。backend は同じ不変条件の実 object を再利用できるが、その cache を公開の ownership 契約にしない。

WebGPU が検証できる format、sample count、blend factor、shader 入出力、binding layout の条件は runtime に委ねる。logical pipeline の identity 管理と runtime validation の複製を混同しない。

## コード配置

以下は repository root からの配置とする。Portable の公開値と handle、native host の WebGPU による raster/compute pipeline は実装済みである。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable/Pipelines/` | raster/compute pipeline の公開 description、固定状態の値、論理 handle と生成・破棄契約。 |
| `src/graphics/Lumyte.Graphics.Portable/Commands/GpuIndexFormat.cs` | pipeline と indexed draw が共有する GpuIndexFormat。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Pipelines/` | 論理 pipeline の保持、`GPURenderPipeline`／`GPUComputePipeline` への変換と cache。実体化処理は同 project の `Submission/` から呼び出す内部経路とする。 |
| `src/graphics/Lumyte.Graphics.Portable.Tests/Pipelines/` | 隣接 xUnit project。description と固定状態の公開値を検証する。公開拡張契約の consumer 試験は `Device/` にも置く。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Pipelines/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Raster/` | 実 Dawn device による未使用 pipeline の未生成、提出時生成・再利用、生成診断と描画結果の確認。実機試験として隔離する。 |

事前準備の公開 API や Native の state handle 分解をこの project に追加しない。

## 使用例

`backend` は Portable device、`module` は二つの entry point を持つ作成済みの WGSL module とする。module は pipeline の破棄まで保持する。

```csharp
using P = Lumyte.Graphics.Portable;

var shaders = new P.GpuShaderProgramDescription(
    [new(module, P.GpuShaderStage.Vertex, "vertexMain"),
     new(module, P.GpuShaderStage.Pixel, "pixelMain")], []);
var description = new P.GpuRasterPipelineDescription(
    [new(Lumyte.Graphics.GpuFormat.Rgba8Unorm)])
{
    CullMode = P.GpuCullMode.Back,
};
var pipeline = backend.CreateRasterPipeline(description, shaders);
try
{
    // この段階では compile しない。command の Draw を Submit すると実体化する。
}
finally
{
    backend.DestroyRasterPipeline(pipeline);
}
```

この例は GPU work を提出していない。実利用時は pipeline を参照する全 command の完了まで保持する。

## 参考文献

- [WebGPU: Render Pipelines](https://gpuweb.github.io/gpuweb/#render-pipelines): shader と固定状態を持つ render pipeline。

## 採用範囲と未実装事項

Portable は WebGPU に沿った pipeline object を採用し、NoGraphicsAPI の独立 state handle による PSO 分解は Native 側の設計とする。raster/compute の論理 handle、固定状態、提出時の native pipeline／layout 生成、同一 handle での再利用、生成診断の batch への帰属と解放を実装した。dual-source factor も native enum に接続し、任意 feature の要求と shader の適合性は runtime に委ねる。

Browser も raster／compute pipeline と layout を実際に使う最初の Submit で生成し、論理 handle の寿命まで再利用する。shader package／loader との統合は未実装である。同一 description の別 handle 間での native pipeline 共有は行っていない。これは公開契約で要求する機能ではなく、必要に応じて加える内部の最適化とする。実機検証の範囲と結果は [進捗記録](../designs/graphics-implementation-progress.md) に記載する。
