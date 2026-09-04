namespace Lumyte.Graphics;

internal readonly record struct GpuAttachmentLayout(GpuFormat ColorFormat, GpuFormat? DepthStencilFormat = null, uint SampleCount = 1)
{
    public GpuAttachmentLayout Validate()
    {
        if (!GpuFormatInfo.IsColor(ColorFormat))
        {
            throw new ArgumentException($"{ColorFormat} is not a color attachment format.", nameof(ColorFormat));
        }
        if (DepthStencilFormat is { } depth && !GpuFormatInfo.IsDepthStencilAttachment(depth))
        {
            throw new ArgumentException($"{depth} is not a depth-stencil attachment format.", nameof(DepthStencilFormat));
        }
        if (SampleCount != 1)
        {
            throw new NotSupportedException("Only sample count 1 is currently supported.");
        }
        return this;
    }
}

internal readonly record struct GpuGraphicsPipelineDesc(
    GpuAttachmentLayout Attachments,
    GpuPrimitiveTopology Topology = GpuPrimitiveTopology.TriangleList,
    string VertexEntry = "vsMain",
    string PixelEntry = "psMain")
{
    public GpuGraphicsPipelineDesc Validate()
    {
        Attachments.Validate();
        if (!Enum.IsDefined(Topology))
        {
            throw new ArgumentOutOfRangeException(nameof(Topology));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(VertexEntry);
        ArgumentException.ThrowIfNullOrWhiteSpace(PixelEntry);
        return this;
    }
}

public enum GpuCullMode { None, Front, Back }
public enum GpuFrontFace { CounterClockwise, Clockwise }
public enum GpuPrimitiveTopology { TriangleList, TriangleStrip }
internal enum GpuBlendMode { None, AlphaBlend }

public readonly record struct GpuRasterizerState(GpuCullMode CullMode, GpuFrontFace FrontFace)
{
    public static GpuRasterizerState Default => new(GpuCullMode.None, GpuFrontFace.CounterClockwise);
}

internal readonly record struct GpuBlendState(GpuBlendMode Mode)
{
    public static GpuBlendState None => new(GpuBlendMode.None);
    public static GpuBlendState AlphaBlend => new(GpuBlendMode.AlphaBlend);
}

public enum GpuCompareOp { Never, Less, Equal, LessEqual, Greater, NotEqual, GreaterEqual, Always }
public enum GpuStencilOp { Keep, Zero, Replace, IncrementClamp, DecrementClamp, Invert, IncrementWrap, DecrementWrap }

public readonly record struct GpuStencilFaceState(
    GpuCompareOp Compare,
    GpuStencilOp FailOp,
    GpuStencilOp DepthFailOp,
    GpuStencilOp PassOp)
{
    public static GpuStencilFaceState Default => new(GpuCompareOp.Always, GpuStencilOp.Keep, GpuStencilOp.Keep, GpuStencilOp.Keep);
}

public readonly record struct GpuDepthStencilState(
    bool DepthTest,
    bool DepthWrite,
    GpuCompareOp DepthCompare,
    bool StencilTest,
    GpuStencilFaceState StencilFront,
    GpuStencilFaceState StencilBack,
    uint StencilReadMask,
    uint StencilWriteMask)
{
    public static GpuDepthStencilState Default => new(false, false, GpuCompareOp.LessEqual, false,
        GpuStencilFaceState.Default, GpuStencilFaceState.Default, 0xff, 0xff);

    public GpuDepthStencilState Normalize()
    {
        ValidateByte(StencilReadMask, nameof(StencilReadMask));
        ValidateByte(StencilWriteMask, nameof(StencilWriteMask));
        if (!Enum.IsDefined(DepthCompare))
        {
            throw new ArgumentOutOfRangeException(nameof(DepthCompare));
        }
        if (!StencilTest)
        {
            return this with
            {
                StencilFront = GpuStencilFaceState.Default,
                StencilBack = GpuStencilFaceState.Default,
                StencilReadMask = 0xff,
                StencilWriteMask = 0xff,
            };
        }
        ValidateFace(StencilFront, nameof(StencilFront));
        ValidateFace(StencilBack, nameof(StencilBack));
        return this;
    }

    internal static void ValidateByte(uint value, string name)
    {
        if (value > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(name, value, "Stencil values must be in the range 0..255.");
        }
    }

    private static void ValidateFace(GpuStencilFaceState face, string name)
    {
        if (!Enum.IsDefined(face.Compare) || !Enum.IsDefined(face.FailOp)
            || !Enum.IsDefined(face.DepthFailOp) || !Enum.IsDefined(face.PassOp))
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}

internal static class GpuGraphicsStateValidation
{
    internal static void ValidateDepthStencilAttachmentRequirements(
        GpuAttachmentLayout attachments, GpuDepthStencilState depthStencil)
    {
        GpuFormat? format = attachments.DepthStencilFormat;
        if ((depthStencil.DepthTest || depthStencil.DepthWrite)
            && (format is not { } depth || !GpuFormatInfo.HasDepth(depth)))
        {
            throw new InvalidOperationException("Depth state requires a depth attachment format.");
        }
        if (depthStencil.StencilTest
            && (format is not { } stencil || !GpuFormatInfo.HasStencil(stencil)))
        {
            throw new InvalidOperationException("Stencil state requires a stencil attachment format.");
        }
    }
}

public readonly record struct GpuViewport(float X, float Y, float Width, float Height, float MinDepth = 0, float MaxDepth = 1);
public readonly record struct GpuScissorRect(uint X, uint Y, uint Width, uint Height);

public readonly record struct GpuPipelineDiagnostics(ulong CacheHits, ulong CacheMisses, ulong NativePipelineCount);
