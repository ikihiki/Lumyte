using System.Numerics;

using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal static class TextureValidation
{
    internal static void Validate(TextureDesc desc, DeviceCaps caps)
    {
        ArgumentNullException.ThrowIfNull(desc);
        if (desc.Format is TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8 && desc.Usage != TextureUsage.RenderAttachment)
        {
            throw new NotSupportedException("Depth/stencil textures currently support attachment usage only.");
        }

        const TextureUsage KnownUsage = TextureUsage.CopySource | TextureUsage.CopyDestination | TextureUsage.Sampled | TextureUsage.RenderAttachment;
        if (desc.Width == 0 || desc.Height == 0 || desc.Width > caps.MaxTextureDimension2D || desc.Height > caps.MaxTextureDimension2D ||
            desc.ArrayLayers == 0 || desc.ArrayLayers > caps.MaxTextureArrayLayers || desc.MipLevels == 0 ||
            desc.MipLevels > BitOperations.Log2(Math.Max(desc.Width, desc.Height)) + 1 || !Enum.IsDefined(desc.Format) ||
            desc.Usage == 0 || (desc.Usage & ~KnownUsage) != 0)
        {
            throw new ArgumentException("Invalid texture dimensions, mip count, layers, format, usage or device limit.", nameof(desc));
        }
    }

    internal static TextureViewInfo Resolve(IGraphicsTexture texture, TextureViewDesc? desc)
    {
        if ((texture.Usage & (TextureUsage.Sampled | TextureUsage.RenderAttachment)) == 0)
        {
            throw new InvalidOperationException("A view requires Sampled or RenderAttachment usage.");
        }

        desc ??= new() { Dimension = texture.ArrayLayers == 1 ? TextureViewDimension.D2 : TextureViewDimension.D2Array };
        if (!Enum.IsDefined(desc.Dimension) || desc.BaseMipLevel >= texture.MipLevels || desc.BaseArrayLayer >= texture.ArrayLayers)
        {
            throw new ArgumentException("Invalid view dimension or base subresource.", nameof(desc));
        }

        uint mips = desc.MipLevelCount ?? texture.MipLevels - desc.BaseMipLevel;
        uint layers = desc.ArrayLayerCount ?? desc.Dimension switch
        {
            TextureViewDimension.D2 => 1U,
            TextureViewDimension.Cube => 6U,
            _ => texture.ArrayLayers - desc.BaseArrayLayer,
        };
        bool cube = desc.Dimension is TextureViewDimension.Cube or TextureViewDimension.CubeArray;
        if (mips == 0 || mips > texture.MipLevels - desc.BaseMipLevel || layers == 0 || layers > texture.ArrayLayers - desc.BaseArrayLayer ||
            (desc.Dimension == TextureViewDimension.D2 && layers != 1) ||
            (desc.Dimension == TextureViewDimension.Cube && layers != 6) ||
            (cube && (layers % 6 != 0 || texture.Width != texture.Height)))
        {
            throw new ArgumentException("Invalid view range or cube shape.", nameof(desc));
        }

        return new()
        {
            Format = texture.Format,
            Dimension = desc.Dimension,
            BaseMipLevel = desc.BaseMipLevel,
            MipLevelCount = mips,
            BaseArrayLayer = desc.BaseArrayLayer,
            ArrayLayerCount = layers,
        };
    }
}
