namespace Lumyte.Graphics.Portable;

public enum GpuSamplerBindingType { Filtering, NonFiltering, Comparison }

public readonly record struct GpuSamplerBindingLayout(GpuSamplerBindingType Type);
