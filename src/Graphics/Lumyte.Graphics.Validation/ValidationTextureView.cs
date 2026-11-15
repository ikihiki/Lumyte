using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationTextureView : ValidationResource<IGraphicsTextureView>, IGraphicsTextureView
{
    private readonly ValidationTexture _texture;

    internal ValidationTextureView(ValidationDevice device, IGraphicsTextureView raw, ValidationTexture texture)
        : base(device, raw, texture)
    {
        _texture = texture;
    }

    public IGraphicsTexture Texture
    {
        get
        {
            ValidateAlive();
            return _texture;
        }
    }

    public TextureViewInfo Info
    {
        get
        {
            ValidateAlive();
            return Raw.Info;
        }
    }

    public override void ValidateAlive()
    {
        base.ValidateAlive();
        _texture.ValidateAlive();
    }
}
