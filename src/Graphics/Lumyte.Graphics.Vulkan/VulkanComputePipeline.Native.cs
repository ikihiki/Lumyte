using System.Text;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe partial class VulkanComputePipeline
{
    private readonly Dictionary<string, (PipelineLayout Pipeline, DescriptorSetLayout Group)> _argumentLayouts = [];
    private readonly Dictionary<string, Pipeline> _variants = [];
    private Pipeline _native;
    private PipelineLayout _layout;
    private DescriptorSetLayout _argumentLayout;

    internal PipelineLayout ArgumentPipelineLayout => _layout;

    internal DescriptorSetLayout ArgumentLayout => _argumentLayout;

    internal Pipeline Native
    {
        get
        {
            ValidateAlive();
            return _native;
        }
    }

    internal Pipeline Resolve(ShaderBindingSnapshot? arguments)
    {
        ValidateAlive();
        if (arguments == null)
        {
            SelectLayout(null);
            return Native;
        }

        string key = SelectLayout(arguments);
        if (_variants.TryGetValue(key, out Pipeline cached))
        {
            return cached;
        }

        byte[] entry = Encoding.UTF8.GetBytes(Data.EntryPoint + "\0");
        fixed (byte* name = entry)
        {
            var info = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = new() { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.ComputeBit, Module = _compute.Native, PName = name },
                Layout = _layout,
                BasePipelineIndex = -1,
            };
            Result result = _owner.Api.CreateComputePipelines(_owner.NativeDevice, default, 1, &info, null, out Pipeline pipeline);
            if (result != Result.Success)
            {
                throw new InvalidOperationException($"Vulkan compute specialization failed: {result}.");
            }

            _variants.Add(key, pipeline);
            return pipeline;
        }
    }

    private string SelectLayout(ShaderBindingSnapshot? arguments)
    {
        uint textures = arguments == null ? 1 : arguments.References.Where(r => r.Resource is VulkanTextureView).Select(r => checked(r.Slot + 1)).DefaultIfEmpty(1u).Max();
        uint samplers = arguments == null ? 1 : arguments.References.Where(r => r.Resource is VulkanSampler).Select(r => checked(r.Slot + 1)).DefaultIfEmpty(1u).Max();
        if (textures > _owner.Caps.MaxSampledTexturesPerStage || samplers > _owner.Caps.MaxSamplersPerStage)
        {
            throw new NotSupportedException("Native descriptor slots exceed the device's per-stage limits.");
        }

        string key = $"{Math.Max(1, textures)}:{Math.Max(1, samplers)}";
        if (_argumentLayouts.TryGetValue(key, out (PipelineLayout Pipeline, DescriptorSetLayout Group) existing))
        {
            (_layout, _argumentLayout) = existing;
            return key;
        }

        CreateLayout(textures, samplers);
        _argumentLayouts.Add(key, (_layout, _argumentLayout));
        return key;
    }

    private void CreateLayout(uint textures = 1, uint samplers = 1)
    {
        _argumentLayout = VulkanShaderBinding.CreateLayout(_owner, textures, samplers);
        DescriptorSetLayout group = _argumentLayout;
        var info = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &group };
        Result result = _owner.Api.CreatePipelineLayout(_owner.NativeDevice, &info, null, out _layout);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan pipeline layout creation failed: {result}.");
        }
    }

    private void Initialize()
    {
        SelectLayout(null);
        try
        {
            byte[] entry = Encoding.UTF8.GetBytes(Data.EntryPoint + "\0");
            fixed (byte* name = entry)
            {
                var info = new ComputePipelineCreateInfo
                {
                    SType = StructureType.ComputePipelineCreateInfo,
                    Stage = new() { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.ComputeBit, Module = _compute.Native, PName = name },
                    Layout = _layout,
                    BasePipelineIndex = -1,
                };
                Result result = _owner.Api.CreateComputePipelines(_owner.NativeDevice, default, 1, &info, null, out _native);
                if (result != Result.Success)
                {
                    if (_native.Handle != 0)
                    {
                        _owner.Api.DestroyPipeline(_owner.NativeDevice, _native, null);
                    }

                    throw new InvalidOperationException($"Vulkan compute pipeline creation failed: {result}.");
                }
            }
        }
        catch
        {
            _owner.Api.DestroyPipelineLayout(_owner.NativeDevice, _layout, null);
            _owner.Api.DestroyDescriptorSetLayout(_owner.NativeDevice, _argumentLayout, null);
            throw;
        }
    }

    private void DisposeNative()
    {
        _owner.Api.DestroyPipeline(_owner.NativeDevice, _native, null);
        foreach (Pipeline pipeline in _variants.Values)
        {
            _owner.Api.DestroyPipeline(_owner.NativeDevice, pipeline, null);
        }

        _variants.Clear();
        foreach ((PipelineLayout pipeline, DescriptorSetLayout group) in _argumentLayouts.Values)
        {
            _owner.Api.DestroyPipelineLayout(_owner.NativeDevice, pipeline, null);
            _owner.Api.DestroyDescriptorSetLayout(_owner.NativeDevice, group, null);
        }

        _argumentLayouts.Clear();
    }
}
