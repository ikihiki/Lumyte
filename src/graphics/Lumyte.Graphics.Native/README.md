# Native GPU 基盤

DirectX 12／Vulkan 向けの caller-owned な低レベル契約。device、共通 heap、線形 region と texture の明示配置、render view、descriptor storage、GPU コピー、compute／vertex・mesh raster と直接 root、コマンド記録・提出・同期を対象とする。[設計の正本](../../../docs/adr/0002-native-graphics-api.md) は Native ADR 群。

`CreateGpuHeap` は backing allocation だけを確保する。`CreateLinearRegion` が指定した heap offset に独立した native buffer を配置し、その resource の GPU address と必要な CPU mapping を提供する。`NativeGpuRange.Offset` は region 内の値であり、heap offset を含めない。

`CreateTexture` も同じ heap と offset を受け取り、独立した native texture を配置する。`GetTextureMemoryRequirements` と線形 region の requirements をまとめて heap 確保に渡すことで、native 条件が許す組合せを同居させられる。texture は opaque identity であり、CPU/GPU pointer や metadata の照会は公開しない。[混在配置の使用例](../../../docs/adr/0005-native-texture-api.md#使用例) を参照する。

heap、region、texture と GPU 利用の寿命は caller が管理する。managed reference をコピーしても native resource を延命しない。解放順は GPU 利用終了 → region／texture → heap → backend。提出は一回限りとし、caller の semaphore と completion 値で GPU 完了を確認する。

同一 heap／resource の操作は、配置と破棄も含めて caller が直列化する。backend の Dispose と利用も競合させない。Vulkan の同一 allocation 内で共有する mapping は、この host 同期の下で取得・解放する。

backend の Dispose 前に、caller は提出済み work を完了させ、未提出分を含む全 recording と semaphore を破棄する。未提出 recording の native memory は各 recording が所有し、queue は提出済み command memory だけを回収する。

## 使用例

以下は `Lumyte.Graphics.DirectX12` と `Lumyte.Graphics.Native` を参照する Native 利用側の例。CPU から配置した region に書き込むだけであり、GPU command は実行しない。

```csharp
using System.Runtime.InteropServices;
using Lumyte.Graphics.DirectX12;
using Lumyte.Graphics.Native;

using INativeGpuBackend backend = DirectX12Backend.Create();
byte[] bytes = [3, 5, 8, 13, 21];
var kind = NativeGpuMemoryKind.CpuVisible;
var requirements = backend.GetLinearMemoryRequirements(256, kind);
var heap = backend.CreateGpuHeap(
    checked(requirements.Size * 2), requirements.Alignment, kind,
    [requirements.Compatibility]);
try
{
    var region = backend.CreateLinearRegion(256, heap, requirements.Size);
    try
    {
        var range = new NativeGpuRange(region, 32, (ulong)bytes.Length);
        nint destination = checked(region.CpuAddress + (nint)range.Offset);
        Marshal.Copy(bytes, 0, destination, bytes.Length);
        Console.WriteLine(range.GpuAddress);
    }
    finally { backend.DestroyLinearRegion(region); }
}
finally { backend.DestroyGpuHeap(heap); }
```

requirement の Size は alignment を含む予約容量で、region の Size は論理データ容量。Compatibility は同じ backend・memory kind で取得した値をそのまま列として渡す。

## GPU コピーと明示的な待機

`upload`、`deviceData`、`readback` はそれぞれ CpuVisible、GpuOnly、Readback の region 内に確保済みの `NativeGpuRange` とする。各 range は16 byte以上あり、native の整列条件を満たす。caller は関連する全 resource と heap を、最後の待機が完了するまで破棄・再利用しない。

```csharp
byte[] input = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
Marshal.Copy(input, 0,
    checked(upload.Region.CpuAddress + (nint)upload.Offset), input.Length);

var queue = backend.MainQueue;
using var completion = backend.CreateSemaphore();
using var commands = queue.StartCommandRecording();
commands.CopyMemory(upload.Slice(0, 16), deviceData.Slice(0, 16));
commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite,
    GpuStage.Copy, GpuAccess.CopyRead);
commands.CopyMemory(deviceData.Slice(0, 16), readback.Slice(0, 16));
commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite,
    GpuStage.Host, GpuAccess.HostRead);
queue.Submit([commands], new(completion, 1));
commands.Dispose(); // GPU work は取り消さず、内部 command memory は完了まで保持する。
completion.WaitCpu(1);

byte[] output = new byte[16];
Marshal.Copy(checked(readback.Region.CpuAddress + (nint)readback.Offset),
    output, 0, output.Length);
```

`GpuStage`／`GpuAccess` は `Lumyte.Graphics` に属する。HostRead barrier が memory visibility を、`WaitCpu` が実行完了を扱う。GPU 間の依存も caller が指定し、Submit が不足 barrier を推定しない。`completion.IsComplete(value)` なら CPU を待機させずに確認できる。

CPU thread を占有せずに待つ場合は `await completion.WaitAsync(value, cancellationToken)` を使う。非同期 timer と counter 照会による観測で、取消しは CPU の待機だけを終了する。GPU work や資源の利用が終わったとは扱わない。DirectX 12／Vulkan は観測中の semaphore の Dispose を拒否し、caller は全 CPU／GPU 利用が終了してから破棄する。

未提出の `Dispose` は記録を破棄する。提出済みの `Dispose` は待機せず、queue は内部 completion で command memory を回収する。caller semaphore は producer と、それを待つ全 consumer の GPU 利用が完了した後に破棄できる。command の状態を取得する API、application resource の自動退役、暗黙 staging は設けない。

`Submit` が native queue への受渡し後に同期的に失敗し、native API に未提出の保証がない場合、`NativeGpuSubmissionException` が要求した `Completion` と元の `InnerException` を保持する。受理の有無が不明な場合も含み、未提出として再実行・解放しない。この値の到達や GPU 停止は保証されず、device loss や backend の Dispose も利用終了の代理にはならない。詳細は [提出と同期の ADR](../../../docs/adr/0012-native-command-submission-and-synchronization.md) に従う。

## 非同期 copy と CPU の先行

`backend.CopyQueue` は独立した転送 queue で、利用できない device では null を返す。存在しても物理的な同時実行や高速化を保証しない。以下の `slots` は caller が用意した3組の upload／device／readback range とする。CPU は再利用する slot の最終 consumer だけを待ち、それ以外のフレームを先行提出できる。

```csharp
var copy = backend.CopyQueue ?? throw new NotSupportedException("No copy queue.");
var main = backend.MainQueue;
using var copied = backend.CreateSemaphore();
using var finished = backend.CreateSemaphore();
ulong[] lastUse = new ulong[slots.Length];

for (ulong frame = 0; frame < frameCount; frame++)
{
    int index = (int)(frame % (ulong)slots.Length);
    finished.WaitCpu(lastUse[index]); // 初回は0。再利用する slot だけを待つ。
    var slot = slots[index];
    WriteFrameData(slot.Upload); // caller が mapped memory に書く。
    ulong value = frame + 1;

    using var uploadCommands = copy.StartCommandRecording();
    uploadCommands.CopyMemory(slot.Upload, slot.Device);
    copy.Submit([uploadCommands], new(copied, value));

    using var consumeCommands = main.StartCommandRecording();
    consumeCommands.CopyMemory(slot.Device, slot.Readback);
    consumeCommands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite,
        GpuStage.Host, GpuAccess.HostRead);
    main.Submit([consumeCommands], new(finished, value), [new(copied, value)]);
    lastUse[index] = value;
    // 次の slot を準備する。描画を行う場合もその最終利用まで finished に含める。
}
finished.WaitCpu(frameCount); // resource と両 semaphore の破棄前に全 consumer を完了させる。
```

GPU wait は batch 全体より前に働く。CPU の待機ではなく、queue に依存を積む操作である。`Submit([], signal, waits)` で work を持たない同期だけの提出もできる。各 queue 内の host 操作は caller が直列化するが、別 queue の操作と `IsComplete`／`WaitCpu`／`SignalCpu` は並行できる。`SignalCpu(value)` は CPU producer や gate 用で、未完了の GPU work の完了通知として使わない。全 signal の値と実行順、待機が解消することは caller が保証する。

CopyQueue の共通用途は線形データと color texture の転送である。depth／stencil は MainQueue を使う。DirectX 12 の texture は MainQueue で `DiscardTexture(view, GpuTextureLayout.Common)`、または既存 layout から Common への `TextureTransition` を実行して signal する。CopyQueue がその値を GPU wait して転送し、MainQueue が転送完了を GPU wait して shader／attachment layout へ移す。CopyQueue 自体は layout transition を行えない。

Vulkan は両 queue family が異なる場合、線形 region と texture をその2 family の concurrent sharing で生成し、通常は GENERAL を維持する。新規 texture は、初期化する producer の Submit を先に受理させてから consumer を Submit する。未来の値への GPU wait は、この初回初期化の host 順序を代替しない。初期化済み resource の依存には wait-before-signal を使える。どちらの backend も application resource の寿命、frame slot や descriptor の再利用を追跡しない。

## View と descriptor

`NativeGpuTextureView` は texture の範囲を示す値だけを持つ。`CreateRenderView` が attachment 用 native view を生成し、`DestroyRenderView` がそれだけを解放する。read-only depth／stencil flags は handle に保持し、親 texture は延命しない。

descriptor は独立した専用 heap の caller 指定 slot に書く。以下の `view` は作成済み texture の view で、この例では shader を実行しない。

```csharp
var resources = backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Resource, 16);
var samplers = backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Sampler, 4);
try
{
    backend.WriteTextureDescriptor(resources, 7, view);
    backend.WriteSamplerDescriptor(samplers, 2, new NativeGpuSamplerDescription());
    using var selection = backend.MainQueue.StartCommandRecording();
    selection.SetResourceDescriptorHeap(resources);
    selection.SetSamplerDescriptorHeap(samplers);
} // 未提出の recording を破棄してから heap を解放する。
finally
{
    backend.DestroyDescriptorHeap(samplers);
    backend.DestroyDescriptorHeap(resources);
}
```

slot の空き管理、参照先の保持、上書き前の待機は caller が担当する。書込みや heap 選択は texture を初期化しない。Vulkan で shader から初めて texture を参照する場合は、caller が先に `DiscardTexture(view, GpuTextureLayout.General)` を記録するか、先行する texture copy による初期化を済ませる。DX12 の layout 変更も caller が明示する。

## Compute と直接 root

`computeCode` は対象 device の raw DXIL または SPIR-V、`rootData` は shader の ABI に従う byte 列とする。`resources`／`samplers` は caller-owned heap、`readback` は出力を受け取る range とする。shader が書く `deviceOutput` は初期化・同期済みで、必要な descriptor を書込み済みとする。

```csharp
var pipeline = backend.CreateComputePipeline(new NativeGpuShaderProgram(
    new NativeGpuShaderCode
    {
        Stage = GpuShaderStage.Compute,
        Code = computeCode,
        EntryPoint = "main"
    }));
try
{
    var queue = backend.MainQueue;
    using var completion = backend.CreateSemaphore();
    using var commands = queue.StartCommandRecording();
    commands.SetResourceDescriptorHeap(resources);
    commands.SetSamplerDescriptorHeap(samplers);
    commands.SetComputePipeline(pipeline);
    commands.Dispatch(rootData, 1); // 呼出し後は caller の rootData 領域を再利用できる。
    commands.Barrier(GpuStage.ComputeShader, GpuAccess.ShaderWrite,
        GpuStage.Copy, GpuAccess.CopyRead);
    commands.CopyMemory(deviceOutput, readback);
    commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite,
        GpuStage.Host, GpuAccess.HostRead);
    queue.Submit([commands], new(completion, 1));
    completion.WaitCpu(1);
}
finally
{
    backend.DestroyComputePipeline(pipeline);
}
```

DirectX 12 の compute ABI は `b0, space0` に最大256 byte の root constants を渡す。固定 root signature は両 heap の直接 indexing flags を持つため、shader が使用しない場合も caller が resource／sampler heap の両方を選択する。buffer data は descriptor index と offset で参照し、実 GPU pointer の shader dereference は提供しない。

Vulkan は null pipeline layout と `vkCmdPushDataEXT` を使う。実 GPU pointer だけを使う shader なら descriptor heap は不要である。heap を使う artifact は `Limits.Descriptors` の固定 slot stride に従う。試験用 Slang artifact は device ごとの descriptor size を固定せず、unified descriptor stride を使う。

root の有効上限は `Limits.MaxRootDataSize`、dispatch 数は `Limits.Dispatch` で得る。各 work が読む root 全域を caller が渡し、末尾の zero fill は行わない。command は root の pointer や index を解釈せず、Parameter Data の生成・upload と GPU buffer への fallback は行わない。

`DispatchIndirect(rootData, arguments)` は range の先頭にある3個の uint32（X／Y／Z）で1件を実行する。GPU が引数を書いた場合は、その producer から `GpuStage.DrawIndirect`／`GpuAccess.IndirectRead` への barrier を caller が記録する。root は直接版と同様に command へ渡し、GPU 引数の readback や CPU 展開を行わない。

## Raster と indexed draw

`vertexShader` と `pixelShader` は対象 backend 用の `NativeGpuShaderCode`、`colorView` は color attachment 用の render view、`indices` は3個の Uint16 index を持つ範囲とする。texture の初期化と必要な layout／barrier は済ませ、descriptor heap を含むすべての参照先を GPU 完了まで保持する。

```csharp
var pipeline = backend.CreateRasterPipeline(
    new NativeGpuRasterPipelineDescription
    {
        ColorTargets = [new(GpuFormat.Rgba8Unorm)],
        Topology = NativeGpuPrimitiveTopology.TriangleList
    },
    new NativeGpuShaderProgram(vertexShader, pixelShader));
try
{
    var queue = backend.MainQueue;
    using var completion = backend.CreateSemaphore();
    using var commands = queue.StartCommandRecording();
    commands.SetResourceDescriptorHeap(resources);
    commands.SetSamplerDescriptorHeap(samplers);
    commands.SetPipeline(pipeline);
    commands.BeginRendering([new NativeGpuColorAttachment(
        colorView, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store,
        new GpuClearColor(0, 0, 0, 1))]);
    commands.DrawIndexed(rootData, indices, NativeGpuIndexFormat.Uint16, 3);
    commands.EndRendering();
    queue.Submit([commands], new(completion, 1));
    completion.WaitCpu(1);
}
finally
{
    backend.DestroyRasterPipeline(pipeline);
}
```

vertex data の取得は shader が行い、index だけを native index fetch へ渡す。`Draw` は非 indexed、`DrawIndexed` は Uint16／Uint32 の範囲を使う。両者に `instanceCount` と開始位置を指定でき、`DrawIndexed` の `baseVertex` は符号付きである。shader の system-value semantic は対象 artifact の ABI に従い、command が開始位置を root に追加・補正することはない。

Vulkan の試験用 Slang shader は `SV_VulkanVertexID`／`SV_VulkanInstanceID` で開始位置を含む native index を取得する。通常の `SV_VertexID`／`SV_InstanceID` と同じ値とは扱わない。DirectX 12 で開始引数そのものを shader から読むには、対応 device と SM 6.8 の `SV_StartVertexLocation`／`SV_StartInstanceLocation` を使える。この任意機能は試験で確認しており、Native backend の必須条件には加えていない。ABI の詳細は [DirectX 12](../../../docs/adr/0013-directx12-backend-implementation.md#dxil-と-root-payload) と [Vulkan](../../../docs/adr/0014-vulkan-backend-implementation.md#spir-v-と直接-root-data) の ADR を参照する。

`DrawIndirect` は先頭4個の uint32、`DrawIndexedIndirect` は5個の32-bit field（4番目は符号付き baseVertex）を1件の GPU 引数として読む。root は直接渡し、引数の生成と indirect read の間には caller が barrier を指定する。

`BeginRendering` は先頭 color attachment、または depth/stencil attachment の mip 領域に viewport/scissor を設定し、depth/stencil の test/write を無効にする。`SetViewport`、`SetScissor` と `SetDepthStencilState` はその後に上書きできる。read-only aspect は render view の flags から導出し、その load/store を指定しない。存在する writable aspect は load/store の両方を明示する。

DirectX 12 は pipeline 作成時に description と raw shader をコピーし、実際の `Submit` で使う depth/stencil の組だけ native PSO を生成する。成功した PSO は同じ pipeline で再利用し、front/back の stencil reference、root、viewport/scissor の変更では増やさない。Vulkan は作成時に native pipeline を完成させ、depth/stencil は dynamic state で変更する。

## Mesh と amplification

mesh は任意機能で、`Capabilities.MeshShaders` と `Limits.MeshShader` が対応を表す。
amplification は別の capability を持ち、非対応の場合は `AmplificationDispatch` が null、`MaxPayloadSize` が0になる。
shader が出力する頂点・primitive と payload の複合制約は compiler／native validation に従う。

次は `meshShader` と `pixelShader` を用意した場合の例。既存の raster と同じ handle と生成 API を使い、
vertex input の `Topology` を null にして mesh の出力 topology を明示する。
`colorView` は初期化と必要な同期を済ませ、heap を含む参照先を完了まで保持する。

```csharp
if (!backend.Capabilities.MeshShaders)
    throw new NotSupportedException("This pass requires mesh shaders.");

var pipeline = backend.CreateRasterPipeline(
    new NativeGpuRasterPipelineDescription
    {
        ColorTargets = [new(GpuFormat.Rgba8Unorm)],
        Topology = null,
        MeshOutputTopology = NativeGpuMeshOutputTopology.Triangle
    },
    new NativeGpuShaderProgram(meshShader, pixelShader));
try
{
    var queue = backend.MainQueue;
    using var completion = backend.CreateSemaphore();
    using var commands = queue.StartCommandRecording();
    commands.SetResourceDescriptorHeap(resources);
    commands.SetSamplerDescriptorHeap(samplers);
    commands.SetPipeline(pipeline);
    commands.BeginRendering([new NativeGpuColorAttachment(colorView, NativeGpuLoadOp.Clear)]);
    commands.DispatchMesh(rootData, meshGroupCount);
    commands.EndRendering();
    queue.Submit([commands], new(completion, 1));
    completion.WaitCpu(1);
}
finally
{
    backend.DestroyRasterPipeline(pipeline);
}
```

amplification shader も program に渡すと、command の group 数は amplification を起動する数になる。
その shader が payload と mesh group 数を生成する。command は payload を生成・upload せず、
root は amplification／mesh／pixel へ直接渡す。`DispatchMeshIndirect` は指定 range の先頭12 byte の X／Y／Z を GPU が読む。
compute が引数を作る場合は、caller が shader write から indirect read への barrier を指定する。
amplification／mesh が読む別の GPU data にも、それぞれの shader stage への依存を指定する。

## バックエンドの追加

別 assembly で `INativeGpuBackend` を実装し、次の基底型から実装内の非公開型を派生させる。`Lumyte.Graphics.Native` に `InternalsVisibleTo` はなく、追加 backend の assembly 名を登録する必要もない。

| 基底型 | protected constructor | 派生型が保持するもの |
| --- | --- | --- |
| `NativeGpuHeap` | `(size, alignment, kind)` | native allocation、所属 device、局所的な解放状態 |
| `NativeGpuMemoryCompatibility` | `()` | native requirement、取得元 device と memory kind |
| `NativeGpuLinearRegion` | `(heap, heapOffset, size, gpuAddress, cpuAddress)` | native resource、所属 device、mapping と局所的な解放状態 |
| `NativeGpuTextureHandle` | `()` | native texture、作成値、所属 device と局所的な解放・初期化状態 |
| `NativeGpuRenderViewHandle` | `(flags)` | attachment 用 native view、所属 device と局所的な解放状態 |
| `NativeGpuDescriptorHeap` | `(kind, capacity)` | 専用 descriptor storage、native slot 配置と所属 device |
| `NativeGpuComputePipelineHandle` | `()` | 完成済みの native compute pipeline、所属 device と局所的な解放状態 |
| `NativeGpuRasterPipelineHandle` | `()` | 同期生成した native pipeline、または raw code と固定値・提出時に解決する pipeline 所有 PSO |
| `NativeGpuQueue` | `()` | native queue、内部 command memory の completion と回収 |
| `NativeGpuCommandBuffer` | `()` | queue identity、記録・一回提出・破棄の局所状態と native command memory |
| `NativeGpuSemaphore` | `()` | caller-owned native timeline と所属 device、独立した CPU 同期操作 |

`NativeGpuMemoryRequirements(size, alignment, compatibility)` は public constructor で返せる。基底型が公開する metadata は不変とし、texture handle と compatibility は opaque に保つ。基底型自体は native resource を生成・破棄しない。backend は受け取った object の派生型と device identity を検査し、別実装・別 device の object を native API に渡さない。`object BackendData` や共通の resource registry は使わない。

公開契約だけで実装・利用できることは、内部公開指定のない別 assembly の [consumer test](../Lumyte.Graphics.Native.Tests/Device/ExternalNativeGpuBackendTests.cs) で検証する。本体向け `InternalsVisibleTo` は repository 全体で使用せず、テスト向けだけに限定する。

## 現段階の範囲

旧 `IGpuBackend` と旧描画系は削除済みであり、互換 adapter はない。各 backend は native API を直接呼ぶ。共通 `Lumyte.Graphics` から利用するものは code format と device loss 例外などの基礎型である。

texture copy は単一 aspect の footprint と、明示した byte pitch を使う。DirectX 12 では caller が `TextureTransition` で前後の layout を指定し、Vulkan では backend が初回利用前に `GENERAL` を順序付ける。alias 再利用は caller が `Barrier` と `DiscardTexture` で指定する。非所有の view 値は subresource を表すだけで、native render view を生成しない。`CreateTexture` 自体は提出・待機しない。

render view と descriptor storage に加え、raw shader の compute pipeline、直接 root、直接／間接 dispatch と shader による descriptor 参照を実装した。両 backend の `BufferDescriptors` と Vulkan の `RawShaderPointers` を true とする。limits は root、compute dispatch と descriptor ABI を提供する。

vertex／mesh raster pipeline、render pass、直接／一件の間接 draw・indexed draw・mesh dispatch と depth/stencil の分離も実装した。DirectX 12 の PSO は実際の raster work の Submit 内で解決する。mesh／amplification は任意機能で、対応と上限を capability／limits に反映する。Resources の管理層と非同期転送を実装し、その他の limits と機能 RenderGraph の移行は後続段階とする。Vulkan は ADR が要求する拡張・feature を初期化時に要求し、古い descriptor set の実装へ切り替えない。実装と実機検証の範囲は [進捗記録](../../../docs/designs/graphics-implementation-progress.md) を参照する。
