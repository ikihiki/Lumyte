using System.Runtime.InteropServices;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableIndirectComputeTests
{
    [Fact]
    public async Task GpuGeneratedDispatchCountsAreReadAtTheSuppliedNonzeroArgumentOffset()
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        P.GpuBindingLayoutHandle layout = fixture.Layout(new P.GpuBindingLayoutEntry(0, P.GpuShaderStage.Compute,
            new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage)));
        P.GpuBufferHandle arguments = fixture.Buffer(32,
            P.GpuBufferUsage.Storage | P.GpuBufferUsage.IndirectArguments | P.GpuBufferUsage.CopySource);
        P.GpuBufferHandle output = fixture.Buffer(48, P.GpuBufferUsage.Storage | P.GpuBufferUsage.CopySource);
        P.GpuBufferHandle readback = fixture.Buffer(48, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuBufferHandle argumentReadback = fixture.Buffer(16, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuBindingsHandle producerBindings = fixture.Bindings(layout, P.GpuBindingEntry.Buffer(0, new(arguments)));
        P.GpuBindingsHandle consumerBindings = fixture.Bindings(layout, P.GpuBindingEntry.Buffer(0, new(output)));
        const string producerSource = """
            requires immediate_address_space;
            struct Root { x: u32, y: u32, z: u32, offset: u32 }
            var<immediate> root: Root;
            @group(0) @binding(0) var<storage, read_write> arguments: array<u32>;
            @compute @workgroup_size(1)
            fn main() {
                arguments[root.offset] = root.x;
                arguments[root.offset + 1u] = root.y;
                arguments[root.offset + 2u] = root.z;
            }
            """;
        const string consumerSource = """
            requires immediate_address_space;
            struct Root { width: u32, height: u32, bias: u32, padding: u32 }
            var<immediate> root: Root;
            @group(0) @binding(0) var<storage, read_write> output: array<u32>;
            @compute @workgroup_size(1)
            fn main(@builtin(global_invocation_id) id: vec3u) {
                let index = id.x + root.width * (id.y + root.height * id.z);
                output[index] = root.bias + index;
            }
            """;
        P.GpuComputePipelineHandle producer = fixture.Pipeline(producerSource, 16, "main", layout);
        P.GpuComputePipelineHandle consumer = fixture.Pipeline(consumerSource, 16, "main", layout);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginCompute();
        commands.SetComputePipeline(producer);
        commands.SetComputeBindings(0, producerBindings);
        commands.SetComputeRootData(MemoryMarshal.AsBytes(new uint[] { 2, 3, 2, 4 }.AsSpan()));
        commands.Dispatch(1);
        commands.EndCompute();

        commands.BeginCompute();
        commands.SetComputePipeline(consumer);
        commands.SetComputeBindings(0, consumerBindings);
        commands.SetComputeRootData(MemoryMarshal.AsBytes(new uint[] { 2, 3, 100, 0 }.AsSpan()));
        commands.DispatchIndirect(new(arguments, 16, 12));
        commands.EndCompute();
        commands.CopyBuffer(new(output), new(readback));
        commands.CopyBuffer(new(arguments, 16, 12), new(argumentReadback, 0, 12));
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(new uint[] { 2, 3, 2 }, await fixture.ReadAsync(argumentReadback, 3));
        Assert.Equal(Enumerable.Range(100, 12).Select(static value => (uint)value), await fixture.ReadAsync(readback, 12));
    }
}
