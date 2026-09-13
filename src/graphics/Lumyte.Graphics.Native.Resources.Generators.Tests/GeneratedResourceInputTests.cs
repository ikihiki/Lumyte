using System.Buffers.Binary;
using Lumyte.Graphics.Native.Resources.Tests;
using Xunit;
using Inputs = Consumer.@namespace.@struct;

namespace Lumyte.Graphics.Native.Resources.Generators.Tests;

public sealed class GeneratedResourceInputTests
{
    [Fact]
    public async Task PreparedOffsetsWriteAddressesAndIndicesWithoutChangingScalarBytes()
    {
        using var backend = new TestResourceBackend();
        await using var manager = new GpuResourceManager(backend, new(BlockSize: 1024));
        using var scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(new(64));
        GpuViewRef view = scope.GetView(buffer, new GpuBufferViewDescription(0, 64));
        scope.GetSampler(new NativeGpuSamplerDescription(MaxLod: 1));
        GpuSamplerRef sampler = scope.GetSampler(new NativeGpuSamplerDescription());
        var inputs = new Inputs(buffer, view, sampler, buffer);
        byte[] bytes = Enumerable.Repeat((byte)0xA5, Inputs.ByteSize + 4).ToArray();

        inputs.Write(manager, bytes);

        byte[] expected = Enumerable.Repeat((byte)0xA5, bytes.Length).ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(expected.AsSpan(0), manager.GetGpuAddress(buffer));
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(12), manager.GetShaderIndex(view));
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(20), manager.GetShaderIndex(sampler));
        BinaryPrimitives.WriteUInt64LittleEndian(expected.AsSpan(24), manager.GetGpuAddress(buffer));
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public async Task RetainKeepsAllGeneratedReferencesAvailableAfterScopeRelease()
    {
        using var backend = new TestResourceBackend();
        await using var manager = new GpuResourceManager(backend, new(BlockSize: 1024));
        using var scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(new(64));
        GpuViewRef view = scope.GetView(buffer, new GpuBufferViewDescription(0, 64));
        GpuSamplerRef sampler = scope.GetSampler(new NativeGpuSamplerDescription());
        var inputs = new Inputs(buffer, view, sampler, buffer);
        using GpuResourceBatch batch = manager.BeginBatch();

        inputs.Retain(batch);
        scope.Dispose();
        manager.Collect();

        Assert.Equal(3, manager.Statistics.ResourceCount);
        inputs.Write(manager, new byte[Inputs.ByteSize]);
        batch.Dispose();
        manager.Collect();
        Assert.Equal(0, manager.Statistics.ResourceCount);
    }

    [Fact]
    public async Task ShortDestinationIsRejectedBeforeWritingAnyBytes()
    {
        using var backend = new TestResourceBackend();
        await using var manager = new GpuResourceManager(backend, new(BlockSize: 1024));
        using var scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(new(64));
        GpuViewRef view = scope.GetView(buffer, new GpuBufferViewDescription(0, 64));
        GpuSamplerRef sampler = scope.GetSampler(new NativeGpuSamplerDescription());
        var inputs = new Inputs(buffer, view, sampler, buffer);
        byte[] bytes = Enumerable.Repeat((byte)0xA5, Inputs.ByteSize - 1).ToArray();

        ArgumentException error = Assert.Throws<ArgumentException>(() => inputs.Write(manager, bytes));

        Assert.Equal("destination", error.ParamName);
        Assert.All(bytes, value => Assert.Equal(0xA5, value));
    }
}
