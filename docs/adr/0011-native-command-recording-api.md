# ADR 0011: Native Command Recording API

## 状態

採用（目標設計）。現行実装の完了を示さず、旧 API との互換経路は設けない。

## 依存 ADR

- [ADR 0002: Native Graphics API](0002-native-graphics-api.md) の device、capability、root 上限。
- [ADR 0004: Native Linear Data API](0004-native-linear-data-api.md) の range と [ADR 0005: Native Texture API](0005-native-texture-api.md) の texture/copy footprint。
- [ADR 0006: Native View API](0006-native-view-api.md) の attachment view と [ADR 0008: Native Descriptor API](0008-native-descriptor-api.md) の heap。
- [ADR 0010: Native Pipeline State API](0010-native-pipeline-state-api.md) の pipeline・state 値と [ADR 0001: Graphics の層構造と共通契約](0001-graphics-api.md) の stage/access/layout。

## 決定

command は順序付きの指示と値を記録し、application resource の寿命や shader の到達先を管理しない。記録状態を公開する enum/property は設けず、記録の破棄は `Dispose` に統一する。

各 work が root data を直接受け取る。command は Parameter Data を生成・upload・保持せず、shader が root から parameter と資源参照を算出する。GPU buffer への root fallback は行わない。

## API

`MainQueue` は `INativeGpuBackend` の member、`StartCommandRecording` は `NativeGpuQueue` の member とする。それ以外の記録操作と `Dispose` は `NativeGpuCommandBuffer` に属する。

| API | 契約 |
| --- | --- |
| `MainQueue` | この device の `NativeGpuQueue`。 |
| `NativeGpuQueue` | 一つの native queue の identity。 |
| `NativeGpuQueue.StartCommandRecording()` | queue 所属の一回提出用 `NativeGpuCommandBuffer` を返す。 |
| `NativeGpuCommandBuffer` | 記録とその内部管理を所有し、application resource を保持しない。状態の公開 enum や property は持たない。 |
| `NativeGpuCommandBuffer.Dispose()` | 未提出なら記録を破棄する。提出済みなら GPU work の取消し・待機を行わず、内部 command memory は必要な完了まで保持する。 |
| `SetResourceDescriptorHeap(heap)`／`SetSamplerDescriptorHeap(heap)` | command が使用する heap 全体を設定する。個別 resource の binding set や型別 profile は受け取らない。 |
| `SetPipeline(pipeline)`／`SetComputePipeline(pipeline)`／`SetDepthStencilState(state)` | command の pipeline と独立 depth/stencil の値を設定する。`SetPipeline` は vertex／mesh の raster pipeline を共に受け取る。 |
| `SetViewport(viewport)`／`SetScissor(scissor)` | pipeline の生成を伴わず記録する。 |
| `NativeGpuLoadOp`／`NativeGpuStoreOp` | load は `Load/Clear/Discard`、store は `Store/Discard`。 |
| `NativeGpuColorAttachment` | render view、load/store と clear color の値を持つ。 |
| `NativeGpuDepthStencilAttachment` | render view、`DepthReadOnly`／`StencilReadOnly`、aspect ごとの nullable load/store と clear 値を持つ。read-only または存在しない aspect の operation は未指定とし、存在する writable aspect は指定する。render view の `Flags` と同じ読み取り専用条件を使う。 |
| `BeginRendering(colorAttachments, depthStencilAttachment = null)`／`EndRendering()` | attachment を指定して rendering 区間を開始・終了する。viewport/scissor は attachment 領域、depth/stencil は無効を初期値にする。 |
| `Draw(rootData, vertexCount, instanceCount = 1, firstVertex = 0, firstInstance = 0)` | rendering 内の非 indexed draw。vertex fetch は shader が行う。 |
| `NativeGpuIndexFormat` | `Uint16/Uint32`。 |
| `DrawIndexed(rootData, indices, format, indexCount, instanceCount = 1, firstIndex = 0, baseVertex = 0, firstInstance = 0)` | `NativeGpuRange` の index data を native index fetch に使う。 |
| `DrawIndirect(rootData, arguments)`／`DrawIndexedIndirect(rootData, indices, format, arguments)` | range 内の一件の native draw 引数を GPU が読む。 |
| `DispatchMesh(rootData, x, y = 1, z = 1)` | rendering 内の mesh raster work。amplification があればその workgroup 数、なければ mesh の workgroup 数を指定する。`MeshShaders` を要求する。 |
| `DispatchMeshIndirect(rootData, arguments)` | `NativeGpuRange` 内の一件の mesh workgroup 数を GPU が読む。使用する pipeline と必要 capability は直接版と同じ。 |
| `Dispatch(rootData, x, y = 1, z = 1)`／`DispatchIndirect(rootData, arguments)` | rendering 外で直接または一件の間接 compute work を記録する。 |
| `CopyMemory(source, destination)` | 二つの `NativeGpuRange` の byte 内容を転送する。各 range が指す線形 region と region 相対 offset を使う。 |
| `CopyMemoryToTexture(source, destination, footprint)`／`CopyTextureToMemory(source, destination, footprint)` | range と texture の指定範囲を native copy に渡す。footprint の単一 `Aspect` が対象 plane を選ぶ。 |
| `Barrier(beforeStages, beforeAccess, afterStages, afterAccess)` | resource を列挙しない global execution/memory dependency。共通の stage/access 値を使う。 |
| `TextureTransition(view, beforeLayout, afterLayout)` | `ExplicitTextureTransitions` 対応 backend が必要とする補足操作。caller が実際の前後 layout/state を指定する。 |
| `DiscardTexture(view, afterLayout)` | 指定 subresource の旧内容を保持せず、再利用のための layout／metadata 初期化を記録する。Vulkan は `General`、DirectX 12 は次の用途の layout を指定する。alias の探索・切替相手の指定・CPU 待機は行わない。 |

各 work の `rootData` は `ReadOnlySpan<byte>` とし、呼出し時に caller の bytes をコピーする。size は 4 byte の倍数で `MaxRootDataSize` 以下、空入力は許可する。active graphics stage 群は一つの root を共有し、mesh raster では amplification／mesh／pixel に直接可視とする。64 byte 固定、末尾 zero fill、独立した root setter を Native 層に要求しない。

`Draw`／`DrawIndexed` とその indirect 版には vertex program、`DispatchMesh` とその indirect 版には mesh program を持つ raster pipeline を使う。mesh work は rendering 内の raster operation であり、compute `Dispatch` の別名ではない。attachment、viewport/scissor、depth/stencil、descriptor heap と root の契約は vertex raster と共通とする。mesh が生成する primitive indices は shader の出力であり、mesh command に index buffer／index format／instance count／base vertex を渡さない。必要な instancing や meshlet 選択は pass の shader とその root が表す。

native 命令を生成するとき、その root を root constants または push data へ直接渡す。DirectX 12 は呼出し時の command 引数、state、root bytes と attachment の配列・clear 値を CPU 記録へコピーし、提出時に不足 PSO の解決後、順番どおりに native command へ変換する。Vulkan は記録呼出し時に native command を生成する。どちらも caller の入力領域を後から読み直さず、保持する resource identity は非所有とする。

rendering の開始時は attachment 領域の viewport/scissor と無効な depth/stencil を初期値にする。read-only attachment は clear・書込みを行わず内容を保持し、未指定の load/store を writable の既定値に置き換えない。caller は指定 aspect に書かない depth/stencil state を使う。存在しない aspect の operation も未指定とする。render view の flags と attachment の指定を一致させる。

copy／index／indirect の range は線形 region を識別する。native 命令の resource 相対 offset に `Region.HeapOffset` を加算せず、allocation から backing resource を探索・暗黙生成しない。

copy、barrier と `DiscardTexture` は rendering 外へ記録する。基本 indirect 引数は、draw が四つの uint32、indexed draw が index count/instance count/first index/int32 base vertex/first instance、compute／mesh dispatch が X/Y/Z の三つの uint32 の native 配置を使う。mesh indirect は一件・12 byte の引数だけを読み、root は直接版と同様に command の bytes を使う。GPU 引数を CPU へ readback・展開しない。copy の範囲、重なり、pitch、format、usage と indirect 引数の整列・GPU 値は caller が native 条件を満たす。暗黙の staging や追加 copy で不適合を修復しない。

compute 等が mesh の geometry、payload の入力または indirect 引数を生成する場合も、caller が `Barrier` で順序付ける。shader が読む data は `GpuStage.AmplificationShader/MeshShader` と shader access、indirect 引数は `GpuStage.DrawIndirect` と `GpuAccess.IndirectRead` の対象になる。未対応 device への mesh command を低層が compute／indexed draw でエミュレートしない。

同期は resource を列挙しない global execution/memory dependency を基本にする。DirectX 12 の texture layout 変更は caller が明示し、現在 layout と不足 barrier を backend が推論しない。Vulkan の通常 image は単一 layout とし、初回初期化、明示的な discard／alias 再初期化と presentation の処理を除いて transition を要求しない。rendering の境界だけで hazard が解消されたとは扱わない。

`DiscardTexture` の `view` は非所有の `NativeGpuTextureView` であり、texture identity、aspect、mip／layer 範囲を指定する。対象の各 subresource 全体を破棄し、矩形の部分保持や alias 間の内容継承は行わない。backend は view の format 再解釈を使って data を変換しない。native が depth／stencil の一括初期化を必要とする範囲では caller が `DepthStencil` を指定し、texture の opaque な配置から影響範囲を限定できなければ texture 全体を指定する。

alias A → B の書込み → A の再利用では、caller が B の利用と memory dependency を `Barrier` または queue の completion／wait で順序付けてから、A に `DiscardTexture` を記録する。再初期化そのものの memory access も先行 alias と競合し得るため、同じ queue の先行 `Barrier` は後続 scope に `GpuStage.All` を含めて再初期化までを覆う。discard 操作はその初期化と後続利用の順序を保つが、他の alias の write flush を代行しない。次に必要な copy／clear を caller が記録し、読み出す内容を定義する。

初回生成の契約は ADR 0005 のままとする。DirectX 12 の `TextureTransition(view, Undefined, afterLayout)` は初回の discard 初期化と同じ native 操作を使い、Vulkan の初回 `GENERAL` 初期化は backend の局所処理とする。既存 image の alias 再初期化は毎回 caller が明示し、生成済みかどうかの内部フラグから省略しない。記録しただけでは再初期化済みにせず、未提出 `Dispose` や Submit 失敗の後は、caller が必要な discard を次の記録に含める。

未提出の `Dispose` は記録を破棄する。提出済みの `Dispose` は GPU work を取消し・待機せず、backend 所有の command memory は必要な completion または確定した device 終了まで保持する。application の resource、descriptor と pipeline の退役は caller が管理する。

## コード配置

パスは repository root 相対の目標配置とする。`Lumyte.Graphics.Native` と隣の `.Tests` は新設予定、DirectX 12／Vulkan と各 `.Tests` は既存 project の改編であり、テストは xUnit を使う。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native/Commands/` | `NativeGpuCommandBuffer`、mesh を含む recording の操作、attachment と load/store・index format の値。queue の公開型は `Submission/` に置き、`StartCommandRecording` から接続する。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Commands/` | 引数と root bytes をコピーする CPU 記録、native command list への順序付き変換と内部 command memory。 |
| `src/graphics/Lumyte.Graphics.Vulkan/Commands/` | 呼出し時の native command 記録、直接 push data、address command と内部 command memory。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Synchronization/`、`src/graphics/Lumyte.Graphics.Vulkan/Synchronization/` | 記録から呼ぶ global dependency、明示 discard／再初期化の写像。DirectX 12 の明示 texture transition もこの担当とする。 |
| `src/graphics/Lumyte.Graphics.Native.Tests/Commands/`、`src/graphics/Lumyte.Graphics.DirectX12.Tests/Commands/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Commands/` | root／attachment 値のコピー、記録順序、未提出 Dispose と native 引数の写像を検証する unit test。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Integration/Commands/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/Commands/` | draw／dispatch、mesh-only／amplification 付き直接・間接 mesh、aspect 別 copy／indirect、barrier と A → B → A の alias 再利用の GPU 試験。mesh 未対応 device の capability 分岐も確認する。 |

Parameter Data の生成や upload、application resource の保持は recorder に配置しない。

## 使用例

`native` は初期化済み device、`pipeline` と `resourceHeap` は caller-owned とする。shader はその heap の descriptor を参照し、`rootData` は shader の byte 配置に従う。以下は未提出の記録である。

```csharp
using NativeGpuCommandBuffer commands = native.MainQueue.StartCommandRecording();
commands.SetResourceDescriptorHeap(resourceHeap);
commands.SetComputePipeline(pipeline);
commands.Dispatch(rootData, 1);
commands.Barrier(
    GpuStage.ComputeShader, GpuAccess.ShaderWrite,
    GpuStage.Copy, GpuAccess.CopyRead);
```

dispatch が caller の root bytes をコピーする。scope 終了時の `Dispose` は未提出の記録だけを破棄し、application resource は解放しない。

mesh raster の記録も同じ recorder を使う。`meshPipeline` は mesh program を使う作成済み raster pipeline、`meshletGroupCount` は shader が処理する workgroup 数、`colorAttachment` は初期化・同期済み attachment とする。

```csharp
using var meshCommands = native.MainQueue.StartCommandRecording();
meshCommands.SetResourceDescriptorHeap(resourceHeap);
meshCommands.BeginRendering([colorAttachment]);
meshCommands.SetPipeline(meshPipeline);
meshCommands.DispatchMesh(meshRootData, meshletGroupCount);
meshCommands.EndRendering();
```

この例は `MeshShaders` が true の経路で記録する。amplification を含む場合は、group 数は amplification の起動数として解釈する。間接版は最後の work を `DispatchMeshIndirect(meshRootData, argumentRange)` に置き換える。

alias が書き込んだ後の texture を再利用する場合は、次の順で記録する。`reusedView` は影響する subresource 全体、`upload` はその内容を初期化する独立した range、`footprint` は単一 aspect の転送配置とする。`nextLayout` は Vulkan なら `General`、DirectX 12 なら `CopyDestination` として Native caller が選ぶ。

```csharp
commands.Barrier(
    GpuStage.Copy, GpuAccess.CopyWrite,
    GpuStage.All, GpuAccess.CopyWrite);
commands.DiscardTexture(reusedView, nextLayout);
commands.CopyMemoryToTexture(upload, reusedTexture, footprint);
```

copy が必要な内容を定義した後、caller は次の用途への依存と、DirectX 12 で必要な layout 変更を記録する。depth と stencil を両方再初期化した場合は、利用する両 aspect の内容を定義する。

## 検証方針

host byte 列の安全、整数変換、owned identity/記録状態、root コピーと native 命令への写像を確認する。read-only flags と operation など一意に変換できない入力は失敗とするが、実際の usage/layout/descriptor/shader 条件を網羅する独自 validator は作らない。

## 採用差分と未実装範囲

直接 root、global dependency と caller lifetime を採用する。DirectX 12 の CPU 記録と明示 texture transition、Lumyte の Dispose 契約と alias 再利用の明示 `DiscardTexture` は差分である。直接／一件の間接 mesh command を採用する。GPU が生成・選択する root、mesh を含む multi-draw/count buffer と presentation の公開 command はこの範囲に含めない。Native recorder、mesh、aspect 別 copy と discard の native 変換、および未提出失敗を含む GPU 検証は未実装である。
