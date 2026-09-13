namespace Lumyte.Graphics.Portable;

public enum GpuStencilOp { Keep, Zero, Replace, IncrementClamp, DecrementClamp, Invert, IncrementWrap, DecrementWrap }

public readonly record struct GpuStencilFaceState(
    GpuCompareOp Compare = GpuCompareOp.Always,
    GpuStencilOp FailOp = GpuStencilOp.Keep,
    GpuStencilOp DepthFailOp = GpuStencilOp.Keep,
    GpuStencilOp PassOp = GpuStencilOp.Keep)
{
    public GpuStencilFaceState() : this(Compare: GpuCompareOp.Always) { }
}

/// <summary>Fixed depth/stencil conditions and depth bias. Disabled tests do not read their comparison or operation fields.</summary>
/// <remarks>Both default(T) and the parameterless constructor disable depth and stencil tests and writes.</remarks>
public readonly record struct GpuDepthStencilState
{
    public GpuDepthStencilState(bool DepthTest = false, bool DepthWrite = false,
        GpuCompareOp DepthCompare = GpuCompareOp.LessEqual, bool StencilTest = false,
        uint StencilReadMask = uint.MaxValue, uint StencilWriteMask = uint.MaxValue,
        GpuStencilFaceState? Front = null, GpuStencilFaceState? Back = null,
        int DepthBias = 0, float DepthBiasSlopeScale = 0, float DepthBiasClamp = 0)
    {
        this.DepthTest = DepthTest;
        this.DepthWrite = DepthWrite;
        this.DepthCompare = DepthCompare;
        this.StencilTest = StencilTest;
        this.StencilReadMask = StencilReadMask;
        this.StencilWriteMask = StencilWriteMask;
        this.Front = Front ?? new GpuStencilFaceState();
        this.Back = Back ?? new GpuStencilFaceState();
        this.DepthBias = DepthBias;
        this.DepthBiasSlopeScale = DepthBiasSlopeScale;
        this.DepthBiasClamp = DepthBiasClamp;
    }

    public GpuDepthStencilState() : this(DepthTest: false) { }

    public bool DepthTest { get; init; }
    public bool DepthWrite { get; init; }
    public GpuCompareOp DepthCompare { get; init; }
    public bool StencilTest { get; init; }
    public uint StencilReadMask { get; init; }
    public uint StencilWriteMask { get; init; }
    public GpuStencilFaceState Front { get; init; }
    public GpuStencilFaceState Back { get; init; }
    public int DepthBias { get; init; }
    public float DepthBiasSlopeScale { get; init; }
    public float DepthBiasClamp { get; init; }
}
