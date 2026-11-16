using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Vulkan;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks compute parity and allocator lifetime independently of graphics encoders.</summary>
public sealed class VulkanDescriptorGenerationTests
{
    /// <summary>Full registration identity and sampled layout metadata invalidate independently.</summary>
    [Fact]
    public void RegistrationSnapshotsPreserveRangesAndSparseShape()
    {
        var table = new VulkanArgumentTable(null!, new() { TextureCapacity = uint.MaxValue, SamplerCapacity = 8, BufferCapacity = 2 });
        var textures = (Dictionary<uint, ArgumentRegistration>)typeof(VulkanArgumentTable).GetField("_textures", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(table)!;
        var buffers = (Dictionary<uint, ArgumentRegistration>)typeof(VulkanArgumentTable).GetField("_buffers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(table)!;
        textures.Add(7, new(table, 7, new object(), 0, 0, 1, ShaderBufferAccess.ReadOnly));
        buffers.Add(1, new(table, 1, new object(), 16, 32, 2, ShaderBufferAccess.ReadOnly));
        IShaderReference[] first = table.SnapshotRegistrations();
        Assert.Same(first, table.SnapshotRegistrations());
        Assert.Equal(new DescriptorLayoutKey(8, 1), table.LayoutKey);
        Assert.Equal(table.LayoutKey, table.LayoutKey);
        Assert.Equal(1, table.SnapshotBuildCount);
        Assert.Equal(1, table.LayoutScanCount);
        table.ReleaseBuffer(1);
        IShaderReference[] second = table.SnapshotRegistrations();
        Assert.NotSame(first, second);
        Assert.Equal(16UL, first.Single(r => r.Slot == 1).OffsetInBytes);
        Assert.Single(second);
        Assert.Equal(new DescriptorLayoutKey(8, 1), table.LayoutKey);
        Assert.Equal(1, table.LayoutScanCount);
        table.ReleaseBuffer(1);
        Assert.Same(second, table.SnapshotRegistrations());
        table.ReleaseTexture(7);
        Assert.Empty(table.SnapshotRegistrations());
        Assert.Equal(new DescriptorLayoutKey(1, 1), table.LayoutKey);
        Assert.Equal(2, table.LayoutScanCount);
        textures.Add(uint.MaxValue, new(table, uint.MaxValue, new object(), 0, 0, 1, ShaderBufferAccess.ReadOnly));
        typeof(VulkanArgumentTable).GetField("_layoutKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(table, null);
        Assert.Throws<OverflowException>(() => table.LayoutKey);
        Assert.Equal(2, table.LayoutScanCount);
        table.Dispose();
        Assert.Throws<ObjectDisposedException>(() => table.SnapshotRegistrations());
        Assert.Throws<ObjectDisposedException>(() => table.LayoutKey);
    }

    /// <summary>A dispatch seals sampled descriptors even without root arguments.</summary>
    /// <returns>The completed shader compilation and assertions.</returns>
    [GpuFact]
    public async Task DispatchSealsAndCoalescesDescriptorGenerationsAsync()
    {
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new()
        {
            Source = "[numthreads(1,1,1)] void main() {}",
            Stage = ShaderStage.Compute,
            Target = ShaderTarget.SpirV,
        });
        using var device = VulkanDevice.Create();
        using IGraphicsShader shader = device.CreateShader(artifact);
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsSampler sampler = device.CreateSampler(new());
        using IArgumentTable table = device.CreateArgumentTable(new() { SamplerCapacity = 1 });
        VulkanArgumentTable native = Assert.IsType<VulkanArgumentTable>(table);
        table.WriteSampler(0, sampler);
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        IComputeEncoder encoder = commands.BeginComputePass(new());
        encoder.SetPipeline(pipeline);
        encoder.SetArgumentTable(table);
        encoder.Dispatch(1);
        encoder.Dispatch(1);
        Assert.Equal(1, device.Descriptors.AllocatedSets);
        Assert.Equal(1, native.SnapshotBuildCount);
        Assert.Equal(1, native.LayoutScanCount);
        Assert.Equal(1, BindCount(encoder, "_pipelineBindCount"));
        Assert.Equal(1, BindCount(encoder, "_descriptorBindCount"));
        table.WriteSampler(0, sampler);
        table.ReleaseSampler(0);
        table.WriteSampler(0, sampler);
        Assert.Equal(1, native.DescriptorGeneration);
        Assert.Equal(1, device.Descriptors.AllocatedSets);
        encoder.Dispatch(1);
        Assert.Equal(2, device.Descriptors.AllocatedSets);
        Assert.Equal(1, BindCount(encoder, "_pipelineBindCount"));
        Assert.Equal(2, BindCount(encoder, "_descriptorBindCount"));
        encoder.End();
        commands.Finish();
        table.Dispose();
        Assert.Equal(2, device.Descriptors.LiveSets);
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        Assert.Equal(2, device.Descriptors.LiveSets);
        commands.Dispose();
        Assert.Equal(0, device.Descriptors.LiveSets);
        Assert.Equal(0, device.Descriptors.PoolCount);
    }

    /// <summary>Different canonical shapes allocate adequate pool descriptor counts and reclaim independently.</summary>
    [GpuFact]
    public unsafe void DescriptorShapesUseSeparatePagesAndPruneImmediately()
    {
        using var device = VulkanDevice.Create();
        Silk.NET.Vulkan.DescriptorSetLayout small = VulkanShaderBinding.CreateLayout(device);
        Silk.NET.Vulkan.DescriptorSetLayout large = VulkanShaderBinding.CreateLayout(device, 2, 2);
        try
        {
            using VulkanDescriptorAllocator.Lease one = device.Descriptors.Allocate(small, 1, 1);
            using VulkanDescriptorAllocator.Lease two = device.Descriptors.Allocate(large, 2, 2);
            Assert.Equal(2, device.Descriptors.PoolCount);
            one.Dispose();
            Assert.Equal(1, device.Descriptors.PoolCount);
            two.Dispose();
            Assert.Equal(0, device.Descriptors.PoolCount);
        }
        finally
        {
            device.Api.DestroyDescriptorSetLayout(device.NativeDevice, small, null);
            device.Api.DestroyDescriptorSetLayout(device.NativeDevice, large, null);
        }
    }

    /// <summary>Distinct table users serialize native pool allocation and release without claiming table mutation safety.</summary>
    [GpuFact]
    public unsafe void PoolAllocationAcrossIndependentUsersIsSynchronized()
    {
        using var device = VulkanDevice.Create();
        Silk.NET.Vulkan.DescriptorSetLayout layout = VulkanShaderBinding.CreateLayout(device);
        try
        {
            var leases = new VulkanDescriptorAllocator.Lease[96];
            Parallel.For(0, leases.Length, i => leases[i] = device.Descriptors.Allocate(layout, 1, 1));
            Assert.Equal(96, device.Descriptors.LiveSets);
            Assert.Equal(3, device.Descriptors.PoolCount);
            Parallel.ForEach(leases, lease => lease.Dispose());
            Assert.Equal(0, device.Descriptors.LiveSets);
            Assert.Equal(0, device.Descriptors.PoolCount);
        }
        finally
        {
            device.Api.DestroyDescriptorSetLayout(device.NativeDevice, layout, null);
        }
    }

    /// <summary>Outstanding allocator leases can be released after device destruction without native calls.</summary>
    [GpuFact]
    public void AllocatorLeaseCanOutliveDevice()
    {
        var device = VulkanDevice.Create();
        Silk.NET.Vulkan.DescriptorSetLayout layout = VulkanShaderBinding.CreateLayout(device);
        VulkanDescriptorAllocator.Lease lease = device.Descriptors.Allocate(layout, 1, 1);
        device.Dispose();
        lease.Dispose();
        lease.Dispose();
        device.Descriptors.Dispose();
        Assert.Equal(0, device.Descriptors.PoolCount);
    }

    private static int BindCount(IComputeEncoder encoder, string field) => (int)encoder.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(encoder)!;
}
