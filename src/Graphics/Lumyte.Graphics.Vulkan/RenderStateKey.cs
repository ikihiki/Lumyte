using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

// All lanes are defined uint values, never raw struct bytes/padding. Default initialization
// canonicalizes unused targets. Packing guards deliberately fail on future enum expansion.
internal readonly struct RenderStateKey : IEquatable<RenderStateKey>
{
    internal const int MaxColorTargets = 8;
    private static readonly int _zeroHash = Hash(default);
    private readonly Words _words;
    private readonly int _hash;

    internal RenderStateKey(GraphicsRenderStateDesc state)
    {
        this = default;
        if (state.ColorTargets.Count > MaxColorTargets)
        {
            throw new NotSupportedException("Vulkan supports at most eight color targets.");
        }

        RasterizationStateDesc r = state.Rasterization;
        DepthStencilStateDesc d = state.DepthStencil;
        _words[0] = Pack((uint)state.Topology, 3) | (state.StripIndexFormat is { } index ? (Pack((uint)index, 1) + 1) << 3 : 0)
            | (Pack((uint)r.Cull, 2) << 5) | (Pack((uint)r.FrontFace, 1) << 7)
            | (r.DepthClipEnable ? 1u << 8 : 0) | (d.DepthTestEnable ? 1u << 9 : 0)
            | (d.DepthWriteEnable ? 1u << 10 : 0) | (d.StencilTestEnable ? 1u << 11 : 0)
            | (Pack((uint)d.DepthCompare, 3) << 12) | ((uint)state.ColorTargets.Count << 15);
        _words[1] = unchecked((uint)r.DepthBiasConstant);
        _words[2] = BitConverter.SingleToUInt32Bits(r.DepthBiasSlope);
        _words[3] = BitConverter.SingleToUInt32Bits(r.DepthBiasClamp);
        _words[4] = state.SampleMask;
        _words[5] = Face(d.Front) | (Face(d.Back) << 12);
        _words[6] = Pack(d.StencilReadMask, 8) | (Pack(d.StencilWriteMask, 8) << 8);
        for (int i = 0; i < state.ColorTargets.Count; i++)
        {
            ColorBlendStateDesc c = state.ColorTargets[i];
            _words[7 + i] = Component(c.Color) | (Component(c.Alpha) << 11)
                | (Pack((uint)c.WriteMask, 4) << 22) | (c.BlendEnable ? 1u << 26 : 0);
        }

        _hash = Hash(in _words) ^ _zeroHash;
    }

    public bool Equals(RenderStateKey other)
    {
        for (int i = 0; i < 15; i++)
        {
            if (_words[i] != other._words[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is RenderStateKey other && Equals(other);

    public override int GetHashCode() => _hash;

    internal static uint Pack(uint value, int bits) => value < (1u << bits) ? value : throw new ArgumentOutOfRangeException(nameof(value), "Pipeline key field exceeds its encoding.");

    private static int Hash(in Words words)
    {
        HashCode hash = default;
        for (int i = 0; i < 15; i++)
        {
            hash.Add(words[i]);
        }

        return hash.ToHashCode();
    }

    private static uint Face(StencilFaceDesc f) => Pack((uint)f.Compare, 3) | (Pack((uint)f.Fail, 3) << 3) | (Pack((uint)f.DepthFail, 3) << 6) | (Pack((uint)f.Pass, 3) << 9);

    private static uint Component(BlendComponentDesc c) => Pack((uint)c.Source, 4) | (Pack((uint)c.Destination, 4) << 4) | (Pack((uint)c.Operation, 3) << 8);

    [InlineArray(15)]
    private struct Words
    {
        private uint _element0;
    }
}
