namespace Lumyte.Graphics;

[Flags]
public enum GpuColorWriteMask : byte
{
    None = 0,
    Red = 1 << 0,
    Green = 1 << 1,
    Blue = 1 << 2,
    Alpha = 1 << 3,
    All = Red | Green | Blue | Alpha,
}

public readonly record struct GpuColorTargetDescription(
    GpuFormat Format,
    GpuColorWriteMask WriteMask = GpuColorWriteMask.All);

/// <summary>Only state that can affect graphics shader microcode.</summary>
public sealed class GpuRasterPipelineDescription
{
    private readonly GpuColorTargetDescription[] colorTargets;

    public GpuRasterPipelineDescription(
        IEnumerable<GpuColorTargetDescription> colorTargets,
        GpuFormat? depthFormat = null,
        GpuFormat? stencilFormat = null)
    {
        ArgumentNullException.ThrowIfNull(colorTargets);
        this.colorTargets = colorTargets.ToArray();
        DepthFormat = depthFormat;
        StencilFormat = stencilFormat;
    }

    public IReadOnlyList<GpuColorTargetDescription> ColorTargets => colorTargets;
    public GpuFormat? DepthFormat { get; }
    public GpuFormat? StencilFormat { get; }
    public GpuPrimitiveTopology Topology { get; init; } = GpuPrimitiveTopology.TriangleList;
    public GpuCullMode CullMode { get; init; } = GpuCullMode.None;
    public GpuFrontFace FrontFace { get; init; } = GpuFrontFace.CounterClockwise;
    public uint SampleCount { get; init; } = 1;
    public bool AlphaToCoverage { get; init; }
    public bool SupportsDualSourceBlending { get; init; }
    public GpuBlendDescription? EmbeddedBlend { get; init; }

    public GpuRasterPipelineDescription Validate()
    {
        if (!Enum.IsDefined(Topology) || !Enum.IsDefined(CullMode) || !Enum.IsDefined(FrontFace))
        {
            throw new ArgumentOutOfRangeException(nameof(Topology), "Raster state contains an unknown value.");
        }
        if (colorTargets.Length == 0 && DepthFormat is null && StencilFormat is null)
        {
            throw new InvalidOperationException("A raster pipeline requires an attachment format.");
        }

        foreach (GpuColorTargetDescription target in colorTargets)
        {
            if (!GpuFormatInfo.IsColor(target.Format))
            {
                throw new ArgumentException($"{target.Format} is not a color format.", nameof(ColorTargets));
            }

            if ((target.WriteMask & ~GpuColorWriteMask.All) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ColorTargets));
            }
        }

        if (DepthFormat is { } depth && !GpuFormatInfo.HasDepth(depth))
        {
            throw new ArgumentException($"{depth} is not a depth format.", nameof(DepthFormat));
        }

        if (StencilFormat is { } stencil && !GpuFormatInfo.HasStencil(stencil))
        {
            throw new ArgumentException($"{stencil} is not a stencil format.", nameof(StencilFormat));
        }

        if (SampleCount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SampleCount));
        }

        EmbeddedBlend?.Validate();
        return this;
    }
}

public enum GpuBlendOperation { Add, Subtract, ReverseSubtract, Minimum, Maximum }

public enum GpuBlendFactor
{
    Zero,
    One,
    SourceColor,
    OneMinusSourceColor,
    DestinationColor,
    OneMinusDestinationColor,
    SourceAlpha,
    OneMinusSourceAlpha,
    DestinationAlpha,
    OneMinusDestinationAlpha,
}

public sealed record GpuBlendDescription(
    GpuBlendOperation ColorOperation = GpuBlendOperation.Add,
    GpuBlendFactor SourceColorFactor = GpuBlendFactor.One,
    GpuBlendFactor DestinationColorFactor = GpuBlendFactor.Zero,
    GpuBlendOperation AlphaOperation = GpuBlendOperation.Add,
    GpuBlendFactor SourceAlphaFactor = GpuBlendFactor.One,
    GpuBlendFactor DestinationAlphaFactor = GpuBlendFactor.Zero,
    GpuColorWriteMask ColorWriteMask = GpuColorWriteMask.All)
{
    public GpuBlendDescription Validate()
    {
        if (!Enum.IsDefined(ColorOperation) || !Enum.IsDefined(AlphaOperation)
            || !Enum.IsDefined(SourceColorFactor) || !Enum.IsDefined(DestinationColorFactor)
            || !Enum.IsDefined(SourceAlphaFactor) || !Enum.IsDefined(DestinationAlphaFactor))
        {
            throw new ArgumentOutOfRangeException(nameof(ColorOperation), "Blend state contains an unknown value.");
        }

        if ((ColorWriteMask & ~GpuColorWriteMask.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ColorWriteMask));
        }

        return this;
    }
}

/// <summary>Backend-ready shader IR for one device, not a multi-backend asset container.</summary>
public readonly struct GpuShaderBinary
{
    public GpuShaderBinary(
        ReadOnlyMemory<byte> bytes,
        GpuShaderCodeFormat format,
        GpuShaderStage stage,
        string entryPoint,
        ReadOnlyMemory<byte> abiHash)
    {
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("Shader IR cannot be empty.", nameof(bytes));
        }
        if (!Enum.IsDefined(format) || !Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);
        if (abiHash.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
        {
            throw new ArgumentException("ABI hash must be SHA-256 sized.", nameof(abiHash));
        }

        Bytes = bytes.ToArray();
        Format = format;
        Stage = stage;
        EntryPoint = entryPoint;
        AbiHash = abiHash.ToArray();
    }

    public ReadOnlyMemory<byte> Bytes { get; }
    public GpuShaderCodeFormat Format { get; }
    public GpuShaderStage Stage { get; }
    public string EntryPoint { get; }
    public ReadOnlyMemory<byte> AbiHash { get; }
}
