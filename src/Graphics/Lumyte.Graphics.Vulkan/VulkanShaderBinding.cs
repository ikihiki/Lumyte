using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanShaderBinding : IDisposable
{
    private readonly IShaderReference[] _registrations;
    private readonly VulkanArgumentTable _table;
    private VulkanDescriptorAllocator.Lease? _lease;
    private int _references = 1;

    internal VulkanShaderBinding(VulkanDevice owner, VulkanArgumentTable table, IShaderReference[] registrations, DescriptorSetLayout layout)
    {
        _registrations = registrations;
        _table = table;
        try
        {
            ValidateResources(registrations);
            IShaderReference[] textures = registrations.Where(r => r.Resource is VulkanTextureView).GroupBy(r => r.Slot).Select(g => g.First()).ToArray();
            IShaderReference[] samplers = registrations.Where(r => r.Resource is VulkanSampler).GroupBy(r => r.Slot).Select(g => g.First()).ToArray();
            uint textureCount = textures.Length == 0 ? 1 : checked(textures.Max(r => r.Slot) + 1);
            uint samplerCount = samplers.Length == 0 ? 1 : checked(samplers.Max(r => r.Slot) + 1);
            if (textureCount > owner.Caps.MaxSampledTexturesPerStage || samplerCount > owner.Caps.MaxSamplersPerStage)
            {
                throw new NotSupportedException("Native descriptor slots exceed the device's per-stage limits.");
            }

            _lease = owner.Descriptors.Allocate(layout, textureCount, samplerCount);
            DescriptorSet set = _lease.Native;
            Native = set;
            WriteDescriptorSet write;
            for (int i = 0; i < textures.Length; i++)
            {
                var info = new DescriptorImageInfo { ImageView = ((VulkanTextureView)textures[i].Resource).Native, ImageLayout = ImageLayout.ShaderReadOnlyOptimal };
                write = new() { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = 0, DstArrayElement = textures[i].Slot, DescriptorCount = 1, DescriptorType = DescriptorType.SampledImage, PImageInfo = &info };
                owner.Api.UpdateDescriptorSets(owner.NativeDevice, 1, &write, 0, null);
            }

            for (int i = 0; i < samplers.Length; i++)
            {
                var info = new DescriptorImageInfo { Sampler = ((VulkanSampler)samplers[i].Resource).Native };
                write = new() { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = 1, DstArrayElement = samplers[i].Slot, DescriptorCount = 1, DescriptorType = DescriptorType.Sampler, PImageInfo = &info };
                owner.Api.UpdateDescriptorSets(owner.NativeDevice, 1, &write, 0, null);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal DescriptorSet Native { get; }

    public void Dispose()
    {
        if (Interlocked.Decrement(ref _references) == 0)
        {
            _lease?.Dispose();
            _lease = null;
        }

        GC.KeepAlive(_registrations);
        GC.KeepAlive(_table);
    }

    internal static void ValidateResources(IShaderReference[] registrations)
    {
        foreach (IShaderReference registration in registrations)
        {
            if (registration.Resource is VulkanTextureView view)
            {
                _ = view.Native;
                _ = ((VulkanTexture)view.Texture).Native;
            }
            else if (registration.Resource is VulkanSampler sampler)
            {
                _ = sampler.Native;
            }
        }
    }

    internal static DescriptorSetLayout CreateLayout(VulkanDevice owner, uint textures = 1, uint samplers = 1)
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[2];
        bindings[0] = new() { Binding = 0, DescriptorCount = Math.Max(1, textures), DescriptorType = DescriptorType.SampledImage, StageFlags = VulkanPushConstants.Stages };
        bindings[1] = new() { Binding = 1, DescriptorCount = Math.Max(1, samplers), DescriptorType = DescriptorType.Sampler, StageFlags = VulkanPushConstants.Stages };
        DescriptorBindingFlags* flags = stackalloc DescriptorBindingFlags[2] { DescriptorBindingFlags.PartiallyBoundBit, DescriptorBindingFlags.PartiallyBoundBit };
        var extension = new DescriptorSetLayoutBindingFlagsCreateInfo { SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo, BindingCount = 2, PBindingFlags = flags };
        var info = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, PNext = &extension, BindingCount = 2, PBindings = bindings };
        owner.CheckResult(owner.Api.CreateDescriptorSetLayout(owner.NativeDevice, &info, null, out DescriptorSetLayout layout), "CreateDescriptorSetLayout");
        return layout;
    }

    internal void Retain() => Interlocked.Increment(ref _references);
}
