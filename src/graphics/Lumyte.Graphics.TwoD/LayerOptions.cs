namespace Lumyte.Graphics.TwoD;

/// <summary>Describes an isolated group that is composited after its children are rendered.</summary>
public readonly record struct LayerOptions
{
    public LayerOptions() { }

    public float Opacity { get; init; } = 1;
    public BlendMode BlendMode { get; init; } = BlendMode.SourceOver;
    /// <summary>An image whose alpha is sampled in normalized target coordinates.</summary>
    public ImageId Mask { get; init; }
    /// <summary>Blur radius in target pixels.</summary>
    public float BlurRadius { get; init; }
    public ShadowOptions? Shadow { get; init; }

    internal LayerOptions Validate(Renderer renderer, string parameterName)
    {
        if (!float.IsFinite(Opacity) || Opacity < 0 || Opacity > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Layer opacity must be between zero and one.");
        }
        if (!Enum.IsDefined(BlendMode))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Layer blend mode is unknown.");
        }
        if (!float.IsFinite(BlurRadius) || BlurRadius < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Layer blur radius cannot be negative.");
        }
        if (!Mask.IsNull)
        {
            renderer.RequireImage(Mask);
        }
        Shadow?.Validate(parameterName);
        return this;
    }
}
