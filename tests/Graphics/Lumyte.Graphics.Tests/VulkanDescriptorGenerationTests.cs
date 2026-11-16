using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Vulkan;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks compute parity and allocator lifetime independently of graphics encoders.</summary>
public sealed class VulkanDescriptorGenerationTests
{
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
        table.WriteSampler(0, sampler);
        table.ReleaseSampler(0);
        table.WriteSampler(0, sampler);
        Assert.Equal(1, native.DescriptorGeneration);
        Assert.Equal(1, device.Descriptors.AllocatedSets);
        encoder.Dispatch(1);
        Assert.Equal(2, device.Descriptors.AllocatedSets);
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
}
