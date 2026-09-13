# ADR 0025: Portable Command Recording API

## 状態

採用（目標設計）。一回提出する記録と、その中の render／compute／copy 操作を定義する。

## 依存 ADR

- [0018 Buffer API](0018-buffer-api.md): 転送と shader data の resource。
- [0019 Texture API](0019-texture-api.md): texture 転送。
- [0020 View API](0020-view-api.md): range、view と attachment。
- [0022 Resource Binding](0022-binding-api.md): group ごとの binding set。
- [0024 Pipeline State](0024-pipeline-state-api.md): Portable の論理 pipeline。

## 決定

command は Portable 専用の記録とする。render pass、compute の区間、copy の順序を明示し、resource を有限の binding set で設定する。Native の command handle、descriptor heap、global barrier を共通化しない。

記録時に root の byte 列や動的引数をコピーし、実 backend の encode は提出時に行う。application resource の lifetime は caller が管理する。未提出 command の破棄には `Dispose` を用い、独立した中止操作や公開の command 状態は設けない。

## API

### 記録と区間

| API | 説明 |
| --- | --- |
| `IGpuQueue` | Portable device に属する queue。Native の同名相当型とは互換にしない。 |
| `IPortableGpuBackend.MainQueue` | backend の主 queue。 |
| `IGpuQueue.StartCommandRecording()` | この queue に一回だけ提出する `GpuCommandBuffer` の記録を開始する。 |
| `GpuCommandBuffer.Dispose()` | 未提出なら記録を破棄する。提出済み GPU work は取り消さず、内部記録 memory の回収は completion に従う。application resource は破棄しない。 |
| `BeginRendering(colors, depthStencil = null)`／`EndRendering()` | attachment と load/store/clear を指定した render 区間。入れ子にしない。 |
| `BeginCompute()`／`EndCompute()` | compute 区間。render と compute の区間を重ねない。copy はどちらの区間の外で記録する。 |

### 状態と入力

| API | 説明 |
| --- | --- |
| `SetPipeline(pipeline)` | render 区間の論理 raster pipeline。実体化は提出時。 |
| `SetComputePipeline(pipeline)` | compute 区間の論理 compute pipeline。実体化は提出時。 |
| `GpuViewport`／`GpuScissorRect` | viewport と scissor の領域値。 |
| `SetViewportAndScissor(viewport, scissor)` | render 区間の viewport と scissor。 |
| `SetStencilReference(reference)` | render 区間の front/back 共通 stencil reference。 |
| `SetBindings(group, bindings, dynamicOffsets = default)` | render 区間で一つの group に immutable binding set を設定する。 |
| `SetComputeBindings(group, bindings, dynamicOffsets = default)` | compute 区間で一つの group に binding set を設定する。 |
| `SetRootData(bytes)`／`SetRootData<T>(in T)` | 現在の raster program の直接入力を記録する。generic 版は Portable 用に生成した unmanaged の Root 型を受け取る。 |
| `SetComputeRootData(bytes)`／`SetComputeRootData<T>(in T)` | compute program の直接入力を記録する。 |

root data は program が要求する byte 数を完全に与え、記録中にコピーする。別の固定容量へ zero-fill して共通 ABI を作らない。root を buffer に置き換えたり、その内容から resource や Parameter Data を抽出したりしない。

`GpuCommandBuffer` は public abstract 型と protected constructor で外部 backend が実装する。generic root overload は `unmanaged` の表現を byte span として非 generic overload へ渡す。dynamic offsets は binding 番号の昇順で `ReadOnlySpan<uint>` を渡し、記録中にコピーする。BeginCompute で pipeline 選択と root の指定状態を初期状態へ戻す。dispatch ごとに最後に指定した root の長さが program の ImmediateSize と一致することを確認し、native の部分更新によって前の root の末尾が残る指定を拒否する。これは Portable の完全入力契約であり、native の即値 alignment／capacity の検証は重複させない。

uniform/storage の resource と byte 列は caller または Resources が明示的に準備する。Parameter Data の参照位置は shader が root から算出する。binding 入力の作成は resource 対応の指定であり、Parameter Data の暗黙の転送ではない。

### Work と copy

| API | 説明 |
| --- | --- |
| `Draw(vertexCount, instanceCount = 1, firstVertex = 0, firstInstance = 0)` | 非 indexed draw。vertex pulling の data は明示 storage binding から読む。 |
| `DrawIndexed(indices, format, indexCount, instanceCount = 1, firstIndex = 0, baseVertex = 0, firstInstance = 0)` | `GpuBufferRange` の index buffer と `GpuIndexFormat` による indexed draw。 |
| `GpuIndexFormat` | `Uint16` または `Uint32`。 |
| `DrawIndirect(arguments)`／`DrawIndexedIndirect(indices, format, arguments)` | buffer range の一件の draw 引数を GPU が読む。 |
| `Dispatch(x, y = 1, z = 1)`／`DispatchIndirect(arguments)` | compute work、または buffer range 内の一件の dispatch 引数。 |
| `CopyBuffer(source, destination)` | 同じ byte 長の二つの buffer range を copy する。 |
| `CopyBufferToTexture(source, texture, footprint)`／`CopyTextureToBuffer(texture, footprint, destination)` | buffer range と texture 領域の copy。 |
| `CopyTexture(source, sourceFootprint, destination, destinationFootprint)` | 二つの texture 領域の copy。 |

copy の alignment、usage、重複範囲、binding と pipeline の適合性、usage scope、draw/dispatch の合法性は runtime が検証する。wrapper は記録を一意に表現するための host 所有状態を持つが、runtime が診断できる条件を同じ validator として再実装しない。

Buffer range の null length は保存した生成 description の残り byte 数から算出する。copy は二つの論理 range の同長、indirect は論理 range が一件の12 byte 引数を含むことを確認する。明示 length に対する実 Buffer の範囲・usage・alignment は runtime が検証する。copy の一つの size や indirect の先頭 offset へ変換すると失われる、caller の論理 range 契約だけを wrapper が扱う。

## 順序と所有権

Portable は render/compute pass と copy の順序を記録する。resource の状態遷移は backend runtime の model に従う。Native の stage/access/layout barrier API を追加しない。同じ pass 内で禁止される resource の同時使用を、見えない pass 分割で自動修正しない。

caller は resource、binding、pipeline、program を最初の記録から全利用終了まで保持する。binding set は immutable だが、buffer/texture の内容を同時に変更できる保証ではない。CPU 更新との同期も caller が管理する。

## コード配置

以下は repository root からの配置で、render／texture copy の目標配置を含む。Portable とそのテスト project は実装済みで、WebGPU に独立した記録・encode 処理を加える。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable/Commands/` | 公開 `GpuCommandBuffer`、recording 操作、viewport/scissor/index format と、一回提出する記録の所有処理。root byte 列と動的引数を記録時にコピーする。 |
| `src/graphics/Lumyte.Graphics.Portable/Submission/` | `IGpuQueue` の契約を置き、ここから command recording の開始を公開する。提出そのものと記録の実装は分ける。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Commands/` | Portable 記録を WebGPU encoder、render/compute pass と copy へ変換する処理。既存 command 実装を改編する。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Browser/Commands/` | ブラウザーの encoder/pass 呼出しと byte 列の interop。Portable command の公開型には JavaScript の object 型を含めない。 |
| `src/graphics/Lumyte.Graphics.Portable.Tests/Commands/` | 隣接 xUnit project。root/動的引数のコピー、未提出記録の破棄など Lumyte が所有する記録契約を検証する。外部 backend の consumer 試験は `Device/` にも置く。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Commands/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Commands/` | 既存 xUnit project。fake runtime で encode の順序と値の受渡しを確認し、実 draw/dispatch/copy は実 device 試験に分離する。 |

Parameter Data の準備・upload や application resource の回収は、この command 実装の担当にしない。

## 使用例

`pipeline`、`bindings`、`root` は同じ Portable compute program のために準備済みとする。この例では記録だけを作り、提出せず破棄する。

```csharp
using var commands = backend.MainQueue.StartCommandRecording();
commands.BeginCompute();
commands.SetComputePipeline(pipeline);
commands.SetComputeBindings(0, bindings);
commands.SetComputeRootData(in root);
commands.Dispatch(64);
commands.EndCompute();
```

## 採用範囲と未実装事項

Portable 固有の pass、明示 binding、直接入力を採用する。compute 区間、pipeline／group／dynamic offsets／直接 root の記録、直接／間接 dispatch、buffer copy、提出時 encode と内部記録の所有を実装した。別 device・破棄済み object と記録区間を確認し、GPU の validator は追加しない。

render 区間、raster draw、texture copy と Browser 接続は未実装である。実機で確認した入力・実行・copy の範囲は [進捗記録](../designs/graphics-implementation-progress.md) に記載する。
