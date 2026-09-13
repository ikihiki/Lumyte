using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    private delegate* unmanaged<CommandBuffer, NativeBindIndexBufferInfo*, void> bindIndexBuffer;
    private delegate* unmanaged<CommandBuffer, NativeDrawIndirectInfo*, void> drawIndirect;
    private delegate* unmanaged<CommandBuffer, NativeDrawIndirectInfo*, void> drawIndexedIndirect;

    private void InitializeRaster()
    {
        bindIndexBuffer = (delegate* unmanaged<CommandBuffer, NativeBindIndexBufferInfo*, void>)
            (nint)vk.GetDeviceProcAddr(device, "vkCmdBindIndexBuffer3KHR");
        drawIndirect = (delegate* unmanaged<CommandBuffer, NativeDrawIndirectInfo*, void>)
            (nint)vk.GetDeviceProcAddr(device, "vkCmdDrawIndirect2KHR");
        drawIndexedIndirect = (delegate* unmanaged<CommandBuffer, NativeDrawIndirectInfo*, void>)
            (nint)vk.GetDeviceProcAddr(device, "vkCmdDrawIndexedIndirect2KHR");
        if (bindIndexBuffer == null || drawIndirect == null || drawIndexedIndirect == null)
        {
            throw new NotSupportedException("Required Native Vulkan raster entry points are unavailable.");
        }
    }

    public NativeGpuRasterPipelineHandle CreateRasterPipeline(NativeGpuRasterPipelineDescription description, NativeGpuShaderProgram program)
    {
        VerifyNotDisposed();
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(program);
        bool mesh = program.Mesh is not null;
        if (mesh && !supportsMeshShaders) { throw new NotSupportedException("This Vulkan device does not support mesh shaders."); }
        if (program.Amplification is not null && !supportsAmplificationShaders)
        {
            throw new NotSupportedException("This Vulkan device does not support amplification shaders.");
        }
        NativeGpuShaderCode first = program.Mesh ?? program.Vertex
            ?? throw new ArgumentException("A raster pipeline requires a vertex or mesh program.", nameof(program));
        if (mesh ? description.Topology.HasValue || !description.MeshOutputTopology.HasValue
            : !description.Topology.HasValue || description.MeshOutputTopology.HasValue)
        {
            throw new ArgumentException("A raster pipeline requires the topology for its vertex or mesh program only.", nameof(description));
        }
        // SPIR-V owns the actual output topology. The caller must match this metadata;
        // reject only unknown common output classes, without parsing the shader.
        if (mesh && description.MeshOutputTopology is not (NativeGpuMeshOutputTopology.Line or NativeGpuMeshOutputTopology.Triangle))
        {
            throw new ArgumentOutOfRangeException(nameof(description), "Unsupported mesh output topology.");
        }
        ArgumentNullException.ThrowIfNull(description.ColorTargets);
        List<NativeGpuShaderCode> shaders = [first];
        if (program.Amplification is { } task) { shaders.Add(task); }
        if (program.Pixel is { } fragment) { shaders.Add(fragment); }
        ShaderModule[] modules = new ShaderModule[shaders.Count];
        try
        {
            for (int i = 0; i < shaders.Count; i++) { modules[i] = CreateRasterShaderModule(shaders[i]); }
            using NativeNames entries = new(shaders.Select(shader => shader.EntryPoint));
            PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[shaders.Count];
            for (int i = 0; i < shaders.Count; i++)
            {
                stages[i] = new()
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = shaders[i].Stage switch
                    {
                        GpuShaderStage.Vertex => ShaderStageFlags.VertexBit,
                        GpuShaderStage.Mesh => ShaderStageFlags.MeshBitExt,
                        GpuShaderStage.Amplification => ShaderStageFlags.TaskBitExt,
                        GpuShaderStage.Pixel => ShaderStageFlags.FragmentBit,
                        _ => throw new ArgumentException("Unsupported raster shader stage.", nameof(program)),
                    },
                    Module = modules[i], PName = entries.Pointer[i],
                };
            }
            Format[] colors = new Format[description.ColorTargets.Length];
            PipelineColorBlendAttachmentState[] blends = new PipelineColorBlendAttachmentState[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = TextureFormat(description.ColorTargets[i].Format);
                blends[i] = RasterColorTarget(description.ColorTargets[i]);
            }
            fixed (Format* colorFormats = colors)
            fixed (PipelineColorBlendAttachmentState* blendAttachments = blends)
            {
                PipelineRenderingCreateInfo rendering = new()
                {
                    SType = StructureType.PipelineRenderingCreateInfo,
                    ColorAttachmentCount = checked((uint)colors.Length), PColorAttachmentFormats = colorFormats,
                    DepthAttachmentFormat = description.DepthStencilFormat is { } depth ? TextureFormat(depth) : Format.Undefined,
                    StencilAttachmentFormat = description.DepthStencilFormat == GpuFormat.Depth24PlusStencil8 ? Format.D24UnormS8Uint : Format.Undefined,
                };
                PipelineCreateFlags2CreateInfo flags = new()
                {
                    SType = StructureType.PipelineCreateFlags2CreateInfo, PNext = &rendering,
                    Flags = (PipelineCreateFlags2)0x1000000000UL,
                };
                PipelineVertexInputStateCreateInfo vertexInput = new() { SType = StructureType.PipelineVertexInputStateCreateInfo };
                PipelineInputAssemblyStateCreateInfo input = new()
                {
                    SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                    Topology = mesh ? default : RasterTopology(description.Topology!.Value),
                };
                PipelineViewportStateCreateInfo viewport = new()
                {
                    SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1,
                };
                PipelineRasterizationStateCreateInfo raster = new()
                {
                    SType = StructureType.PipelineRasterizationStateCreateInfo,
                    PolygonMode = PolygonMode.Fill, CullMode = RasterCullMode(description.CullMode),
                    FrontFace = RasterFrontFace(description.FrontFace), LineWidth = 1,
                };
                PipelineMultisampleStateCreateInfo samples = new()
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    RasterizationSamples = (SampleCountFlags)description.SampleCount,
                };
                PipelineDepthStencilStateCreateInfo depthStencil = new() { SType = StructureType.PipelineDepthStencilStateCreateInfo };
                PipelineColorBlendStateCreateInfo blend = new()
                {
                    SType = StructureType.PipelineColorBlendStateCreateInfo,
                    AttachmentCount = checked((uint)blends.Length), PAttachments = blendAttachments,
                };
                DynamicState* dynamicStates = stackalloc DynamicState[]
                {
                    DynamicState.Viewport, DynamicState.Scissor, DynamicState.DepthTestEnable,
                    DynamicState.DepthWriteEnable, DynamicState.DepthCompareOp, DynamicState.StencilTestEnable,
                    DynamicState.StencilOp, DynamicState.StencilCompareMask, DynamicState.StencilWriteMask,
                    DynamicState.StencilReference,
                };
                PipelineDynamicStateCreateInfo dynamic = new()
                {
                    SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 10, PDynamicStates = dynamicStates,
                };
                GraphicsPipelineCreateInfo info = new()
                {
                    SType = StructureType.GraphicsPipelineCreateInfo, PNext = &flags,
                    StageCount = checked((uint)shaders.Count), PStages = stages,
                    PVertexInputState = mesh ? null : &vertexInput, PInputAssemblyState = mesh ? null : &input,
                    PViewportState = &viewport, PRasterizationState = &raster,
                    PMultisampleState = &samples, PDepthStencilState = &depthStencil,
                    PColorBlendState = &blend, PDynamicState = &dynamic, Layout = default,
                };
                Pipeline pipeline = default;
                try
                {
                    Check(vk.CreateGraphicsPipelines(device, default, 1, &info, null, &pipeline), "vkCreateGraphicsPipelines");
                    return new RasterPipelineRecord(this, pipeline);
                }
                catch
                {
                    if (pipeline.Handle != 0) { vk.DestroyPipeline(device, pipeline, null); }
                    throw;
                }
            }
        }
        finally
        {
            foreach (ShaderModule module in modules)
            {
                if (module.Handle != 0) { vk.DestroyShaderModule(device, module, null); }
            }
        }
    }

    private ShaderModule CreateRasterShaderModule(NativeGpuShaderCode shader)
    {
        ArgumentNullException.ThrowIfNull(shader.EntryPoint);
        if (shader.EntryPoint.Contains('\0')) { throw new ArgumentException("The entry point cannot contain an embedded null character.", nameof(shader)); }
        if ((shader.Code.Length & 3) != 0) { throw new ArgumentException("SPIR-V code must contain complete 32-bit words.", nameof(shader)); }
        uint[] words = MemoryMarshal.Cast<byte, uint>(shader.Code.Span).ToArray();
        fixed (uint* code = words)
        {
            ShaderModuleCreateInfo info = new()
            {
                SType = StructureType.ShaderModuleCreateInfo, CodeSize = checked((nuint)shader.Code.Length), PCode = code,
            };
            Check(vk.CreateShaderModule(device, &info, null, out ShaderModule module), "vkCreateShaderModule");
            return module;
        }
    }

    public void DestroyRasterPipeline(NativeGpuRasterPipelineHandle pipeline)
    {
        VerifyNotDisposed();
        RasterPipelineRecord record = RequireRasterPipeline(pipeline);
        vk.DestroyPipeline(device, record.Pipeline, null);
        record.Destroyed = true;
    }

    private RasterPipelineRecord RequireRasterPipeline(NativeGpuRasterPipelineHandle pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not RasterPipelineRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("Raster pipeline belongs to another backend.", nameof(pipeline));
        }
        ObjectDisposedException.ThrowIf(record.Destroyed, pipeline);
        return record;
    }

    private sealed class RasterPipelineRecord(VulkanBackend owner, Pipeline pipeline) : NativeGpuRasterPipelineHandle
    {
        public VulkanBackend Owner { get; } = owner;
        public Pipeline Pipeline { get; } = pipeline;
        public bool Destroyed;
    }
}
