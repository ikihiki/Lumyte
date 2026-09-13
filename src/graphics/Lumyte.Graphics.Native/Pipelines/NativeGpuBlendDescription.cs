namespace Lumyte.Graphics.Native;

/// <summary>Fixed blend values for one color target; native validation checks their legal combinations.</summary>
public readonly record struct NativeGpuBlendDescription(
    bool Enabled = false,
    NativeGpuBlendOperation ColorOperation = NativeGpuBlendOperation.Add,
    NativeGpuBlendFactor SourceColorFactor = NativeGpuBlendFactor.One,
    NativeGpuBlendFactor DestinationColorFactor = NativeGpuBlendFactor.Zero,
    NativeGpuBlendOperation AlphaOperation = NativeGpuBlendOperation.Add,
    NativeGpuBlendFactor SourceAlphaFactor = NativeGpuBlendFactor.One,
    NativeGpuBlendFactor DestinationAlphaFactor = NativeGpuBlendFactor.Zero)
{
    public NativeGpuBlendDescription() : this(Enabled: false) { }
}
