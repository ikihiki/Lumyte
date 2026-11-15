using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks actual float4 storage loads preserve opaque integer bits without arithmetic.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks zeros, subnormals, infinities and NaN-looking integer patterns on Vulkan.</summary>
    /// <returns>The completed GPU readback.</returns>
    [GpuFact]
    public async Task VulkanNumericStreamBitsAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckNumericStreamBitsAsync(device);
    }

    /// <summary>Checks the identical float4 bitcasts through the WGSL helper.</summary>
    /// <returns>The completed GPU readback.</returns>
    [GpuFact]
    public async Task WgpuNumericStreamBitsAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckNumericStreamBitsAsync(device);
    }

    private static async Task CheckNumericStreamBitsAsync(IGraphicDevice device)
    {
        uint[] bits = [0, 0x80000000, 1, 0x007fffff, 0x00800000, 0x3f800000, 0x7f800000, 0xff800000, 0x7fc01234, 0x7fa12345, 0xffffffff, 0xdeadbeef, 65535, 131071, 262143, uint.MaxValue];
        var cells = new Vector4[4];
        for (int i = 0; i < bits.Length; i++)
        {
            cells[i / 4][i % 4] = BitConverter.UInt32BitsToSingle(bits[i]);
        }

        using IGraphicsBuffer<Vector4> upload = device.CreateBuffer<Vector4>(new() { Count = 4, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        using IGraphicsBuffer<Vector4> data = device.CreateBuffer<Vector4>(new() { Count = 4, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead });
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 16, Usage = BufferUsage.ShaderWrite | BufferUsage.CopySource });
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = 16, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        await upload.MapAsync();
        upload.CopyFrom(cells);
        upload.Unmap();
        using var plan = new ArgumentTablePlan(device);
        ArgumentTableReservation resources = plan.Reserve(0);
        IGpuRef<Vector4>? inputRef = null;
        IGpuRef<uint>? outputRef = null;
        resources.RegisterBuffer(data.SliceElements(0, 4), reference => inputRef = reference);
        resources.RegisterBuffer(output.SliceElements(0, 16), reference => outputRef = reference, ArgumentBufferAccess.Write);
        plan.Materialize();
        using IGraphicsShader shader = device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(Drawing2DTests).Assembly, "Lumyte.Graphics.Tests.numeric-stream-bits.lshader"));
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = upload.SliceElements(0, 4), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = _copyRead });
        commands.CopyBuffer(upload.SliceElements(0, 4), data.SliceElements(0, 4));
        commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = data.SliceElements(0, 4), Before = _copyWrite, After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
        IComputeEncoder compute = commands.BeginComputePass(new());
        try
        {
            compute.SetPipeline(pipeline);
            var arguments = new NumericBitsArguments(inputRef!, outputRef!);
            compute.SetArgumentTable(resources.Table);
            compute.SetArguments(in arguments);
            compute.Dispatch(16);
        }
        finally
        {
            compute.End();
        }

        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = output.SliceElements(0, 16), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = _copyRead });
        commands.CopyBuffer(output.SliceElements(0, 16), readback.SliceElements(0, 16));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = readback.SliceElements(0, 16), Before = _copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        try
        {
            await submission.WaitAsync();
        }
        catch
        {
            await device.WaitForRetirementAsync();
            throw;
        }

        await readback.MapAsync();
        uint[] actual = new uint[16];
        readback.CopyTo(actual);
        readback.Unmap();
        Assert.Equal(bits, actual);
    }
}
