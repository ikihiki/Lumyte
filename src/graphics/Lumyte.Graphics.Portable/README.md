# Portable GPU 基盤

WebGPU の resource と実行モデルに対応する独立した低レベル契約。[設計の正本](../../../docs/adr/0016-portable-api.md) は Portable ADR 群である。Native の backend を経由せず、Buffer／Texture は内部 memory を含めて生成・破棄する。

現在の範囲は device、要求 feature／limits、Buffer／Texture と mapping、View／Binding、raw WGSL、compute pipeline、直接／間接 dispatch、buffer copy と CPU timeline の非同期待機である。shader package／loader、raster／texture copy と Browser runtime の接続は後続段階とする。

## Device と直接入力

native host では `Lumyte.Graphics.WebGPU.WebGpuBackend.CreateAsync(options)` が、同梱 Dawn runtime の instance／adapter／device を所有する。旧 `IGpuBackend` 用 factory は `Lumyte.Graphics.WebGPU.Legacy.WebGpuBackend` へ移し、新しい Portable device と相互変換しない。

native host の callback と GPU 完了通知は、instance ごとの内部イベント処理が進行させる。未完了の native future がない間は休止する。利用者が polling する必要はなく、`CreateAsync`／`MapBufferAsync` の公開呼出しは非同期に復帰する。

WGSL の `immediate_address_space` と非ゼロの直接入力 capacity は初期化要件である。`MaxImmediateSize` を未指定なら adapter が提供する capacity を要求し、明示指定した場合はその値を要求する。有効な limits は作成した device から取得する。64 byte の固定 root ABI や、root を GPU buffer に退避する経路は設けない。raw WGSL と明示した program layout で compute shader を実行する。

`GpuBackendOptions.RequireDualSourceBlend` と `RequireIndirectFirstInstance` は任意 feature の要求で、`Capabilities` は有効になった機能だけを返す。`RequiredLimits` の nullable member は null と0を区別する。Max limits は必要容量の下限、Min offset alignments は許容制約の上限として runtime へ渡す。native C API の未指定 sentinel と衝突する明示値は表現不能として拒否する。

## Buffer の作成と mapping

```csharp
using Lumyte.Graphics.WebGPU;
using P = Lumyte.Graphics.Portable;

using var backend = await WebGpuBackend.CreateAsync(new P.GpuBackendOptions
{
    RequiredLimits = new() { MaxImmediateSize = 16 }
});
var upload = backend.CreateBuffer(new P.GpuBufferDescription(
    256, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource));
try
{
    using var mapped = await backend.MapBufferAsync(upload, P.GpuMapMode.Write, 64, 16);
    mapped.Memory.Span.Fill(42);
} // unmap してから Buffer を破棄する。
finally
{
    backend.DestroyBuffer(upload);
}
```

offset は Buffer 先頭からの byte offset である。Write mapping は `Memory<byte>`、Read mapping は `ReadOnlyMemory<byte>` を使う。Write mapping でも `ReadOnlyMemory` を取得できる。`Dispose` は unmap し、Buffer 自体を破棄しない。

unmap 後は保存した Memory から Span を取得し直す操作も拒否する。ただし、すでに取得済みの Span／pointer を失効させることはできないため、caller は unmap 前に全アクセスを終了し、Dispose 後にそれらを使用しない。mapping の length は `Memory<byte>` の int 長とホストの pointer 幅で表現できる必要がある。native の map alignment／usage／resource size の検証は runtime に委ねる。

## Texture、View と Binding

```csharp
var texture = backend.CreateTexture(new P.GpuTextureDescription(
    P.GpuTextureDimension.Texture2D,
    Width: 64, Height: 64, Depth: 1,
    MipCount: 1, LayerCount: 1, SampleCount: 1,
    Format: Lumyte.Graphics.GpuFormat.Rgba8Unorm,
    Usage: P.GpuTextureUsage.Sampled | P.GpuTextureUsage.CopyDestination,
    MutableFormat: true));
try
{
    var layout = backend.CreateBindingLayout([
        new(0, P.GpuShaderStage.Pixel, new P.GpuTextureBindingLayout(P.GpuTextureSampleType.Float)),
        new(1, P.GpuShaderStage.Pixel, new P.GpuSamplerBindingLayout(P.GpuSamplerBindingType.Filtering))
    ]);
    try
    {
        var bindings = backend.CreateBindings(layout, [
            P.GpuBindingEntry.Texture(0, new P.GpuTextureView(texture)),
            P.GpuBindingEntry.Sampler(1, new P.GpuSamplerDescription())
        ]);
        try
        {
            // この Pixel group を使う raster 描画は後続段階。
        }
        finally
        {
            backend.DestroyBindings(bindings);
        }
    }
    finally
    {
        backend.DestroyBindingLayout(layout);
    }
}
finally
{
    backend.DestroyTexture(texture);
}
```

事前の heap、allocation、配置要件は不要である。`MutableFormat` は runtime が許す互換 view format の意図であり、公開の許可 format 列を要求しない。Texture handle から配置情報や GPU address を取得する API は設けない。

View は Texture と解釈を組み合わせた非所有の値であり、public の生成・破棄操作は持たない。上の省略 description は Texture 全体の既定 view を表す。sampler も値で指定し、有効な既定値には `new GpuSamplerDescription()` を使う。`default(GpuSamplerDescription)` は構造体のゼロ値であり、無効な anisotropy を backend が自動補正することはない。

layout は uniform／read-only storage／storage Buffer、sampled／storage Texture と sampler を明示する。Buffer の範囲は `GpuBufferRange(buffer, offset, length)` で渡し、null length は残り全体を表す。layout と binding は呼出し中に入力 span を消費するため、復帰後に元の配列を変更できる。compute の dynamic offsets は `SetComputeBindings` に binding 番号の昇順で渡し、呼出し中にコピーする。

内部 view／sampler は同じ値を使う生存中の Bindings 間で再利用し、最後の参照とともに解放する。Bindings の破棄は元 Buffer／Texture と layout を破棄しない。

caller は全利用終了後に resource を一度だけ破棄し、その後に backend を破棄する。handle のコピーは resource を延命しない。backend に全 resource の registry、自動 staging、GC や暗黙の GPU wait は置かない。

## Compute と完了

以下は正常系の例である。root の2個の uint を直接渡し、shader が明示 storage binding へ書いた42を、明示 copy と mapping で読み戻す。既に作成した `backend` と `P` alias を使う。

```csharp
var output = backend.CreateBuffer(new P.GpuBufferDescription(
    4, P.GpuBufferUsage.Storage | P.GpuBufferUsage.CopySource));
var readback = backend.CreateBuffer(new P.GpuBufferDescription(
    4, P.GpuBufferUsage.CopyDestination | P.GpuBufferUsage.MapRead));
var layout = backend.CreateBindingLayout([
    new(0, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage))
]);
var bindings = backend.CreateBindings(layout, [P.GpuBindingEntry.Buffer(0, new(output))]);
var module = backend.CreateShaderModule("""
    requires immediate_address_space;
    struct Root { index: u32, value: u32 }
    var<immediate> root: Root;
    @group(0) @binding(0) var<storage, read_write> output: array<u32>;
    @compute @workgroup_size(1)
    fn main() { output[root.index] = root.value; }
    """);
var pipeline = backend.CreateComputePipeline(new P.GpuShaderProgramDescription(
    [new(module, P.GpuShaderStage.Compute, "main")], [layout], immediateSize: 8));

var queue = backend.MainQueue;
using var completed = queue.CreateSemaphore();
using var commands = queue.StartCommandRecording();
commands.BeginCompute();
commands.SetComputePipeline(pipeline);
commands.SetComputeBindings(0, bindings);
byte[] root = new byte[8];
System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(4), 42);
commands.SetComputeRootData(root);
commands.Dispatch(1);
commands.EndCompute();
commands.CopyBuffer(new(output), new(readback));

queue.Submit([commands], completed, 1);
await queue.WaitAsync(completed, 1);
using (var mapped = await backend.MapBufferAsync(readback, P.GpuMapMode.Read, 0, 4))
{
    Console.WriteLine(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(mapped.ReadOnlyMemory.Span));
}
backend.DestroyComputePipeline(pipeline);
backend.DestroyShaderModule(module);
backend.DestroyBindings(bindings);
backend.DestroyBindingLayout(layout);
backend.DestroyBuffer(readback);
backend.DestroyBuffer(output);
```

pipeline は dispatch を含む最初の Submit で実体化し、論理 handle の破棄まで再利用する。root は記録時にコピーし、各 dispatch で最後に指定した全 byte 数が program の ImmediateSize と一致することを確認する。pipeline 再設定時の暗黙 zero-fill、root の解析や Parameter Data の自動 upload は行わない。indirect dispatch は明示 range の先頭12 byteから3軸の group 数を読む。

一つの記録は生成元 queue に一回だけ提出する。複数記録を渡した batch は全 encode 後にまとめて提出する。`commands.Dispose()` は提出済み work を取り消さず、内部記録は GPU 利用終了まで保持する。caller は Buffer／Texture、binding、pipeline、module と layout を全利用終了まで維持する。

timeline の signal 値は単調増加とし、照会できるのは initial value と実際に受理した値だけである。`IsComplete` は GPU 利用終了だけを示し、`WaitAsync` の正常復帰が当該 batch の診断も含む成功を示す。`GpuExecutionException` は利用終了後の失敗で、`FenceValue` とコピー済み `Diagnostics` を持つ。待機取消しは GPU work の取消しや回収許可ではなく、device loss は未完了の待機にも通知する。失敗経路での所有は [提出と完了のADR](../../../docs/adr/0026-command-submission-and-synchronization.md) に従う。

## Runtime 診断

resource の同期生成は runtime の非同期診断の成功を保証しない。validation／out-of-memory／internal scope を生成操作に対応付け、診断結果をその object に保持する。map は buffer の生成診断と native map の双方を確認してから成功する。失敗時は `GpuOperationException` に操作名とコピー済み診断列を渡し、device loss は `GpuDeviceLostException` で通知する。

scope を開いた native 呼出し区間は同じ device で直列化し、全 scope を pop してから非同期結果を待つ。Bindings は layout、Buffer／Texture、内部 view／sampler と自身の生成診断を保持する。batch は参照した object と encode／submit の診断を観測する。無関係な object や先行 batch の失敗を一律に付け替えず、runtime の validator を複製しない。GPU 完了が先でも、診断確定前に WaitAsync の成功を返さない。

## Backend の追加

外部 assembly は `IPortableGpuBackend` と `IGpuQueue` を実装し、`GpuBufferHandle`、`GpuTextureHandle`、`GpuMappedBufferRange`、`GpuBindingLayoutHandle`、`GpuBindingsHandle`、`GpuShaderModuleHandle`、`GpuComputePipelineHandle`、`GpuCommandBuffer`、`GpuSemaphore` の public abstract 基底型と protected constructor から非公開実装を派生させる。Portable assembly の `InternalsVisibleTo` は不要である。

動作確認は隣接する [Portable.Tests](../Lumyte.Graphics.Portable.Tests/Lumyte.Graphics.Portable.Tests.csproj) の consumer test と、[WebGPU の適合試験](../Lumyte.Graphics.WebGPU.Tests/Integration/PORTABLE.md) に分ける。試験結果と後続作業は [進捗記録](../../../docs/designs/graphics-implementation-progress.md) を参照する。
