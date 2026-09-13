using System.Buffers.Binary;
using System.Runtime.InteropServices;
using P = Lumyte.Graphics.Portable;

public static partial class BrowserCases
{
    private const string RootWriter = """
        requires immediate_address_space;
        struct Root { index: u32, value: u32 }
        var<immediate> root: Root;
        @group(0) @binding(0) var<storage, read_write> output: array<u32>;
        @compute @workgroup_size(1) fn main() { output[root.index] = root.value; }
        """;

    private static async Task<object> DeviceAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        return new { browser = OperatingSystem.IsBrowser(), direct = fixture.Backend.Capabilities.DirectRootData, immediateSize = fixture.Backend.Limits.MaxImmediateSize };
    }

    private static async Task<object> ComputeEightAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        P.GpuBufferHandle output = fixture.Buffer(8, P.GpuBufferUsage.Storage | P.GpuBufferUsage.CopySource);
        P.GpuBufferHandle readback = fixture.Buffer(8, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuBindingLayoutHandle layout = fixture.Layout();
        P.GpuBindingsHandle bindings = fixture.Bindings(layout, output);
        P.GpuComputePipelineHandle pipeline = fixture.Compute(RootWriter, 8, "main", layout);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginCompute();
        commands.SetComputePipeline(pipeline);
        commands.SetComputeBindings(0, bindings);
        byte[] root = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(4), 40);
        commands.SetComputeRootData(root);
        commands.Dispatch(1);
        BinaryPrimitives.WriteUInt32LittleEndian(root, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(4), 74);
        commands.SetComputeRootData(root);
        commands.SetComputePipeline(pipeline);
        commands.Dispatch(1);
        Array.Fill(root, (byte)99);
        commands.EndCompute();
        commands.CopyBuffer(new(output), new(readback));
        await fixture.SubmitAsync(commands);
        byte[] actual = await fixture.ReadAsync(readback, 8);
        return new { actual = MemoryMarshal.Cast<byte, uint>(actual).ToArray() };
    }

    private static async Task<object> ComputeMixedAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        P.GpuBufferHandle output = fixture.Buffer(8, P.GpuBufferUsage.Storage | P.GpuBufferUsage.CopySource);
        P.GpuBufferHandle readback = fixture.Buffer(8, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuBindingLayoutHandle layout = fixture.Layout();
        P.GpuBindingsHandle bindings = fixture.Bindings(layout, output);
        P.GpuComputePipelineHandle pipeline = fixture.Compute("""
            requires immediate_address_space;
            struct Root { count: u32, gain: f32, offset: vec3f, index: u32 }
            var<immediate> root: Root;
            @group(0) @binding(0) var<storage, read_write> output: array<f32>;
            @compute @workgroup_size(1) fn main() { output[root.index] = f32(root.count) * root.gain + root.offset.x; }
            """, 32, "main", layout);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginCompute();
        commands.SetComputePipeline(pipeline);
        commands.SetComputeBindings(0, bindings);
        MixedRoot root = new() { Count = 3, Gain = 2.5f, X = 1, Y = 2, Z = 3, Index = 0 };
        commands.SetComputeRootData(root);
        commands.Dispatch(1);
        root = new() { Count = 5, Gain = -2, X = 4, Y = 6, Z = 8, Index = 1 };
        commands.SetComputeRootData(root);
        commands.Dispatch(1);
        commands.EndCompute();
        commands.CopyBuffer(new(output), new(readback));
        await fixture.SubmitAsync(commands);
        byte[] actual = await fixture.ReadAsync(readback, 8);
        return new { actual = MemoryMarshal.Cast<byte, float>(actual).ToArray(), rootSize = Marshal.SizeOf<MixedRoot>() };
    }

    private static async Task<object> IndirectComputeAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        P.GpuBufferHandle arguments = fixture.Buffer(32, P.GpuBufferUsage.Storage | P.GpuBufferUsage.IndirectArguments);
        P.GpuBufferHandle output = fixture.Buffer(8, P.GpuBufferUsage.Storage | P.GpuBufferUsage.CopySource);
        P.GpuBufferHandle readback = fixture.Buffer(8, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuBindingLayoutHandle layout = fixture.Layout();
        P.GpuBindingsHandle argumentBindings = fixture.Bindings(layout, arguments);
        P.GpuBindingsHandle outputBindings = fixture.Bindings(layout, output);
        P.GpuComputePipelineHandle producer = fixture.Compute("""
            @group(0) @binding(0) var<storage, read_write> args: array<u32>;
            @compute @workgroup_size(1) fn main() { args[4] = 2u; args[5] = 1u; args[6] = 1u; }
            """, 0, "main", layout);
        P.GpuComputePipelineHandle consumer = fixture.Compute("""
            requires immediate_address_space;
            var<immediate> value: u32;
            @group(0) @binding(0) var<storage, read_write> output: array<u32>;
            @compute @workgroup_size(1) fn main(@builtin(global_invocation_id) id: vec3u) { output[id.x] = value + id.x; }
            """, 4, "main", layout);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginCompute();
        commands.SetComputePipeline(producer);
        commands.SetComputeBindings(0, argumentBindings);
        commands.Dispatch(1);
        commands.EndCompute();
        commands.BeginCompute();
        commands.SetComputePipeline(consumer);
        commands.SetComputeBindings(0, outputBindings);
        commands.SetComputeRootData(37u);
        commands.DispatchIndirect(new(arguments, 16, 12));
        commands.EndCompute();
        commands.CopyBuffer(new(output), new(readback));
        await fixture.SubmitAsync(commands);
        byte[] actual = await fixture.ReadAsync(readback, 8);
        return new { actual = MemoryMarshal.Cast<byte, uint>(actual).ToArray() };
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct MixedRoot
    {
        [FieldOffset(0)] public uint Count;
        [FieldOffset(4)] public float Gain;
        [FieldOffset(16)] public float X;
        [FieldOffset(20)] public float Y;
        [FieldOffset(24)] public float Z;
        [FieldOffset(28)] public uint Index;
    }
}
