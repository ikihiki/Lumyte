using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    private delegate* unmanaged<CommandBuffer, NativePushDataInfo*, void> pushData;
    private delegate* unmanaged<CommandBuffer, NativeDispatchIndirectInfo*, void> dispatchIndirect;

    private void InitializeCompute()
    {
        pushData = (delegate* unmanaged<CommandBuffer, NativePushDataInfo*, void>)
            (nint)vk.GetDeviceProcAddr(device, "vkCmdPushDataEXT");
        dispatchIndirect = (delegate* unmanaged<CommandBuffer, NativeDispatchIndirectInfo*, void>)
            (nint)vk.GetDeviceProcAddr(device, "vkCmdDispatchIndirect2KHR");
        if (pushData == null || dispatchIndirect == null)
        {
            throw new NotSupportedException("Required Native Vulkan compute entry points are unavailable.");
        }
    }

    public NativeGpuComputePipelineHandle CreateComputePipeline(NativeGpuShaderProgram program)
    {
        VerifyNotDisposed();
        ArgumentNullException.ThrowIfNull(program);
        NativeGpuShaderCode shader = program.Compute
            ?? throw new ArgumentException("A compute pipeline requires a compute program.", nameof(program));
        ArgumentNullException.ThrowIfNull(shader.EntryPoint);
        if (shader.EntryPoint.Contains('\0'))
        {
            throw new ArgumentException("The entry point cannot contain an embedded null character.", nameof(program));
        }
        // A byte slice can be unaligned. Own aligned host storage only for the synchronous call.
        // This check prevents MemoryMarshal.Cast from silently discarding trailing source bytes.
        if ((shader.Code.Length & 3) != 0)
        {
            throw new ArgumentException("SPIR-V code must contain complete 32-bit words.", nameof(program));
        }
        uint[] words = MemoryMarshal.Cast<byte, uint>(shader.Code.Span).ToArray();
        fixed (uint* code = words)
        {
            ShaderModuleCreateInfo moduleInfo = new()
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = checked((nuint)shader.Code.Length), PCode = code,
            };
            Check(vk.CreateShaderModule(device, &moduleInfo, null, out ShaderModule module), "vkCreateShaderModule");
            try
            {
                using NativeNames names = new([shader.EntryPoint]);
                PipelineCreateFlags2CreateInfo flags = new()
                {
                    SType = StructureType.PipelineCreateFlags2CreateInfo,
                    Flags = (PipelineCreateFlags2)0x1000000000UL,
                };
                ComputePipelineCreateInfo info = new()
                {
                    SType = StructureType.ComputePipelineCreateInfo, PNext = &flags,
                    Stage = new()
                    {
                        SType = StructureType.PipelineShaderStageCreateInfo,
                        Stage = ShaderStageFlags.ComputeBit, Module = module, PName = names.Pointer[0],
                    },
                    Layout = default,
                };
                Pipeline pipeline = default;
                try
                {
                    Check(vk.CreateComputePipelines(device, default, 1, &info, null, &pipeline), "vkCreateComputePipelines");
                    return new ComputePipelineRecord(this, pipeline);
                }
                catch
                {
                    if (pipeline.Handle != 0) { vk.DestroyPipeline(device, pipeline, null); }
                    throw;
                }
            }
            finally { vk.DestroyShaderModule(device, module, null); }
        }
    }

    public void DestroyComputePipeline(NativeGpuComputePipelineHandle pipeline)
    {
        VerifyNotDisposed();
        ComputePipelineRecord record = RequireComputePipeline(pipeline);
        vk.DestroyPipeline(device, record.Pipeline, null);
        record.Destroyed = true;
    }

    private ComputePipelineRecord RequireComputePipeline(NativeGpuComputePipelineHandle pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not ComputePipelineRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("Compute pipeline belongs to another backend.", nameof(pipeline));
        }
        ObjectDisposedException.ThrowIf(record.Destroyed, pipeline);
        return record;
    }

    private sealed class ComputePipelineRecord(VulkanBackend owner, Pipeline pipeline) : NativeGpuComputePipelineHandle
    {
        public VulkanBackend Owner { get; } = owner;
        public Pipeline Pipeline { get; } = pipeline;
        public bool Destroyed;
    }
}
