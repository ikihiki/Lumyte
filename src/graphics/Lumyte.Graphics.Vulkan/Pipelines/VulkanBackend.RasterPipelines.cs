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
        if (program.Mesh is not null) { throw new NotSupportedException("Native Vulkan mesh pipelines are not implemented."); }
        NativeGpuShaderCode vertex = program.Vertex
            ?? throw new ArgumentException("A raster pipeline requires a vertex program.", nameof(program));
        if (!description.Topology.HasValue || description.MeshOutputTopology.HasValue)
        {
            throw new ArgumentException("A vertex pipeline requires only vertex topology.", nameof(description));
        }
        ArgumentNullException.ThrowIfNull(description.ColorTargets);
        ShaderModule vertexModule = CreateRasterShaderModule(vertex);
        ShaderModule pixelModule = default;
        try
        {
            if (program.Pixel is { } pixel) { pixelModule = CreateRasterShaderModule(pixel); }
            using NativeNames entries = new(program.Pixel is null ? [vertex.EntryPoint] : [vertex.EntryPoint, program.Pixel.EntryPoint]);
            PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new()
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit, Module = vertexModule, PName = entries.Pointer[0],
            };
            if (pixelModule.Handle != 0)
            {
                stages[1] = new()
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.FragmentBit, Module = pixelModule, PName = entries.Pointer[1],
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
                    Topology = RasterTopology(description.Topology.Value),
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
                    StageCount = pixelModule.Handle == 0 ? 1u : 2u, PStages = stages,
                    PVertexInputState = &vertexInput, PInputAssemblyState = &input,
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
            if (pixelModule.Handle != 0) { vk.DestroyShaderModule(device, pixelModule, null); }
            vk.DestroyShaderModule(device, vertexModule, null);
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
