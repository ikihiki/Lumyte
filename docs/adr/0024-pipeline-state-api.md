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
| `GpuCullMode`／`GpuFrontFace` | culling 対象と front face の winding。 |
| `GpuColorWriteMask` | RGBA の書込み channel。 |
| `GpuBlendOperation` | `Add`、`Subtract`、`ReverseSubtract`、`Minimum`、`Maximum`。 |
| `GpuBlendFactor` | zero/one、source/destination color/alpha とその補数。第二 color 出力の factor は dual-source feature と shader の対応を要求する。 |
| `GpuBlendDescription` | color/alpha の operation と source/destination factor。省略時は blending 無効。 |
| `GpuColorTargetDescription` | `Format`、`WriteMask`、optional な `Blend`。 |
| `GpuStencilOp` | keep、zero、replace、invert、increment/decrement の clamp/wrap。 |
| `GpuStencilFaceState` | compare と fail/depth-fail/pass の stencil operation。 |
| `GpuDepthStencilState` | depth test/write/compare、front/back stencil、read/write mask、depth bias。immutable な値であり、独立した device handle を作らない。 |
| `GpuRasterPipelineDescription` | color targets、depth/stencil format と state、topology、strip index format、cull/front face、sample count/mask、alpha-to-coverage。 |
| `GpuRasterPipelineHandle` | device に属する immutable な論理 raster pipeline の identity。 |
| `IPortableGpuBackend.CreateRasterPipeline(description, shaders)` | raster の description と GpuShaderProgramDescription を対応付ける。不変の構成値を保持し、上位 PortableShaderProgram は引数にしない。 |
| `DestroyRasterPipeline(pipeline)` | 未提出記録と全 GPU 利用終了後に論理 object と実体化済み pipeline を解放する。 |
| `GpuComputePipelineHandle` | device に属する immutable な論理 compute pipeline の identity。 |
| `CreateComputePipeline(shaders)` | compute entry を持つ GpuShaderProgramDescription に対応する論理 pipeline を作る。実 device pipeline は最初の提出時に生成する。 |
| `DestroyComputePipeline(pipeline)` | 全利用終了後に論理 object と実体化済み pipeline を解放する。 |

shader 構成値の binding layout と直接入力 byte 数も実 pipeline の layout に含まれる。pipeline は caller の application resource を保持しない。caller は module と layout を pipeline の寿命まで保持する。上位 PortableShaderProgram を使う場合は、そこから Description を渡し、program の所有を同じ期間維持する。低層の Portable project から Portable.Shaders project を参照しない。

`GpuComputePipelineHandle` は public abstract 型と protected constructor で外部 backend が実装する。compute の構成値は一つの Compute entry を持つ形だけを受け入れ、余分な entry を黙って捨てない。entry 名の存在、実 shader stage、binding layout と直接入力 size の適合性は runtime が検証する。native host は最初の dispatch を含む Submit で pipeline layout と compute pipeline を生成し、論理 handle の破棄まで再利用する。設定だけで work に使わなかった pipeline は実体化しない。

viewport、scissor、stencil reference は command に設定する動的値とする。depth/stencil/blend の固定条件を変える場合は別の論理 pipeline を選ぶ。backend は同じ不変条件の実 object を再利用できるが、その cache を公開の ownership 契約にしない。

WebGPU が検証できる format、sample count、blend factor、shader 入出力、binding layout の条件は runtime に委ねる。logical pipeline の identity 管理と runtime validation の複製を混同しない。

## コード配置

以下は repository root からの配置で、raster 等の目標配置を含む。Portable とそのテスト project は実装済みで、WebGPU に独立した compute pipeline を加える。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable/Pipelines/` | raster/compute pipeline の公開 description、固定状態の値、論理 handle と生成・破棄契約。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Pipelines/` | 論理 pipeline の保持、`GPURenderPipeline`／`GPUComputePipeline` への変換と cache。実体化処理は同 project の `Submission/` から呼び出す内部経路とする。 |
| `src/graphics/Lumyte.Graphics.Portable.Tests/Pipelines/` | 隣接 xUnit project。description と固定状態の公開値を検証する。公開拡張契約の consumer 試験は `Device/` にも置く。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Pipelines/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Pipelines/` | 既存 xUnit project。未提出 pipeline の未生成、提出時生成・再利用を fake runtime で確認し、実 shader を使った pipeline 試験を隔離する。 |

事前準備の公開 API や Native の state handle 分解をこの project に追加しない。

## 使用例

`program` はロード済みの Portable raster program、`description` はその出力と固定状態に対応する値とする。

```csharp
var pipeline = backend.CreateRasterPipeline(description, program.Description);
try
{
    // command に設定した pipeline は Submit 時に実体化される。
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

Portable は WebGPU に沿った pipeline object を採用し、NoGraphicsAPI の独立 state handle による PSO 分解は Native 側の設計とする。compute の論理 handle、提出時の native pipeline／layout 生成、再利用、生成診断の batch への帰属と解放を実装した。

raster の固定状態・pipeline と Browser 接続は未実装である。compute の検証結果は [進捗記録](../designs/graphics-implementation-progress.md) に記載する。
