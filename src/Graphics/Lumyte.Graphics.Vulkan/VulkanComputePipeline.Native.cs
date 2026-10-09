using System.Text;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe partial class VulkanComputePipeline
{
    private Pipeline _native;
    private PipelineLayout _layout;

    internal Pipeline Native
    {
        get
        {
            ValidateAlive();
            return _native;
        }
    }

    private void CreateLayout()
    {
        var info = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo };
        Result result = _owner.Api.CreatePipelineLayout(_owner.NativeDevice, &info, null, out _layout);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan pipeline layout creation failed: {result}.");
        }
    }

    private void Initialize()
    {
        CreateLayout();
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
            throw;
        }
    }

    private void DisposeNative()
    {
        _owner.Api.DestroyPipeline(_owner.NativeDevice, _native, null);
        _owner.Api.DestroyPipelineLayout(_owner.NativeDevice, _layout, null);
    }
}
