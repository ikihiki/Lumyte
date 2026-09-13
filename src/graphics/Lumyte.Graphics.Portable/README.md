# Portable GPU 基盤

WebGPU の resource と実行モデルに対応する独立した低レベル契約。[設計の正本](../../../docs/adr/0016-portable-api.md) は Portable ADR 群である。Native の backend を経由せず、Buffer／Texture は内部 memory を含めて生成・破棄する。

現在の範囲は device、要求 feature／limits、Buffer／Texture と mapping、View／Binding、raw WGSL、raster／compute pipeline、直接・indexed・indirect draw、直接／間接 dispatch、buffer／texture copy と CPU timeline の非同期待機である。native host の Dawn と Browser の WebGPU に独立した実装を置く。shader package／loader は後続段階とする。

## Device と直接入力

native host では `Lumyte.Graphics.WebGPU.WebGpuBackend.CreateAsync(options)` が、同梱 Dawn runtime の instance／adapter／device を所有する。旧 `IGpuBackend` 用 factory は `Lumyte.Graphics.WebGPU.Legacy.WebGpuBackend` へ移し、新しい Portable device と相互変換しない。

Browser host では `Lumyte.Graphics.WebGPU.Browser.WebGpuBrowserRuntime.LoadAsync(moduleUrl)` で配布 ES module を読み込み、同 namespace の `WebGpuBackend.CreateAsync(runtime, options)` へ渡す。以降は同じ Portable API を使う。runtime は caller 所有で、借用する backend をすべて終了してから破棄する。JSObject を扱う操作は runtime を作成した JavaScript thread で行う。[Browser の配信と実行例](../Lumyte.Graphics.WebGPU.Browser/README.md) に host 設定と制約を記す。

native host の callback と GPU 完了通知は、instance ごとの内部イベント処理が進行させる。未完了の native future がない間は休止する。利用者が polling する必要はなく、`CreateAsync`／`MapBufferAsync` の公開呼出しは非同期に復帰する。

WGSL の `immediate_address_space` と非ゼロの直接入力 capacity は初期化要件である。`MaxImmediateSize` を未指定なら adapter が提供する capacity を要求し、明示指定した場合はその値を要求する。有効な limits は作成した device から取得する。64 byte の固定 root ABI や、root を GPU buffer に退避する経路は設けない。raw WGSL と明示した program layout で raster／compute shader を実行する。

`GpuBackendOptions.RequireDualSourceBlend` と `RequireIndirectFirstInstance` は任意 feature の要求で、`Capabilities` は有効になった機能だけを返す。`RequiredLimits` の nullable member は null と0を区別する。Max limits は必要容量の下限、Min offset alignments は許容制約の上限として runtime へ渡す。native C API の未指定 sentinel と衝突する明示値は表現不能として拒否する。

Browser では C API の sentinel 制約を持ち込まず、未指定 property を省略する。GPU size／offset 等の ulong を JavaScript へ渡すときは、正確に表現できる最大値 `2^53 − 1` を超える値を拒否する。CPU timeline の値は JavaScript へ渡さず、ulong 全域を保持する。

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

native host は mapped pointer を直接包む。Browser は mapped ArrayBuffer と WASM memory を直接共有できないため、map 完了後に managed memory へコピーし、Write mapping の Dispose で同じ ArrayBuffer へ書き戻してから unmap する。追加の GPU buffer は作らず、root data はこの mapping 経路を通らない。

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
            // render pass 内で commands.SetBindings(0, bindings) に渡せる。
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

layout は uniform／read-only storage／storage Buffer、sampled／storage Texture と sampler を明示する。Buffer の範囲は `GpuBufferRange(buffer, offset, length)` で渡し、null length は残り全体を表す。layout と binding は呼出し中に入力 span を消費するため、復帰後に元の配列を変更できる。dynamic offsets は render の `SetBindings`、compute の `SetComputeBindings` に binding 番号の昇順で渡し、呼出し中にコピーする。

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

## Raster と Texture copy

次も正常系の例である。8 byte の root を vertex／pixel shader へ直接渡し、1 pixel の描画結果を読み戻す。module と pipeline を再利用するときは、下の破棄を最後の利用終了後まで遅らせる。

```csharp
var target = backend.CreateTexture(new P.GpuTextureDescription(
    P.GpuTextureDimension.Texture2D, 1, 1, 1, 1, 1, 1,
    Lumyte.Graphics.GpuFormat.Rgba8Unorm,
    P.GpuTextureUsage.ColorAttachment | P.GpuTextureUsage.CopySource));
var readback = backend.CreateBuffer(new P.GpuBufferDescription(
    4, P.GpuBufferUsage.CopyDestination | P.GpuBufferUsage.MapRead));
var module = backend.CreateShaderModule("""
    requires immediate_address_space;
    struct Root { color: u32, depth: f32 }
    var<immediate> root: Root;
    @vertex fn vertex(@builtin(vertex_index) id: u32) -> @builtin(position) vec4f {
        let positions = array<vec2f, 3>(vec2f(-1, -1), vec2f(3, -1), vec2f(-1, 3));
        return vec4f(positions[id], root.depth, 1);
    }
    @fragment fn fragment() -> @location(0) vec4f { return unpack4x8unorm(root.color); }
    """);
var pipeline = backend.CreateRasterPipeline(
    new P.GpuRasterPipelineDescription([new(Lumyte.Graphics.GpuFormat.Rgba8Unorm)]),
    new P.GpuShaderProgramDescription([
        new(module, P.GpuShaderStage.Vertex, "vertex"),
        new(module, P.GpuShaderStage.Pixel, "fragment")
    ], [], immediateSize: 8));

var queue = backend.MainQueue;
using var completed = queue.CreateSemaphore();
using var commands = queue.StartCommandRecording();
commands.BeginRendering([
    new(new(target), P.GpuAttachmentLoadOperation.Clear, ClearColor: new(0, 0, 1, 1))
]);
commands.SetPipeline(pipeline);
byte[] root = new byte[8];
System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(root, 0xff0000ff);
commands.SetRootData(root);
commands.Draw(3);
commands.EndRendering();
commands.CopyTextureToBuffer(target,
    new(0, P.GpuTextureAspect.All, new(0, 0, 0), new(1, 1, 1)), new(readback));
queue.Submit([commands], completed, 1);
await queue.WaitAsync(completed, 1);
using (var mapped = await backend.MapBufferAsync(readback, P.GpuMapMode.Read, 0, 4))
{
    Console.WriteLine(Convert.ToHexString(mapped.ReadOnlyMemory.Span)); // FF0000FF
}
backend.DestroyRasterPipeline(pipeline);
backend.DestroyShaderModule(module);
backend.DestroyBuffer(readback);
backend.DestroyTexture(target);
```

draw 用の vertex buffer layout は持たず、必要な頂点データは shader が明示 storage binding から読む。`DrawIndexed` は index range と Uint16／Uint32、signed baseVertex を受け取る。indirect draw は16 byte、indexed indirect draw は20 byteの引数を range の先頭から読む。

color attachment の `ResolveTarget` は MSAA の解決先、`DepthSlice` は3D view の描画先を表す。mip／layer は View で選び、複数 mip を持つ Texture の attachment には `MipCount: 1` を指定する。depth/stencil、blend、culling、topology、sample 条件は immutable pipeline に、viewport／scissor、stencil reference と blend constant は command に設定する。内部 attachment view は GPU 利用終了で解放する。

`GpuTextureCopyFootprint` は mip／aspect／origin／extent と byte 単位の row／image pitch を表す。`RequiredBytes(format)` は最後の行の後の padding を含めず、必要な buffer span の長さを計算する。pitch の0は native descriptor で省略する指定であり、複数行を自動で256 byte境界へ整列する機能ではない。Buffer との copy の offset は `GpuBufferRange.Offset` で指定する。Texture 間 copy は両 footprint の extent を一致させ、buffer 用 pitch は使用しない。

## Runtime 診断

resource の同期生成は runtime の非同期診断の成功を保証しない。validation／out-of-memory／internal scope を生成操作に対応付け、診断結果をその object に保持する。map は buffer の生成診断と native map の双方を確認してから成功する。失敗時は `GpuOperationException` に操作名とコピー済み診断列を渡し、device loss は `GpuDeviceLostException` で通知する。

scope を開いた native 呼出し区間は同じ device で直列化し、全 scope を pop してから非同期結果を待つ。Bindings は layout、Buffer／Texture、内部 view／sampler と自身の生成診断を保持する。batch は参照した object と encode／submit の診断を観測する。無関係な object や先行 batch の失敗を一律に付け替えず、runtime の validator を複製しない。GPU 完了が先でも、診断確定前に WaitAsync の成功を返さない。

## Backend の追加

外部 assembly は `IPortableGpuBackend` と `IGpuQueue` を実装し、`GpuBufferHandle`、`GpuTextureHandle`、`GpuMappedBufferRange`、`GpuBindingLayoutHandle`、`GpuBindingsHandle`、`GpuShaderModuleHandle`、`GpuRasterPipelineHandle`、`GpuComputePipelineHandle`、`GpuCommandBuffer`、`GpuSemaphore` の public abstract 基底型と protected constructor から非公開実装を派生させる。Portable assembly の `InternalsVisibleTo` は不要である。

動作確認は隣接する [Portable.Tests](../Lumyte.Graphics.Portable.Tests/Lumyte.Graphics.Portable.Tests.csproj) の consumer test、[Dawn の適合試験](../Lumyte.Graphics.WebGPU.Tests/Integration/PORTABLE.md)、[Browser の適合試験](../Lumyte.Graphics.WebGPU.Browser.Tests/Integration/README.md) に分ける。試験結果と後続作業は [進捗記録](../../../docs/designs/graphics-implementation-progress.md) を参照する。
