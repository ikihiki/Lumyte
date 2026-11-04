using System.Numerics;

namespace Lumyte.UI;

/// <summary>An immutable drawing pose with scaling around the image center.</summary>
public sealed record UiVisualPose
{
    /// <summary>Gets the finite unit opacity.</summary>
    public float Opacity { get; init; } = 1;

    /// <summary>Gets the finite logical translation.</summary>
    public Vector2 Offset { get; init; }

    /// <summary>Gets the positive finite scale.</summary>
    public Vector2 Scale { get; init; } = Vector2.One;

    internal static UiVisualPose Identity { get; } = new();

    internal void Validate()
    {
        if (!float.IsFinite(Opacity) || Opacity < 0 || Opacity > 1 || !float.IsFinite(Offset.X) || !float.IsFinite(Offset.Y)
            || !float.IsFinite(Scale.X) || !float.IsFinite(Scale.Y) || Scale.X <= 0 || Scale.Y <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(UiVisualPose));
        }
    }
}
