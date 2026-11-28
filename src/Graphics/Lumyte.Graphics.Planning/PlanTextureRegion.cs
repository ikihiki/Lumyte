using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;
/// <summary>Declares plan texture region.</summary>
/// <param name = "MipLevel">The mip level.</param>
/// <param name = "OriginX">The origin x.</param>
/// <param name = "OriginY">The origin y.</param>
/// <param name = "BaseArrayLayer">The base array layer.</param>
/// <param name = "Width">The width.</param>
/// <param name = "Height">The height.</param>
/// <param name = "ArrayLayerCount">The array layer count.</param>
public readonly record struct PlanTextureRegion(uint MipLevel, uint OriginX, uint OriginY, uint BaseArrayLayer, uint Width, uint Height, uint ArrayLayerCount = 1)
{
    internal PlanRange Range(PlanTexture texture)
    {
        if (Width == 0 || Height == 0 || ArrayLayerCount == 0 || MipLevel >= texture.Identity.MipLevels)
        {
            throw new ArgumentOutOfRangeException(nameof(Width));
        }

        // The current contiguous linear subresource use must conservatively include gaps between layers.
        return new(checked(((ulong)BaseArrayLayer * texture.Identity.MipLevels) + MipLevel), checked((((ulong)ArrayLayerCount - 1) * texture.Identity.MipLevels) + 1));
    }

    internal TextureCopyRegion Resolve(PlanTexture texture, PreparedResources resources) => new()
    {
        Texture = (IGraphicsTexture)resources.Allocations[texture.Identity],
        MipLevel = MipLevel,
        OriginX = OriginX,
        OriginY = OriginY,
        BaseArrayLayer = BaseArrayLayer,
        Width = Width,
        Height = Height,
        ArrayLayerCount = ArrayLayerCount,
    };
}
