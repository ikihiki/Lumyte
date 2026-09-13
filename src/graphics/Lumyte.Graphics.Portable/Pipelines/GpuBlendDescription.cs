namespace Lumyte.Graphics.Portable;

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

public enum GpuBlendOperation { Add, Subtract, ReverseSubtract, Minimum, Maximum }

/// <summary>Blend factors. Source1 factors require the dual-source feature and a matching shader.</summary>
public enum GpuBlendFactor
{
    Zero, One,
    SourceColor, OneMinusSourceColor, DestinationColor, OneMinusDestinationColor,
    SourceAlpha, OneMinusSourceAlpha, DestinationAlpha, OneMinusDestinationAlpha,
    SourceAlphaSaturated, Constant, OneMinusConstant,
    Source1Color, OneMinusSource1Color, Source1Alpha, OneMinusSource1Alpha,
}

/// <summary>Independent color and alpha blend equations. Use null in a color target to disable blending.</summary>
/// <remarks>Use <c>new GpuBlendDescription()</c> for source replacement defaults; default(T) retains all-zero fields.</remarks>
public readonly record struct GpuBlendDescription(
    GpuBlendOperation ColorOperation = GpuBlendOperation.Add,
    GpuBlendFactor SourceColorFactor = GpuBlendFactor.One,
    GpuBlendFactor DestinationColorFactor = GpuBlendFactor.Zero,
    GpuBlendOperation AlphaOperation = GpuBlendOperation.Add,
    GpuBlendFactor SourceAlphaFactor = GpuBlendFactor.One,
    GpuBlendFactor DestinationAlphaFactor = GpuBlendFactor.Zero)
{
    public GpuBlendDescription() : this(ColorOperation: GpuBlendOperation.Add) { }
}

/// <summary>A fragment output format, channel mask, and optional blend equation.</summary>
public readonly record struct GpuColorTargetDescription(
    GpuFormat Format,
    GpuColorWriteMask WriteMask = GpuColorWriteMask.All,
    GpuBlendDescription? Blend = null);
