namespace Lumyte.Graphics.Portable;

public enum GpuTextureSampleType { Float, UnfilterableFloat, Depth, Sint, Uint }

public readonly record struct GpuTextureBindingLayout(
    GpuTextureSampleType SampleType,
    GpuTextureViewDimension ViewDimension = GpuTextureViewDimension.Texture2D,
    bool Multisampled = false);
