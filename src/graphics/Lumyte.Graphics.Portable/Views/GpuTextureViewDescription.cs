namespace Lumyte.Graphics.Portable;

/// <summary>A non-owning interpretation of a texture. Null values request the corresponding defaults.</summary>
public readonly record struct GpuTextureViewDescription(
    GpuFormat? Format = null,
    GpuTextureViewDimension? Dimension = null,
    GpuTextureAspect Aspect = GpuTextureAspect.All,
    uint BaseMip = 0,
    uint? MipCount = null,
    uint BaseLayer = 0,
    uint? LayerCount = null)
{
    /// <summary>Resolves omitted values from caller-supplied creation values without accessing a device.</summary>
    /// <remarks>
    /// Checks only host range arithmetic. Format, aspect, dimension, usage, and sample compatibility remain
    /// runtime responsibilities. A packed depth/stencil format remains the logical format; the backend
    /// translates that format together with <see cref="Aspect"/> to the native aspect-specific format.
    /// </remarks>
    public GpuTextureViewDescription Normalize(in GpuTextureDescription textureDescription)
    {
        if (BaseMip > textureDescription.MipCount)
        { throw new ArgumentOutOfRangeException(nameof(textureDescription), "BaseMip exceeds the described mip count."); }
        uint mipCount = MipCount ?? textureDescription.MipCount - BaseMip;
        if (mipCount > textureDescription.MipCount - BaseMip)
        { throw new ArgumentOutOfRangeException(nameof(textureDescription), "MipCount exceeds the described remaining mip levels."); }
        if (BaseLayer > textureDescription.LayerCount)
        { throw new ArgumentOutOfRangeException(nameof(textureDescription), "BaseLayer exceeds the described layer count."); }

        GpuTextureViewDimension dimension = Dimension ?? textureDescription.Dimension switch
        {
            GpuTextureDimension.Texture1D => GpuTextureViewDimension.Texture1D,
            GpuTextureDimension.Texture2D => textureDescription.LayerCount > 1
                ? GpuTextureViewDimension.Texture2DArray : GpuTextureViewDimension.Texture2D,
            GpuTextureDimension.Texture3D => GpuTextureViewDimension.Texture3D,
            _ => throw new ArgumentOutOfRangeException(nameof(textureDescription)),
        };
        uint layerCount = LayerCount ?? dimension switch
        {
            GpuTextureViewDimension.Texture1D or GpuTextureViewDimension.Texture2D or GpuTextureViewDimension.Texture3D => 1,
            GpuTextureViewDimension.Cube => 6,
            GpuTextureViewDimension.Texture2DArray or GpuTextureViewDimension.CubeArray => textureDescription.LayerCount - BaseLayer,
            _ => throw new InvalidOperationException("Cannot infer LayerCount for an unknown view Dimension."),
        };
        if (layerCount > textureDescription.LayerCount - BaseLayer)
        { throw new ArgumentOutOfRangeException(nameof(textureDescription), "LayerCount exceeds the described remaining layers."); }

        return this with { Format = Format ?? textureDescription.Format, Dimension = dimension, MipCount = mipCount, LayerCount = layerCount };
    }
}
