using System.Buffers.Binary;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Tests;
using Lumyte.Graphics.Validation;
using Xunit;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Runs the optimized shader upload path through native D3D12 and DXIL.</summary>
public sealed class ShaderTransferGpuTests
{
    /// <summary>Checks mapped batch writes and partial overwrites against literal D3D12 reference bytes.</summary>
    /// <param name="count">The number of values in the batch.</param>
    /// <returns>The completed byte-exact GPU copy check.</returns>
    [AgilityTheory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(1024)]
    public async Task MappedBatchAndPartialWritesRoundtripAsync(int count)
    {
        ShaderArtifact artifact = await ExplicitTransferExercise.CompileAsync(ShaderTarget.Dxil);
        using var device = Direct3D12Device.Create(new() { UseWarp = true, EnableDebugLayer = true });
        using IGraphicsBuffer<uint> target = device.CreateBuffer<uint>(new() { Count = 8, Usage = BufferUsage.ShaderRead });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 1 });
        IGpuRef<uint> reference = table.WriteBuffer(0, target.SliceElements(3, 2), ShaderBufferAccess.ReadOnly);
        using IGraphicsShaderDataBuffer<ExplicitTransferValue> upload = device.CreateShaderDataBuffer<ExplicitTransferValue>(artifact, (ulong)count + 2, MemoryPreference.Upload);
        Direct3D12ShaderDataBuffer<ExplicitTransferValue> native = Assert.IsType<Direct3D12ShaderDataBuffer<ExplicitTransferValue>>(upload);
        Assert.Equal(20UL, upload.ShaderElementStrideInBytes);
        ExplicitTransferValue[] values = Enumerable.Range(0, count + 2).Select(index => new ExplicitTransferValue { Value = (uint)index, Target = reference }).ToArray();
        await upload.MapAsync();
        upload.CopyFrom(values);
        for (int i = 1; i <= count; i++)
        {
            values[i].Value += 100;
        }

        upload.CopyFrom(values.AsSpan(1, count), 1);
        int middle = 1 + (count / 2);
        values[middle].Value = 0x12345678;
        upload.CopyFrom(values.AsSpan(middle, 1), (ulong)middle);
        upload.Unmap();

        byte[] expected = new byte[checked((count + 2) * 20)];
        for (int i = 0; i < values.Length; i++)
        {
            Span<byte> row = expected.AsSpan(i * 20, 20);
            BinaryPrimitives.WriteUInt32LittleEndian(row, values[i].Value);
            BinaryPrimitives.WriteUInt32LittleEndian(row[8..], 12);
            BinaryPrimitives.WriteUInt32LittleEndian(row[12..], 2);
        }

        values[middle].Value = 0;
        values[middle].Target = null;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = upload.SizeInBytes, Memory = MemoryPreference.Readback, Usage = BufferUsage.CopyDestination });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        BufferSlice<byte> source = native.Storage.SliceElements(0, upload.SizeInBytes);
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = source, Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(source, readback.SliceElements(0, readback.Count));
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        byte[] actual = new byte[expected.Length];
        readback.CopyTo(actual);
        readback.Unmap();
        Assert.Equal(expected, actual);
    }

    /// <summary>Checks explicit numeric and reference transfer boundaries against the shared Vulkan exercise.</summary>
    /// <param name="validation">Whether to enable the managed validation layer.</param>
    /// <returns>The completed GPU readback checks.</returns>
    [AgilityTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeclarationsStayLiveUntilExplicitTransferAsync(bool validation)
    {
        ShaderArtifact artifact = await ExplicitTransferExercise.CompileAsync(ShaderTarget.Dxil);
        using var raw = Direct3D12Device.Create(new() { UseWarp = true, EnableDebugLayer = true });
        using ValidationDevice? layer = validation ? new(raw) : null;
        IGraphicDevice device = layer ?? (IGraphicDevice)raw;
        await ExplicitTransferExercise.CheckAsync(device, artifact, validation);
    }
}
