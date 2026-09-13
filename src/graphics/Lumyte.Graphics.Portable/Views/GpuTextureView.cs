namespace Lumyte.Graphics.Portable;

/// <summary>A texture and its interpretation. Copying this value neither creates a GPU object nor retains resource ownership.</summary>
public readonly record struct GpuTextureView(GpuTextureHandle Texture, GpuTextureViewDescription Description = default)
{
    /// <summary>Resolves the description while preserving the caller's texture handle.</summary>
    public GpuTextureView Normalize(in GpuTextureDescription textureDescription)
        => this with { Description = Description.Normalize(textureDescription) };
}
