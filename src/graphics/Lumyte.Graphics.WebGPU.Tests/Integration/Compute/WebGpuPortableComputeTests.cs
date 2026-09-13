using System.Buffers.Binary;
using System.Runtime.InteropServices;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableComputeTests
{
    internal const string RootWriter = """
        requires immediate_address_space;
        struct Root { destination: u32, value: u32 }
        var<immediate> root: Root;
        @group(0) @binding(0) var<storage, read_write> output: array<u32>;
        @compute @workgroup_size(1)
        fn main() { output[root.destination] = root.value; }
        """;

    private const string PaddedRootWriter = """
        requires immediate_address_space;
        struct Root { destination: u32, payload: vec3u, tail: u32 }
        var<immediate> root: Root;
        @group(0) @binding(0) var<storage, read_write> output: array<u32>;
        @compute @workgroup_size(1)
        fn main() { output[root.destination] = root.payload.x + root.payload.y * 10u + root.payload.z * 100u + root.tail * 1000u; }
        """;

    [Theory]
    [InlineData(8)]
    [InlineData(32)]
    public async Task DirectRootBytesAreCopiedAtRecordingAndReachWgslWithTheirOwnLayout(int rootSize)
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        P.GpuBindingLayoutHandle layout = fixture.Layout(new P.GpuBindingLayoutEntry(0, P.GpuShaderStage.Compute,
            new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage)));
        P.GpuBufferHandle output = fixture.Buffer(8, P.GpuBufferUsage.Storage | P.GpuBufferUsage.CopySource);
        P.GpuBufferHandle readback = fixture.Buffer(8, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuBindingsHandle bindings = fixture.Bindings(layout, P.GpuBindingEntry.Buffer(0, new(output, 0, 8)));
        P.GpuComputePipelineHandle pipeline = fixture.Pipeline(rootSize == 8 ? RootWriter : PaddedRootWriter,
            (uint)rootSize, "main", layout);
        byte[] root = new byte[rootSize];
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginCompute();
        commands.SetComputePipeline(pipeline);
        commands.SetComputeBindings(0, bindings);

        FillRoot(root, 0, rootSize == 8 ? 111u : 5432u);
        commands.SetComputeRootData(root);
        commands.Dispatch(1);
        FillRoot(root, 1, rootSize == 8 ? 222u : 9876u);
        commands.SetComputeRootData(root);
        commands.Dispatch(1);
        Array.Fill(root, (byte)0xff);
        commands.EndCompute();
        commands.CopyBuffer(new(output, 0, 8), new(readback, 0, 8));
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(rootSize == 8 ? new uint[] { 111, 222 } : [5432u, 9876u], await fixture.ReadAsync(readback, 2));
    }

    [Fact]
    public async Task GenericRootInputUsesTheUnmanagedValueAtTheCall()
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        P.GpuBindingLayoutHandle layout = fixture.Layout(new P.GpuBindingLayoutEntry(0, P.GpuShaderStage.Compute,
            new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage)));
        P.GpuBufferHandle output = fixture.Buffer(8, P.GpuBufferUsage.Storage | P.GpuBufferUsage.CopySource);
        P.GpuBufferHandle readback = fixture.Buffer(8, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuBindingsHandle bindings = fixture.Bindings(layout, P.GpuBindingEntry.Buffer(0, new(output, 0, 8)));
        P.GpuComputePipelineHandle pipeline = fixture.Pipeline(RootWriter, 8, "main", layout);
        var root = new RootData { Destination = 1, Value = 1234 };
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginCompute();
        commands.SetComputePipeline(pipeline);
        commands.SetComputeBindings(0, bindings);

        commands.SetComputeRootData(in root);
        root.Value = 9999;
        commands.Dispatch(1);
        commands.EndCompute();
        commands.CopyBuffer(new(output, 0, 8), new(readback, 0, 8));
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(new uint[] { 0, 1234 }, await fixture.ReadAsync(readback, 2));
    }

    [Fact]
    public async Task DynamicOffsetsAreCopiedAndAppliedInBindingNumberOrder()
    {
        using WebGpuPortableComputeFixture fixture = await WebGpuPortableComputeFixture.CreateAsync();
        uint alignment = fixture.Backend.Limits.MinUniformBufferOffsetAlignment;
        uint[] parameters = new uint[checked((int)(alignment * 4 / 4))];
        parameters[0] = 3;
        parameters[alignment / 4] = 5;
        parameters[alignment * 2 / 4] = 7;
        parameters[alignment * 3 / 4] = 11;
        P.GpuBufferHandle upload = await fixture.UploadAsync(parameters);
        P.GpuBufferHandle uniforms = fixture.Buffer((ulong)alignment * 4, P.GpuBufferUsage.Uniform | P.GpuBufferUsage.CopyDestination);
        P.GpuBufferHandle output = fixture.Buffer(8, P.GpuBufferUsage.Storage | P.GpuBufferUsage.CopySource);
        P.GpuBufferHandle readback = fixture.Buffer(8, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuBindingLayoutHandle layout = fixture.Layout(
            new(7, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Uniform, 16, true)),
            new(2, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage)),
            new(1, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Uniform, 16, true)));
        P.GpuBindingsHandle bindings = fixture.Bindings(layout,
            P.GpuBindingEntry.Buffer(7, new(uniforms, 0, 16)),
            P.GpuBindingEntry.Buffer(2, new(output, 0, 8)),
            P.GpuBindingEntry.Buffer(1, new(uniforms, alignment, 16)));
        const string source = """
            requires immediate_address_space;
            struct Parameters { value: vec4u }
            struct Root { destination: u32, bias: u32 }
            var<immediate> root: Root;
            @group(0) @binding(1) var<uniform> a: Parameters;
            @group(0) @binding(7) var<uniform> b: Parameters;
            @group(0) @binding(2) var<storage, read_write> output: array<u32>;
            @compute @workgroup_size(1)
            fn main() { output[root.destination] = a.value.x * 100u + b.value.x + root.bias; }
            """;
        P.GpuComputePipelineHandle pipeline = fixture.Pipeline(source, 8, "main", layout);
        uint[] offsets = [alignment, alignment * 3];
        P.GpuCommandBuffer commands = fixture.Record();
        commands.CopyBuffer(new(upload), new(uniforms));
        commands.BeginCompute();
        commands.SetComputePipeline(pipeline);

        commands.SetComputeBindings(0, bindings, offsets);
        Array.Clear(offsets);
        commands.SetComputeRootData(new RootData { Destination = 0, Value = 13 });
        commands.Dispatch(1);
        commands.EndCompute();
        commands.CopyBuffer(new(output), new(readback));
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(new uint[] { 724, 0 }, await fixture.ReadAsync(readback, 2));
    }

    private static void FillRoot(byte[] root, uint destination, uint value)
    {
        Array.Clear(root);
        BinaryPrimitives.WriteUInt32LittleEndian(root, destination);
        if (root.Length == 8) { BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(4), value); }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(16), value % 10);
            BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(20), value / 10 % 10);
            BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(24), value / 100 % 10);
            BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(28), value / 1000);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RootData
    {
        internal uint Destination;
        internal uint Value;
    }
}
