# Native GPU 基盤

DirectX 12／Vulkan 向けの caller-owned な低レベル契約。device、共通 heap、線形 region と texture の明示配置、render view、descriptor storage、GPU コピー、compute と直接 root、コマンド記録・提出・同期を対象とする。[設計の正本](../../../docs/adr/0002-native-graphics-api.md) は Native ADR 群。

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
using var completion = queue.CreateSemaphore(0);
using var commands = queue.StartCommandRecording();
commands.CopyMemory(upload.Slice(0, 16), deviceData.Slice(0, 16));
commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite,
    GpuStage.Copy, GpuAccess.CopyRead);
commands.CopyMemory(deviceData.Slice(0, 16), readback.Slice(0, 16));
commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite,
    GpuStage.Host, GpuAccess.HostRead);
queue.Submit([commands], completion, 1);
commands.Dispose(); // GPU work は取り消さず、内部 command memory は完了まで保持する。
queue.Wait(completion, 1);

byte[] output = new byte[16];
Marshal.Copy(checked(readback.Region.CpuAddress + (nint)readback.Offset),
    output, 0, output.Length);
```

`GpuStage`／`GpuAccess` は `Lumyte.Graphics` に属する。HostRead barrier が memory visibility を、`Wait` が実行完了を扱う。GPU 間の依存も caller が指定し、Submit が不足 barrier を推定しない。`IsComplete(completion, value)` なら CPU を待機させずに確認できる。

未提出の `Dispose` は記録を破棄する。提出済みの `Dispose` は待機せず、queue は内部 completion で command memory を回収する。caller semaphore は完了後に破棄できる。command の状態を取得する API、application resource の自動退役、暗黙 staging は設けない。

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
    using var completion = queue.CreateSemaphore(0);
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
    queue.Submit([commands], completion, 1);
    queue.Wait(completion, 1);
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
| `NativeGpuQueue` | `()` | native queue、内部 command memory の completion と回収 |
| `NativeGpuCommandBuffer` | `()` | queue identity、記録・一回提出・破棄の局所状態と native command memory |
| `NativeGpuSemaphore` | `()` | caller-owned native completion timeline と所属 queue |

`NativeGpuMemoryRequirements(size, alignment, compatibility)` は public constructor で返せる。基底型が公開する metadata は不変とし、texture handle と compatibility は opaque に保つ。基底型自体は native resource を生成・破棄しない。backend は受け取った object の派生型と device identity を検査し、別実装・別 device の object を native API に渡さない。`object BackendData` や共通の resource registry は使わない。

公開契約だけで実装・利用できることは、内部公開指定のない別 assembly の [consumer test](../Lumyte.Graphics.Native.Tests/Device/ExternalNativeGpuBackendTests.cs) で検証する。本体向け `InternalsVisibleTo` は repository 全体で使用せず、テスト向けだけに限定する。

## 現段階の範囲

旧 `IGpuBackend` の adapter は作らない。既存の描画系は未移行の source として残り、新しい backend は native API を直接呼ぶ。共通 `Lumyte.Graphics` から現在利用するものは code format と device loss 例外などの基礎型である。

texture copy は単一 aspect の footprint と、明示した byte pitch を使う。DirectX 12 では caller が `TextureTransition` で前後の layout を指定し、Vulkan では backend が初回利用前に `GENERAL` を順序付ける。alias 再利用は caller が `Barrier` と `DiscardTexture` で指定する。非所有の view 値は subresource を表すだけで、native render view を生成しない。`CreateTexture` 自体は提出・待機しない。

render view と descriptor storage に加え、raw shader の compute pipeline、直接 root、直接／間接 dispatch と shader による descriptor 参照を実装した。両 backend の `BufferDescriptors` と Vulkan の `RawShaderPointers` を true とする。limits は root、compute dispatch と descriptor ABI を提供する。

raster pipeline／描画、attachment としての使用、mesh、その他の limits、Resources と機能 RenderGraph の移行は後続段階とする。Vulkan は ADR が要求する拡張・feature を初期化時に要求し、古い descriptor set の実装へ切り替えない。実装と実機検証の範囲は [進捗記録](../../../docs/designs/graphics-implementation-progress.md) を参照する。
