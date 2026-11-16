using Lumyte.Core.Time;

namespace Lumyte.UI;

/// <summary>Provides timer-free custom drawing progression on the surface owner loop.</summary>
public partial class UiElement
{
    /// <summary>Gets a value indicating whether this element participates in the existing animation phase.</summary>
    /// <remarks>This property must be side-effect free. No time is evaluated by state getters.</remarks>
    protected virtual bool HasFrameAnimation => false;

    internal void ConnectFrameAnimation(UiSurface surface)
    {
        if (HasFrameAnimation)
        {
            surface.AddFrameAnimation(this);
        }
    }

    internal void AdvanceFrameAnimation(TimePoint? now) => OnAnimationFrame(now);

    /// <summary>Advances custom visual state at an explicit owner boundary.</summary>
    /// <param name="now">The borrowed surface clock, or null to freeze without catching up on resume.</param>
    protected virtual void OnAnimationFrame(TimePoint? now)
    {
    }
}
