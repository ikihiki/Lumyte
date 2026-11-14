using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanShaderBinding : IDisposable
{
    private readonly VulkanDevice _owner;
    private DescriptorPool _pool;

    internal VulkanShaderBinding(VulkanDevice owner, ShaderBindingDependencies dependencies, ShaderTargetData target, DescriptorSetLayout layout)
    {
        _owner = owner;
        try
        {
            IShaderReference[] textures = dependencies.References.Where(r => r.Resource is VulkanTextureView).GroupBy(r => r.Slot).Select(g => g.First()).ToArray();
            IShaderReference[] samplers = dependencies.References.Where(r => r.Resource is VulkanSampler).GroupBy(r => r.Slot).Select(g => g.First()).ToArray();
            uint textureCount = textures.Length == 0 ? 1 : checked(textures.Max(r => r.Slot) + 1);
            uint samplerCount = samplers.Length == 0 ? 1 : checked(samplers.Max(r => r.Slot) + 1);
            if (textureCount > owner.Caps.MaxSampledTexturesPerStage || samplerCount > owner.Caps.MaxSamplersPerStage)
            {
                throw new NotSupportedException("Native descriptor slots exceed the device's per-stage limits.");
            }

            dependencies.Arguments.Seal();

            DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[2];
            sizes[0] = new() { Type = DescriptorType.SampledImage, DescriptorCount = checked((uint)textureCount) };
            sizes[1] = new() { Type = DescriptorType.Sampler, DescriptorCount = checked((uint)samplerCount) };
            var pool = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = 1, PoolSizeCount = 2, PPoolSizes = sizes };
            Check(owner.Api.CreateDescriptorPool(owner.NativeDevice, &pool, null, out _pool));
            var allocate = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = _pool, DescriptorSetCount = 1, PSetLayouts = &layout };
            Check(owner.Api.AllocateDescriptorSets(owner.NativeDevice, &allocate, out DescriptorSet set));
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
        if (_pool.Handle != 0)
        {
            _owner.Api.DestroyDescriptorPool(_owner.NativeDevice, _pool, null);
            _pool = default;
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

    private void Check(Result result)
    {
        if (result == Result.ErrorDeviceLost)
        {
            _owner.ReportLoss("Vulkan operation failed: device lost.");
        }

        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan shader binding operation failed: {result}.");
        }
    }
}
