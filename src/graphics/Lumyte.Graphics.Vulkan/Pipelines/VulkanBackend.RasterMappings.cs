using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    internal static PrimitiveTopology RasterTopology(NativeGpuPrimitiveTopology topology) => topology switch
    {
        NativeGpuPrimitiveTopology.TriangleList => PrimitiveTopology.TriangleList,
        NativeGpuPrimitiveTopology.TriangleStrip => PrimitiveTopology.TriangleStrip,
        _ => throw new ArgumentOutOfRangeException(nameof(topology)),
    };

    internal static CullModeFlags RasterCullMode(NativeGpuCullMode mode) => mode switch
    {
        NativeGpuCullMode.None => CullModeFlags.None,
        NativeGpuCullMode.Front => CullModeFlags.FrontBit,
        NativeGpuCullMode.Back => CullModeFlags.BackBit,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    internal static FrontFace RasterFrontFace(NativeGpuFrontFace front) => front switch
    {
        NativeGpuFrontFace.Clockwise => FrontFace.Clockwise,
        NativeGpuFrontFace.CounterClockwise => FrontFace.CounterClockwise,
        _ => throw new ArgumentOutOfRangeException(nameof(front)),
    };

    internal static PipelineColorBlendAttachmentState RasterColorTarget(NativeGpuColorTargetDescription target) => new()
    {
        ColorWriteMask = (ColorComponentFlags)target.WriteMask, BlendEnable = target.Blend.Enabled,
        ColorBlendOp = RasterBlendOperation(target.Blend.ColorOperation),
        SrcColorBlendFactor = RasterBlendFactor(target.Blend.SourceColorFactor),
        DstColorBlendFactor = RasterBlendFactor(target.Blend.DestinationColorFactor),
        AlphaBlendOp = RasterBlendOperation(target.Blend.AlphaOperation),
        SrcAlphaBlendFactor = RasterBlendFactor(target.Blend.SourceAlphaFactor),
        DstAlphaBlendFactor = RasterBlendFactor(target.Blend.DestinationAlphaFactor),
    };

    internal static BlendOp RasterBlendOperation(NativeGpuBlendOperation operation) => operation switch
    {
        NativeGpuBlendOperation.Add => BlendOp.Add,
        NativeGpuBlendOperation.Subtract => BlendOp.Subtract,
        NativeGpuBlendOperation.ReverseSubtract => BlendOp.ReverseSubtract,
        NativeGpuBlendOperation.Minimum => BlendOp.Min,
        NativeGpuBlendOperation.Maximum => BlendOp.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    internal static BlendFactor RasterBlendFactor(NativeGpuBlendFactor factor) => factor switch
    {
        NativeGpuBlendFactor.Zero => BlendFactor.Zero,
        NativeGpuBlendFactor.One => BlendFactor.One,
        NativeGpuBlendFactor.SourceColor => BlendFactor.SrcColor,
        NativeGpuBlendFactor.OneMinusSourceColor => BlendFactor.OneMinusSrcColor,
        NativeGpuBlendFactor.DestinationColor => BlendFactor.DstColor,
        NativeGpuBlendFactor.OneMinusDestinationColor => BlendFactor.OneMinusDstColor,
        NativeGpuBlendFactor.SourceAlpha => BlendFactor.SrcAlpha,
        NativeGpuBlendFactor.OneMinusSourceAlpha => BlendFactor.OneMinusSrcAlpha,
        NativeGpuBlendFactor.DestinationAlpha => BlendFactor.DstAlpha,
        NativeGpuBlendFactor.OneMinusDestinationAlpha => BlendFactor.OneMinusDstAlpha,
        NativeGpuBlendFactor.SourceAlphaSaturate => BlendFactor.SrcAlphaSaturate,
        _ => throw new ArgumentOutOfRangeException(nameof(factor)),
    };

    internal static CompareOp RasterCompare(GpuCompareOp operation) => operation switch
    {
        GpuCompareOp.Never => CompareOp.Never,
        GpuCompareOp.Less => CompareOp.Less,
        GpuCompareOp.Equal => CompareOp.Equal,
        GpuCompareOp.LessEqual => CompareOp.LessOrEqual,
        GpuCompareOp.Greater => CompareOp.Greater,
        GpuCompareOp.NotEqual => CompareOp.NotEqual,
        GpuCompareOp.GreaterEqual => CompareOp.GreaterOrEqual,
        GpuCompareOp.Always => CompareOp.Always,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    internal static StencilOp RasterStencilOperation(NativeGpuStencilOperation operation) => operation switch
    {
        NativeGpuStencilOperation.Keep => StencilOp.Keep,
        NativeGpuStencilOperation.Zero => StencilOp.Zero,
        NativeGpuStencilOperation.Replace => StencilOp.Replace,
        NativeGpuStencilOperation.IncrementClamp => StencilOp.IncrementAndClamp,
        NativeGpuStencilOperation.DecrementClamp => StencilOp.DecrementAndClamp,
        NativeGpuStencilOperation.Invert => StencilOp.Invert,
        NativeGpuStencilOperation.IncrementWrap => StencilOp.IncrementAndWrap,
        NativeGpuStencilOperation.DecrementWrap => StencilOp.DecrementAndWrap,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    internal static IndexType RasterIndexType(NativeGpuIndexFormat format) => format switch
    {
        NativeGpuIndexFormat.Uint16 => IndexType.Uint16,
        NativeGpuIndexFormat.Uint32 => IndexType.Uint32,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    internal static Viewport RasterViewport(NativeGpuViewport viewport) => new(viewport.X, viewport.Y + viewport.Height,
        viewport.Width, -viewport.Height, viewport.MinDepth, viewport.MaxDepth);

    internal static AttachmentLoadOp RasterLoad(NativeGpuLoadOp load) => load switch
    {
        NativeGpuLoadOp.Load => AttachmentLoadOp.Load,
        NativeGpuLoadOp.Clear => AttachmentLoadOp.Clear,
        NativeGpuLoadOp.Discard => AttachmentLoadOp.DontCare,
        _ => throw new ArgumentOutOfRangeException(nameof(load)),
    };

    internal static AttachmentStoreOp RasterStore(NativeGpuStoreOp store) => store switch
    {
        NativeGpuStoreOp.Store => AttachmentStoreOp.Store,
        NativeGpuStoreOp.Discard => AttachmentStoreOp.DontCare,
        _ => throw new ArgumentOutOfRangeException(nameof(store)),
    };
}
