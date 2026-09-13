namespace Lumyte.Graphics.Native;

/// <summary>Independent depth/stencil values. Both default and the parameterless constructor disable all tests and writes.</summary>
public readonly record struct NativeGpuDepthStencilState
{
    public NativeGpuDepthStencilState(
        bool DepthTest = false, bool DepthWrite = false,
        GpuCompareOp DepthCompare = GpuCompareOp.LessEqual, bool StencilTest = false,
        byte StencilReadMask = byte.MaxValue, byte StencilWriteMask = byte.MaxValue,
        NativeGpuStencilFaceState? Front = null, NativeGpuStencilFaceState? Back = null)
    {
        this.DepthTest = DepthTest;
        this.DepthWrite = DepthWrite;
        this.DepthCompare = DepthCompare;
        this.StencilTest = StencilTest;
        this.StencilReadMask = StencilReadMask;
        this.StencilWriteMask = StencilWriteMask;
        this.Front = Front ?? new NativeGpuStencilFaceState();
        this.Back = Back ?? new NativeGpuStencilFaceState();
    }

    public NativeGpuDepthStencilState() : this(DepthTest: false) { }

    public bool DepthTest { get; init; }
    public bool DepthWrite { get; init; }
    public GpuCompareOp DepthCompare { get; init; }
    public bool StencilTest { get; init; }
    public byte StencilReadMask { get; init; }
    public byte StencilWriteMask { get; init; }
    public NativeGpuStencilFaceState Front { get; init; }
    public NativeGpuStencilFaceState Back { get; init; }
}
