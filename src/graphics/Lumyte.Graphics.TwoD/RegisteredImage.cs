namespace Lumyte.Graphics.TwoD;

internal readonly record struct RegisteredImage(
    GpuTextureHandle Texture,
    GpuTextureDescription Description,
    SamplerId Sampler);
