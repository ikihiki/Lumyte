using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed unsafe partial class Direct3D12GraphicsPipeline
{
    private readonly Dictionary<string, nint> _variants = [];
    private nint _partial;

    internal nint Resolve(RenderStateSnapshot snapshot, TextureFormat[] formats, ShaderBindingSnapshot? arguments, TextureFormat? depthFormat, IndexFormat? indexFormat)
    {
        ValidateAlive();
        GraphicsRenderStateDesc state = snapshot.Desc;
        if (Desc.AlphaToCoverageEnable)
        {
            throw new NotSupportedException("Alpha-to-coverage requires multisampled attachments.");
        }

        if (formats.Length != state.ColorTargets.Count)
        {
            throw new ArgumentException("Color state count must match the pass.");
        }

        PrimitiveTopologyClass topology = state.Topology switch { PrimitiveTopology.PointList => PrimitiveTopologyClass.Point, PrimitiveTopology.LineList or PrimitiveTopology.LineStrip => PrimitiveTopologyClass.Line, _ => PrimitiveTopologyClass.Triangle };
        if (topology != Desc.TopologyClass)
        {
            throw new ArgumentException("Topology does not match the program compilation class.");
        }

        if (state.StripIndexFormat.HasValue && state.StripIndexFormat != indexFormat)
        {
            throw new ArgumentException("Strip restart format differs from the index buffer.");
        }

        if ((state.DepthStencil.DepthTestEnable || state.DepthStencil.DepthWriteEnable || state.DepthStencil.StencilTestEnable) && depthFormat == null)
        {
            throw new ArgumentException("Depth/stencil state requires a depth attachment.");
        }

        if (state.DepthStencil.StencilTestEnable && depthFormat != TextureFormat.Depth24Stencil8)
        {
            throw new ArgumentException("Stencil requires a stencil attachment.");
        }

        string key = snapshot.Key + ":" + depthFormat + ":" + string.Join(',', formats);
        if (_variants.TryGetValue(key, out nint cached))
        {
            return cached;
        }

        uint[] words = Encode(state, formats, depthFormat, topology);
        byte[] vs = _vertex.Native;
        byte[] ps = _fragment?.Native ?? [];
        fixed (byte* v = vs)
        {
            fixed (byte* p = ps)
            {
                fixed (uint* s = words)
                {
                    _owner.Check(NativeMethods.GraphicsCreate(_owner.Native, v, (ulong)vs.Length, p, (ulong)ps.Length, s, _partial, out nint pipeline));
                    _variants.Add(key, pipeline);
                    return pipeline;
                }
            }
        }
    }

    private static uint[] Encode(GraphicsRenderStateDesc state, TextureFormat[] formats, TextureFormat? depth, PrimitiveTopologyClass topology)
    {
        RasterizationStateDesc r = state.Rasterization;
        DepthStencilStateDesc d = state.DepthStencil;
        uint[] words = new uint[97];
        uint[] head = [(uint)topology + 1, state.StripIndexFormat is { } strip ? (strip == IndexFormat.Uint16 ? 1u : 2u) : 0,
            (uint)r.Cull + 1, r.FrontFace == FrontFace.CounterClockwise ? 1u : 0, r.DepthClipEnable ? 1u : 0,
            unchecked((uint)r.DepthBiasConstant), BitConverter.SingleToUInt32Bits(r.DepthBiasSlope), BitConverter.SingleToUInt32Bits(r.DepthBiasClamp),
            d.DepthTestEnable ? 1u : 0, d.DepthWriteEnable ? 1u : 0, (uint)d.DepthCompare + 1, d.StencilTestEnable ? 1u : 0, d.StencilReadMask, d.StencilWriteMask,
            (uint)d.Front.Fail + 1, (uint)d.Front.DepthFail + 1, (uint)d.Front.Pass + 1, (uint)d.Front.Compare + 1,
            (uint)d.Back.Fail + 1, (uint)d.Back.DepthFail + 1, (uint)d.Back.Pass + 1, (uint)d.Back.Compare + 1,
            state.SampleMask, (uint)formats.Length, depth.HasValue ? Direct3D12Texture.NativeFormat(depth.Value) : 0];
        head.CopyTo(words, 0);
        for (int i = 0; i < formats.Length; i++)
        {
            words[25 + i] = Direct3D12Texture.NativeFormat(formats[i]);
            ColorBlendStateDesc c = state.ColorTargets[i];
            uint[] color = [c.BlendEnable ? 1u : 0, Factor(c.Color.Source), Factor(c.Color.Destination), (uint)c.Color.Operation + 1,
                Factor(AlphaFactor(c.Alpha.Source)), Factor(AlphaFactor(c.Alpha.Destination)), (uint)c.Alpha.Operation + 1, (uint)c.WriteMask];
            color.CopyTo(words, 33 + (i * 8));
        }

        return words;
    }

    private static uint Factor(BlendFactor value) => value switch
    {
        BlendFactor.DestinationColor => 9,
        BlendFactor.OneMinusDestinationColor => 10,
        BlendFactor.DestinationAlpha => 7,
        BlendFactor.OneMinusDestinationAlpha => 8,
        BlendFactor.Constant => 14,
        BlendFactor.OneMinusConstant => 15,
        _ => (uint)value + 1,
    };

    private static BlendFactor AlphaFactor(BlendFactor value) => value switch
    {
        BlendFactor.SourceColor => BlendFactor.SourceAlpha,
        BlendFactor.OneMinusSourceColor => BlendFactor.OneMinusSourceAlpha,
        BlendFactor.DestinationColor => BlendFactor.DestinationAlpha,
        BlendFactor.OneMinusDestinationColor => BlendFactor.OneMinusDestinationAlpha,
        _ => value,
    };

    private void Initialize()
    {
        if (!_owner.PartialGraphicsProgramsEnabled)
        {
            return;
        }

        byte[] vs = _vertex.Native;
        byte[] ps = _fragment?.Native ?? [];
        fixed (byte* v = vs)
        {
            fixed (byte* p = ps)
            {
                _owner.Check(NativeMethods.PartialCreate(_owner.Native, v, (ulong)vs.Length, VertexData.EntryPoint, p, (ulong)ps.Length, FragmentData?.EntryPoint ?? string.Empty, (uint)Desc.TopologyClass + 1, (uint)Desc.Optimization, out _partial));
            }
        }
    }

    private void DisposeNative()
    {
        foreach (nint p in _variants.Values)
        {
            NativeMethods.PipelineDestroy(p);
        }

        _variants.Clear();
        NativeMethods.PartialDestroy(_partial);
    }
}
