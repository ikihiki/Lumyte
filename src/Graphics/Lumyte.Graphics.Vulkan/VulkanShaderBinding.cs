using System.Buffers.Binary;
using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;
using V = Silk.NET.Vulkan;

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
            object[] textures = snapshot.References.Select(r => r.Resource).OfType<VulkanTextureView>().Distinct().Cast<object>().ToArray();
            object[] samplers = snapshot.References.Select(r => r.Resource).OfType<VulkanSampler>().Distinct().Cast<object>().ToArray();
            if (textures.Length > owner.Caps.MaxSampledTexturesPerStage || samplers.Length > owner.Caps.MaxSamplersPerStage)
            {
                throw new NotSupportedException("Native descriptor arrays exceed the device's per-stage limits.");
            }

            var data = new Dictionary<IShaderDataSource, VulkanShaderBacking>();
            foreach ((IShaderDataSource source, _) in snapshot.Elements.Keys)
            {
                if (!data.ContainsKey(source))
                {
                    var backing = new VulkanShaderBacking(owner, checked((int)source.SizeInBytes));
                    _backings.Add(backing);
                    data.Add(source, backing);
                }
            }

            byte[] PackReference(ShaderValue value, string kind)
            {
                if (value.Reference is not IShaderReference reference)
                {
                    throw new ArgumentException("Missing resource reference.");
                }

                byte[] wire = new byte[16];
                object resource = reference.Resource;
                if (kind == "GpuTextureRef" && resource is VulkanTextureView)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(wire, checked((uint)Array.IndexOf(textures, resource)));
                }
                else if (kind == "GpuSamplerRef" && resource is VulkanSampler)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(wire, checked((uint)Array.IndexOf(samplers, resource)));
                }
                else if (kind is "GpuBufferRef" or "GpuRWBufferRef")
                {
                    ulong address;
                    if (resource is IShaderDataSource source && kind == "GpuBufferRef")
                    {
                        address = data[source].Address;
                    }
                    else if (resource is IShaderRawBuffer raw && (raw.Usage & (kind == "GpuBufferRef" ? BufferUsage.ShaderRead : BufferUsage.ShaderWrite)) != 0)
                    {
                        var info = new BufferDeviceAddressInfo { SType = StructureType.BufferDeviceAddressInfo, Buffer = (V.Buffer)raw.ShaderHandle };
                        address = owner.Api.GetBufferDeviceAddress(owner.NativeDevice, &info);
                    }
                    else
                    {
                        throw new ArgumentException("Buffer reference access does not match the shader ABI.");
                    }

                    BinaryPrimitives.WriteUInt64LittleEndian(wire, checked(address + reference.OffsetInBytes));
                }
                else
                {
                    throw new ArgumentException("Reference type does not match the shader ABI.");
                }

                BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(8), checked((uint)reference.Count));
                return wire;
            }

            foreach ((IShaderDataSource source, VulkanShaderBacking backing) in data)
            {
                byte[] bytes = new byte[checked((int)source.SizeInBytes)];
                foreach (((IShaderDataSource buffer, ulong element), ShaderValueSnapshot values) in snapshot.Elements)
                {
                    if (ReferenceEquals(buffer, source))
                    {
                        source.Layout.Pack(values, PackReference).CopyTo(bytes, checked((int)(element * (ulong)source.Layout.Size)));
                    }
                }

                backing.Set(bytes);
            }

            byte[] root = ShaderDataLayout.Root(target, snapshot.Root.RootParameter).Pack(snapshot.Root, PackReference);
            var uniform = new VulkanShaderBacking(owner, root.Length);
            _backings.Add(uniform);
            uniform.Set(root);
            DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[3];
            sizes[0] = new() { Type = DescriptorType.SampledImage, DescriptorCount = checked((uint)Math.Max(1, textures.Length)) };
            sizes[1] = new() { Type = DescriptorType.Sampler, DescriptorCount = checked((uint)Math.Max(1, samplers.Length)) };
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
                var info = new DescriptorImageInfo { ImageView = ((VulkanTextureView)textures[i]).Native, ImageLayout = ImageLayout.ShaderReadOnlyOptimal };
                write = new() { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = 0, DstArrayElement = (uint)i, DescriptorCount = 1, DescriptorType = DescriptorType.SampledImage, PImageInfo = &info };
                owner.Api.UpdateDescriptorSets(owner.NativeDevice, 1, &write, 0, null);
            }

            for (int i = 0; i < samplers.Length; i++)
            {
                var info = new DescriptorImageInfo { Sampler = ((VulkanSampler)samplers[i]).Native };
                write = new() { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = 1, DstArrayElement = (uint)i, DescriptorCount = 1, DescriptorType = DescriptorType.Sampler, PImageInfo = &info };
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
