using Lumyte.Graphics.ImageEffects;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Describes an effect on the current layer contents preceding this command.</summary>
public sealed record BackdropEffectOptions2D
{
    /// <summary>Gets the local rectangle where the effected background replaces the original.</summary>
    public required Rect2D OutputBounds { get; init; }

    /// <summary>Gets the effect graph evaluated against the preceding background snapshot.</summary>
    public required ImageEffectGraph Effects { get; init; }
}
