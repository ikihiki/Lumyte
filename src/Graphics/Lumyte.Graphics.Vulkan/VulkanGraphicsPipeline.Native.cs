using System.Text;
using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;
using V = Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe partial class VulkanGraphicsPipeline
{
    private readonly Dictionary<string, Pipeline> _variants = [];
    private readonly List<Pipeline> _uncachedPipelines = [];
    private readonly Dictionary<string, (PipelineLayout Pipeline, DescriptorSetLayout Group)> _argumentLayouts = [];
    private PipelineLayout _layout;
    private DescriptorSetLayout _argumentLayout;

    internal PipelineLayout ArgumentPipelineLayout => _layout;

    internal DescriptorSetLayout ArgumentLayout => _argumentLayout;

    internal Pipeline Resolve(RenderStateSnapshot state, TextureFormat[] formats, ShaderBindingSnapshot? arguments = null, TextureFormat? depthFormat = null, IndexFormat? indexFormat = null)
    {
        ValidateAlive();
        string argumentKey = SelectLayout(arguments);
        if (!_owner.CacheGraphicsPipelines)
        {
            Pipeline fresh = CreateNative(state.Desc, formats, depthFormat);
            _uncachedPipelines.Add(fresh);
            return fresh;
        }

        string key = state.Key + ":" + depthFormat + ":" + string.Join(',', formats) + ":" + argumentKey;
        if (_variants.TryGetValue(key, out Pipeline cached))
        {
            return cached;
        }

        Pipeline pipeline = CreateNative(state.Desc, formats, depthFormat);
        _variants.Add(key, pipeline);
        return pipeline;
    }

    private static V.BlendFactor Factor(Abstractions.BlendFactor value) => value switch
    {
        Abstractions.BlendFactor.Zero => V.BlendFactor.Zero,
        Abstractions.BlendFactor.One => V.BlendFactor.One,
        Abstractions.BlendFactor.SourceColor => V.BlendFactor.SrcColor,
        Abstractions.BlendFactor.OneMinusSourceColor => V.BlendFactor.OneMinusSrcColor,
        Abstractions.BlendFactor.SourceAlpha => V.BlendFactor.SrcAlpha,
        Abstractions.BlendFactor.OneMinusSourceAlpha => V.BlendFactor.OneMinusSrcAlpha,
        Abstractions.BlendFactor.DestinationColor => V.BlendFactor.DstColor,
        Abstractions.BlendFactor.OneMinusDestinationColor => V.BlendFactor.OneMinusDstColor,
        Abstractions.BlendFactor.DestinationAlpha => V.BlendFactor.DstAlpha,
        Abstractions.BlendFactor.OneMinusDestinationAlpha => V.BlendFactor.OneMinusDstAlpha,
        Abstractions.BlendFactor.SourceAlphaSaturated => V.BlendFactor.SrcAlphaSaturate,
        Abstractions.BlendFactor.Constant => V.BlendFactor.ConstantColor,
        _ => V.BlendFactor.OneMinusConstantColor,
    };

    private static BlendOp Operation(BlendOperation value) => value switch
    {
        BlendOperation.Add => BlendOp.Add,
        BlendOperation.Subtract => BlendOp.Subtract,
        BlendOperation.ReverseSubtract => BlendOp.ReverseSubtract,
        BlendOperation.Min => BlendOp.Min,
        _ => BlendOp.Max,
    };

    private static Format Format(TextureFormat format) => format switch
    {
        TextureFormat.Rgba8Unorm => V.Format.R8G8B8A8Unorm,
        TextureFormat.Rgba8Srgb => V.Format.R8G8B8A8Srgb,
        TextureFormat.Bgra8Unorm => V.Format.B8G8R8A8Unorm,
        TextureFormat.Bgra8Srgb => V.Format.B8G8R8A8Srgb,
        TextureFormat.R8Unorm => V.Format.R8Unorm,
        TextureFormat.Rg8Unorm => V.Format.R8G8Unorm,
        TextureFormat.R16Float => V.Format.R16Sfloat,
        TextureFormat.Rg16Float => V.Format.R16G16Sfloat,
        TextureFormat.Rgba16Float => V.Format.R16G16B16A16Sfloat,
        TextureFormat.Rgb10A2Unorm => V.Format.A2B10G10R10UnormPack32,
        _ => throw new NotSupportedException("Unsupported color attachment format."),
    };

    private static CompareOp Compare(CompareFunction value) => (CompareOp)(int)value;

    private static StencilOp Operation(StencilOperation value) => value switch
    {
        StencilOperation.Keep => StencilOp.Keep,
        StencilOperation.Zero => StencilOp.Zero,
        StencilOperation.Replace => StencilOp.Replace,
        StencilOperation.IncrementClamp => StencilOp.IncrementAndClamp,
        StencilOperation.DecrementClamp => StencilOp.DecrementAndClamp,
        StencilOperation.Invert => StencilOp.Invert,
        StencilOperation.IncrementWrap => StencilOp.IncrementAndWrap,
        _ => StencilOp.DecrementAndWrap,
    };

    private static StencilOpState Stencil(StencilFaceDesc face, DepthStencilStateDesc state) => new()
    {
        CompareOp = Compare(face.Compare),
        FailOp = Operation(face.Fail),
        DepthFailOp = Operation(face.DepthFail),
        PassOp = Operation(face.Pass),
        CompareMask = state.StencilReadMask,
        WriteMask = state.StencilWriteMask,
    };

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

    private void Initialize() => SelectLayout(null);

    private Pipeline CreateNative(GraphicsRenderStateDesc state, TextureFormat[] formats, TextureFormat? depthFormat)
    {
        byte[] vertexEntry = Encoding.UTF8.GetBytes(VertexData.EntryPoint + "\0");
        byte[] fragmentEntry = Encoding.UTF8.GetBytes((FragmentData?.EntryPoint ?? string.Empty) + "\0");
        Span<PipelineShaderStageCreateInfo> stages = stackalloc PipelineShaderStageCreateInfo[2];
        Span<PipelineColorBlendAttachmentState> colors = stackalloc PipelineColorBlendAttachmentState[formats.Length];
        Span<Format> nativeFormats = stackalloc Format[formats.Length];
        for (int i = 0; i < formats.Length; i++)
        {
            ColorBlendStateDesc c = state.ColorTargets[i];
            colors[i] = new()
            {
                BlendEnable = c.BlendEnable,
                SrcColorBlendFactor = Factor(c.Color.Source),
                DstColorBlendFactor = Factor(c.Color.Destination),
                ColorBlendOp = Operation(c.Color.Operation),
                SrcAlphaBlendFactor = c.Alpha.Source == Abstractions.BlendFactor.Constant ? V.BlendFactor.ConstantAlpha : c.Alpha.Source == Abstractions.BlendFactor.OneMinusConstant ? V.BlendFactor.OneMinusConstantAlpha : Factor(c.Alpha.Source),
                DstAlphaBlendFactor = c.Alpha.Destination == Abstractions.BlendFactor.Constant ? V.BlendFactor.ConstantAlpha : c.Alpha.Destination == Abstractions.BlendFactor.OneMinusConstant ? V.BlendFactor.OneMinusConstantAlpha : Factor(c.Alpha.Destination),
                AlphaBlendOp = Operation(c.Alpha.Operation),
                ColorWriteMask = (ColorComponentFlags)c.WriteMask,
            };
            nativeFormats[i] = Format(formats[i]);
        }

        DynamicState* dynamicStates = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor, DynamicState.BlendConstants, DynamicState.StencilReference };
        var dynamic = new PipelineDynamicStateCreateInfo { SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 4, PDynamicStates = dynamicStates };
        var vertexInput = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo };
        var assembly = new PipelineInputAssemblyStateCreateInfo
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = state.Topology switch
            {
                Abstractions.PrimitiveTopology.PointList => V.PrimitiveTopology.PointList,
                Abstractions.PrimitiveTopology.LineList => V.PrimitiveTopology.LineList,
                Abstractions.PrimitiveTopology.LineStrip => V.PrimitiveTopology.LineStrip,
                Abstractions.PrimitiveTopology.TriangleStrip => V.PrimitiveTopology.TriangleStrip,
                _ => V.PrimitiveTopology.TriangleList,
            },
            PrimitiveRestartEnable = state.StripIndexFormat != null,
        };
        var viewport = new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1 };
        var raster = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = PolygonMode.Fill,
            CullMode = state.Rasterization.Cull switch { CullMode.Front => CullModeFlags.FrontBit, CullMode.Back => CullModeFlags.BackBit, _ => CullModeFlags.None },
            FrontFace = state.Rasterization.FrontFace == Abstractions.FrontFace.CounterClockwise ? V.FrontFace.CounterClockwise : V.FrontFace.Clockwise,
            LineWidth = 1,
            DepthBiasEnable = state.Rasterization.DepthBiasConstant != 0 || state.Rasterization.DepthBiasSlope != 0,
            DepthBiasConstantFactor = state.Rasterization.DepthBiasConstant,
            DepthBiasSlopeFactor = state.Rasterization.DepthBiasSlope,
        };
        DepthStencilStateDesc d = state.DepthStencil;
        var depthState = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = d.DepthTestEnable || d.DepthWriteEnable,
            DepthWriteEnable = d.DepthWriteEnable,
            DepthCompareOp = d.DepthTestEnable ? Compare(d.DepthCompare) : CompareOp.Always,
            StencilTestEnable = d.StencilTestEnable,
            Front = Stencil(d.Front, d),
            Back = Stencil(d.Back, d),
        };
        uint mask = state.SampleMask;
        var samples = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = SampleCountFlags.Count1Bit, PSampleMask = &mask };
        fixed (byte* vs = vertexEntry)
        {
            fixed (byte* fs = fragmentEntry)
            {
                fixed (PipelineShaderStageCreateInfo* shaders = stages)
                {
                    fixed (PipelineColorBlendAttachmentState* targets = colors)
                    {
                        fixed (Format* targetFormats = nativeFormats)
                        {
                            shaders[0] = new() { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = _vertex.Native, PName = vs };
                            if (_fragment != null)
                            {
                                shaders[1] = new() { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = _fragment.Native, PName = fs };
                            }

                            var blend = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = (uint)formats.Length, PAttachments = targets };
                            var rendering = new PipelineRenderingCreateInfo { SType = StructureType.PipelineRenderingCreateInfo, ColorAttachmentCount = (uint)formats.Length, PColorAttachmentFormats = targetFormats, DepthAttachmentFormat = depthFormat is { } df ? VulkanTexture.NativeFormat(df) : V.Format.Undefined, StencilAttachmentFormat = depthFormat == TextureFormat.Depth24Stencil8 ? VulkanTexture.NativeFormat(depthFormat.Value) : V.Format.Undefined };
                            var info = new GraphicsPipelineCreateInfo
                            {
                                SType = StructureType.GraphicsPipelineCreateInfo,
                                PNext = &rendering,
                                StageCount = _fragment == null ? 1u : 2u,
                                PStages = shaders,
                                PVertexInputState = &vertexInput,
                                PInputAssemblyState = &assembly,
                                PViewportState = &viewport,
                                PRasterizationState = &raster,
                                PMultisampleState = &samples,
                                PColorBlendState = &blend,
                                PDepthStencilState = depthFormat == null ? null : &depthState,
                                PDynamicState = &dynamic,
                                Layout = _layout,
                                BasePipelineIndex = -1,
                            };
                            Result result = _owner.Api.CreateGraphicsPipelines(_owner.NativeDevice, default, 1, &info, null, out Pipeline pipeline);
                            if (result != Result.Success)
                            {
                                if (pipeline.Handle != 0)
                                {
                                    _owner.Api.DestroyPipeline(_owner.NativeDevice, pipeline, null);
                                }

                                throw new InvalidOperationException($"Vulkan graphics pipeline creation failed: {result}.");
                            }

                            return pipeline;
                        }
                    }
                }
            }
        }
    }

    private void DisposeNative()
    {
        foreach (Pipeline pipeline in _variants.Values)
        {
            _owner.Api.DestroyPipeline(_owner.NativeDevice, pipeline, null);
        }

        foreach (Pipeline pipeline in _uncachedPipelines)
        {
            _owner.Api.DestroyPipeline(_owner.NativeDevice, pipeline, null);
        }

        _uncachedPipelines.Clear();
        _variants.Clear();
        foreach ((PipelineLayout pipeline, DescriptorSetLayout group) in _argumentLayouts.Values)
        {
            _owner.Api.DestroyPipelineLayout(_owner.NativeDevice, pipeline, null);
            _owner.Api.DestroyDescriptorSetLayout(_owner.NativeDevice, group, null);
        }

        _argumentLayouts.Clear();
    }
}
