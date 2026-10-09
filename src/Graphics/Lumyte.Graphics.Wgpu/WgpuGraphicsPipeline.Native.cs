using System.Text;
using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe partial class WgpuGraphicsPipeline
{
    private readonly Dictionary<string, nint> _variants = [];

    internal WGPURenderPipelineImpl* Resolve(RenderStateSnapshot state, TextureFormat[] formats)
    {
        ValidateAlive();
        PipelineValidation.Draw(Desc, state, formats, FragmentOutputs);
        string key = state.Key + ":" + string.Join(',', formats);
        if (_variants.TryGetValue(key, out nint cached))
        {
            return (WGPURenderPipelineImpl*)cached;
        }

        WGPURenderPipelineImpl* pipeline = CreateNative(state.Desc, formats);
        _variants.Add(key, (nint)pipeline);
        return pipeline;
    }

    private static WGPUBlendFactor Factor(BlendFactor value) => value switch
    {
        BlendFactor.Zero => WGPUBlendFactor.Zero,
        BlendFactor.One => WGPUBlendFactor.One,
        BlendFactor.SourceColor => WGPUBlendFactor.Src,
        BlendFactor.OneMinusSourceColor => WGPUBlendFactor.OneMinusSrc,
        BlendFactor.SourceAlpha => WGPUBlendFactor.SrcAlpha,
        BlendFactor.OneMinusSourceAlpha => WGPUBlendFactor.OneMinusSrcAlpha,
        BlendFactor.DestinationColor => WGPUBlendFactor.Dst,
        BlendFactor.OneMinusDestinationColor => WGPUBlendFactor.OneMinusDst,
        BlendFactor.DestinationAlpha => WGPUBlendFactor.DstAlpha,
        BlendFactor.OneMinusDestinationAlpha => WGPUBlendFactor.OneMinusDstAlpha,
        BlendFactor.SourceAlphaSaturated => WGPUBlendFactor.SrcAlphaSaturated,
        BlendFactor.Constant => WGPUBlendFactor.Constant,
        _ => WGPUBlendFactor.OneMinusConstant,
    };

    private static WGPUBlendComponent Blend(BlendComponentDesc value) => new()
    {
        srcFactor = Factor(value.Source),
        dstFactor = Factor(value.Destination),
        operation = value.Operation switch
        {
            BlendOperation.Add => WGPUBlendOperation.Add,
            BlendOperation.Subtract => WGPUBlendOperation.Subtract,
            BlendOperation.ReverseSubtract => WGPUBlendOperation.ReverseSubtract,
            BlendOperation.Min => WGPUBlendOperation.Min,
            _ => WGPUBlendOperation.Max,
        },
    };

    private static WGPUTextureFormat Format(TextureFormat value) => value switch
    {
        TextureFormat.Rgba8Unorm => WGPUTextureFormat.RGBA8Unorm,
        TextureFormat.Rgba8Srgb => WGPUTextureFormat.RGBA8UnormSrgb,
        TextureFormat.Bgra8Unorm => WGPUTextureFormat.BGRA8Unorm,
        _ => WGPUTextureFormat.BGRA8UnormSrgb,
    };

    private void Initialize()
    {
        // Native graphics variants require actual attachment and draw state.
    }

    private WGPURenderPipelineImpl* CreateNative(GraphicsRenderStateDesc state, TextureFormat[] formats)
    {
        byte[] vertexEntry = Encoding.UTF8.GetBytes(VertexData.EntryPoint);
        byte[] fragmentEntry = Encoding.UTF8.GetBytes(FragmentData!.EntryPoint);
        Span<WGPUColorTargetState> colors = stackalloc WGPUColorTargetState[formats.Length];
        Span<WGPUBlendState> blends = stackalloc WGPUBlendState[formats.Length];
        fixed (byte* vs = vertexEntry)
        {
            fixed (byte* fs = fragmentEntry)
            {
                fixed (WGPUColorTargetState* targets = colors)
                {
                    fixed (WGPUBlendState* equations = blends)
                    {
                        for (int i = 0; i < formats.Length; i++)
                        {
                            ColorBlendStateDesc c = state.ColorTargets[i];
                            equations[i] = new() { color = Blend(c.Color), alpha = Blend(c.Alpha) };
                            targets[i] = new() { format = Format(formats[i]), writeMask = (ulong)c.WriteMask, blend = c.BlendEnable ? equations + i : null };
                        }

                        var fragment = new WGPUFragmentState
                        {
                            module = _fragment!.Native.Handle,
                            entryPoint = new() { data = (sbyte*)fs, length = (nuint)fragmentEntry.Length },
                            targetCount = (nuint)formats.Length,
                            targets = targets,
                        };
                        var desc = new WGPURenderPipelineDescriptor
                        {
                            vertex = new() { module = _vertex.Native.Handle, entryPoint = new() { data = (sbyte*)vs, length = (nuint)vertexEntry.Length } },
                            fragment = &fragment,
                            primitive = new()
                            {
                                topology = state.Topology switch
                                {
                                    PrimitiveTopology.PointList => WGPUPrimitiveTopology.PointList,
                                    PrimitiveTopology.LineList => WGPUPrimitiveTopology.LineList,
                                    PrimitiveTopology.LineStrip => WGPUPrimitiveTopology.LineStrip,
                                    PrimitiveTopology.TriangleStrip => WGPUPrimitiveTopology.TriangleStrip,
                                    _ => WGPUPrimitiveTopology.TriangleList,
                                },
                                frontFace = state.Rasterization.FrontFace == FrontFace.Clockwise ? WGPUFrontFace.CW : WGPUFrontFace.CCW,
                                cullMode = state.Rasterization.Cull switch { CullMode.Front => WGPUCullMode.Front, CullMode.Back => WGPUCullMode.Back, _ => WGPUCullMode.None },
                            },
                            multisample = new() { count = 1, mask = state.SampleMask },
                        };
                        WGPURenderPipelineImpl* pipeline = WGPU.wgpuDeviceCreateRenderPipeline(_owner.NativeDevice.Handle, &desc);
                        if (pipeline == null)
                        {
                            throw new InvalidOperationException("WebGPU graphics pipeline creation failed.");
                        }

                        return pipeline;
                    }
                }
            }
        }
    }

    private void DisposeNative()
    {
        foreach (nint native in _variants.Values)
        {
            WGPU.wgpuRenderPipelineRelease((WGPURenderPipelineImpl*)native);
        }

        _variants.Clear();
    }
}
