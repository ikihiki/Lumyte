namespace Lumyte.Graphics.TwoD;

internal readonly record struct PreparedImage(
    ImageId Id,
    GpuTextureHandle Texture,
    GpuTextureDescription Description,
    SamplerId Sampler);
