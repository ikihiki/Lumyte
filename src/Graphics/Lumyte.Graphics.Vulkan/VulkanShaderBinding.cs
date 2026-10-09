using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanShaderBinding : IDisposable
{
    private readonly VulkanDevice _owner;
    private readonly List<VulkanShaderBacking> _backings = [];
    private DescriptorPool _pool;

    internal VulkanShaderBinding(VulkanDevice owner, ShaderBindingSnapshot snapshot, ShaderTargetData target, DescriptorSetLayout layout)
    {
        _owner = owner;
        snapshot.ValidateLayouts(target);
        try
        {
            IShaderReference[] textures = snapshot.References.Where(r => r.Resource is VulkanTextureView).GroupBy(r => r.Slot).Select(g => g.First()).ToArray();
            IShaderReference[] samplers = snapshot.References.Where(r => r.Resource is VulkanSampler).GroupBy(r => r.Slot).Select(g => g.First()).ToArray();
            uint textureCount = textures.Length == 0 ? 1 : checked(textures.Max(r => r.Slot) + 1);
            uint samplerCount = samplers.Length == 0 ? 1 : checked(samplers.Max(r => r.Slot) + 1);
            if (textureCount > owner.Caps.MaxSampledTexturesPerStage || samplerCount > owner.Caps.MaxSamplersPerStage)
            {
                throw new NotSupportedException("Native descriptor slots exceed the device's per-stage limits.");
            }

            byte[] PackReference(ShaderValue value, string kind) => VulkanShaderReferenceEncoding.Pack(owner, value, kind);

            byte[] root = ShaderDataLayout.Root(target, snapshot.Root.RootParameter).Pack(snapshot.Root, PackReference);
            var uniform = new VulkanShaderBacking(owner, root.Length);
            _backings.Add(uniform);
            uniform.Set(root);
            DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[3];
            sizes[0] = new() { Type = DescriptorType.SampledImage, DescriptorCount = checked((uint)textureCount) };
            sizes[1] = new() { Type = DescriptorType.Sampler, DescriptorCount = checked((uint)samplerCount) };
            sizes[2] = new() { Type = DescriptorType.UniformBuffer, DescriptorCount = 1 };
            var pool = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = 1, PoolSizeCount = 3, PPoolSizes = sizes };
            Check(owner.Api.CreateDescriptorPool(owner.NativeDevice, &pool, null, out _pool));
            var allocate = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = _pool, DescriptorSetCount = 1, PSetLayouts = &layout };
            Check(owner.Api.AllocateDescriptorSets(owner.NativeDevice, &allocate, out DescriptorSet set));
            Native = set;
            var rootInfo = new DescriptorBufferInfo { Buffer = uniform.Native, Range = uniform.Size };
            var write = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = 8, DescriptorCount = 1, DescriptorType = DescriptorType.UniformBuffer, PBufferInfo = &rootInfo };
            owner.Api.UpdateDescriptorSets(owner.NativeDevice, 1, &write, 0, null);
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

        foreach (VulkanShaderBacking backing in _backings)
        {
            backing.Dispose();
        }

        _backings.Clear();
    }

    internal static DescriptorSetLayout CreateLayout(VulkanDevice owner, uint textures = 1, uint samplers = 1)
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[3];
        bindings[0] = new() { Binding = 0, DescriptorCount = Math.Max(1, textures), DescriptorType = DescriptorType.SampledImage, StageFlags = ShaderStageFlags.All };
        bindings[1] = new() { Binding = 1, DescriptorCount = Math.Max(1, samplers), DescriptorType = DescriptorType.Sampler, StageFlags = ShaderStageFlags.All };
        bindings[2] = new() { Binding = 8, DescriptorCount = 1, DescriptorType = DescriptorType.UniformBuffer, StageFlags = ShaderStageFlags.All };
        DescriptorBindingFlags* flags = stackalloc DescriptorBindingFlags[3] { DescriptorBindingFlags.PartiallyBoundBit, DescriptorBindingFlags.PartiallyBoundBit, 0 };
        var extension = new DescriptorSetLayoutBindingFlagsCreateInfo { SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo, BindingCount = 3, PBindingFlags = flags };
        var info = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, PNext = &extension, BindingCount = 3, PBindings = bindings };
        Check(owner.Api.CreateDescriptorSetLayout(owner.NativeDevice, &info, null, out DescriptorSetLayout layout));
        return layout;
    }

    private static void Check(Result result)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan shader binding operation failed: {result}.");
        }
    }
}
