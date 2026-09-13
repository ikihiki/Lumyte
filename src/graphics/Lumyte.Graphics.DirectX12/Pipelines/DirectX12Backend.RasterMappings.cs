using Lumyte.Graphics.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    internal readonly record struct RasterStencilFaceKey(GpuCompareOp Compare,
        NativeGpuStencilOperation FailOp, NativeGpuStencilOperation DepthFailOp, NativeGpuStencilOperation PassOp)
    {
        public static RasterStencilFaceKey From(NativeGpuStencilFaceState face)
            => new(face.Compare, face.FailOp, face.DepthFailOp, face.PassOp);
        public static RasterStencilFaceKey Disabled => new(GpuCompareOp.Always,
            NativeGpuStencilOperation.Keep, NativeGpuStencilOperation.Keep, NativeGpuStencilOperation.Keep);
    }

    internal readonly record struct RasterDepthStencilKey(bool DepthTest, bool DepthWrite, GpuCompareOp DepthCompare,
        bool StencilTest, byte StencilReadMask, byte StencilWriteMask, RasterStencilFaceKey Front, RasterStencilFaceKey Back)
    {
        public static RasterDepthStencilKey From(NativeGpuDepthStencilState state) => new(state.DepthTest,
            state.DepthTest && state.DepthWrite, state.DepthTest ? state.DepthCompare : GpuCompareOp.Always,
            state.StencilTest, state.StencilTest ? state.StencilReadMask : (byte)0,
            state.StencilTest ? state.StencilWriteMask : (byte)0,
            state.StencilTest ? RasterStencilFaceKey.From(state.Front) : RasterStencilFaceKey.Disabled,
            state.StencilTest ? RasterStencilFaceKey.From(state.Back) : RasterStencilFaceKey.Disabled);
    }

    private static DepthStencilDesc RasterDepthDescription(RasterDepthStencilKey state) => new(
        state.DepthTest, state.DepthWrite ? DepthWriteMask.All : DepthWriteMask.Zero, SamplerComparison(state.DepthCompare),
        state.StencilTest, state.StencilReadMask, state.StencilWriteMask,
        RasterStencilDescription(state.Front), RasterStencilDescription(state.Back));

    private static DepthStencilopDesc RasterStencilDescription(RasterStencilFaceKey face) => new(
        RasterStencilOperation(face.FailOp), RasterStencilOperation(face.DepthFailOp),
        RasterStencilOperation(face.PassOp), SamplerComparison(face.Compare));

    private static StencilOp RasterStencilOperation(NativeGpuStencilOperation operation) => operation switch
    {
        NativeGpuStencilOperation.Keep => StencilOp.Keep,
        NativeGpuStencilOperation.Zero => StencilOp.Zero,
        NativeGpuStencilOperation.Replace => StencilOp.Replace,
        NativeGpuStencilOperation.IncrementClamp => StencilOp.IncrSat,
        NativeGpuStencilOperation.DecrementClamp => StencilOp.DecrSat,
        NativeGpuStencilOperation.Invert => StencilOp.Invert,
        NativeGpuStencilOperation.IncrementWrap => StencilOp.Incr,
        NativeGpuStencilOperation.DecrementWrap => StencilOp.Decr,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static CullMode RasterCullMode(NativeGpuCullMode mode) => mode switch
    {
        NativeGpuCullMode.None => CullMode.None,
        NativeGpuCullMode.Front => CullMode.Front,
        NativeGpuCullMode.Back => CullMode.Back,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static RenderTargetBlendDesc RasterBlendDescription(NativeGpuColorTargetDescription color)
    {
        NativeGpuBlendDescription blend = color.Blend;
        if (!blend.Enabled) { return new(false, false, Blend.One, Blend.Zero, BlendOp.Add,
            Blend.One, Blend.Zero, BlendOp.Add, LogicOp.Noop, (byte)color.WriteMask); }
        return new(true, false, RasterBlendFactor(blend.SourceColorFactor), RasterBlendFactor(blend.DestinationColorFactor),
            RasterBlendOperation(blend.ColorOperation), RasterBlendFactor(blend.SourceAlphaFactor),
            RasterBlendFactor(blend.DestinationAlphaFactor), RasterBlendOperation(blend.AlphaOperation), LogicOp.Noop, (byte)color.WriteMask);
    }

    private static Blend RasterBlendFactor(NativeGpuBlendFactor factor) => factor switch
    {
        NativeGpuBlendFactor.Zero => Blend.Zero,
        NativeGpuBlendFactor.One => Blend.One,
        NativeGpuBlendFactor.SourceColor => Blend.SrcColor,
        NativeGpuBlendFactor.OneMinusSourceColor => Blend.InvSrcColor,
        NativeGpuBlendFactor.DestinationColor => Blend.DestColor,
        NativeGpuBlendFactor.OneMinusDestinationColor => Blend.InvDestColor,
        NativeGpuBlendFactor.SourceAlpha => Blend.SrcAlpha,
        NativeGpuBlendFactor.OneMinusSourceAlpha => Blend.InvSrcAlpha,
        NativeGpuBlendFactor.DestinationAlpha => Blend.DestAlpha,
        NativeGpuBlendFactor.OneMinusDestinationAlpha => Blend.InvDestAlpha,
        NativeGpuBlendFactor.SourceAlphaSaturate => Blend.SrcAlphaSat,
        _ => throw new ArgumentOutOfRangeException(nameof(factor)),
    };

    private static BlendOp RasterBlendOperation(NativeGpuBlendOperation operation) => operation switch
    {
        NativeGpuBlendOperation.Add => BlendOp.Add,
        NativeGpuBlendOperation.Subtract => BlendOp.Subtract,
        NativeGpuBlendOperation.ReverseSubtract => BlendOp.RevSubtract,
        NativeGpuBlendOperation.Minimum => BlendOp.Min,
        NativeGpuBlendOperation.Maximum => BlendOp.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
