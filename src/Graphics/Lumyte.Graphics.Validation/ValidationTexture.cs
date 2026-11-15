using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationTexture : ValidationResource<IGraphicsTexture>, IGraphicsTexture
{
    private readonly IValidationObject? _parent;

    internal ValidationTexture(ValidationDevice device, IGraphicsTexture raw, object parent, bool ownsNative = true)
        : base(device, raw, parent, ownsNative)
    {
        _parent = parent as IValidationObject;
    }

    public uint Width
    {
        get
        {
            ValidateAlive();
            return Raw.Width;
        }
    }

    public uint Height
    {
        get
        {
            ValidateAlive();
            return Raw.Height;
        }
    }

    public uint MipLevels
    {
        get
        {
            ValidateAlive();
            return Raw.MipLevels;
        }
    }

    public uint ArrayLayers
    {
        get
        {
            ValidateAlive();
            return Raw.ArrayLayers;
        }
    }

    public TextureFormat Format
    {
        get
        {
            ValidateAlive();
            return Raw.Format;
        }
    }

    public TextureUsage Usage
    {
        get
        {
            ValidateAlive();
            return Raw.Usage;
        }
    }

    public override void ValidateAlive()
    {
        base.ValidateAlive();
        _parent?.ValidateAlive();
    }

    public override void Dispose()
    {
        if (!OwnsNative)
        {
            throw new InvalidOperationException("Borrowed frame images cannot be disposed directly.");
        }

        base.Dispose();
    }

    public (uint Width, uint Height) GetMipSize(uint mipLevel)
    {
        ValidateAlive();
        return Raw.GetMipSize(mipLevel);
    }

    public IGraphicsTextureView CreateView(TextureViewDesc? desc = null)
    {
        ValidateAlive();
        return Device.OwnCreated(Raw.CreateView(desc), raw => new ValidationTextureView(Device, raw, this));
    }

    internal void ReleaseFrameImage() => base.Dispose();
}
