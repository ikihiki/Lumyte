using Lumyte.Animation;
using Lumyte.Core.Time;

namespace Lumyte.UI.Navigation;

/// <summary>Optional finite page and shared-element presentation effects.</summary>
public sealed record UiPageTransitions
{
    /// <summary>Gets the entry effect.</summary>
    public UiVisualTransition? Enter { get; init; }

    /// <summary>Gets the exit effect.</summary>
    public UiVisualTransition? Exit { get; init; }

    /// <summary>Gets page sequencing.</summary>
    public UiPageTransitionOrder Order { get; init; }

    /// <summary>Gets a value indicating whether outer shared keys are connected within this slot.</summary>
    public bool ConnectSharedElements { get; init; }

    /// <summary>Gets the finite connected interval.</summary>
    public Duration ConnectedDuration { get; init; } = Duration.FromSeconds(0.3);

    /// <summary>Gets the connected temporal easing.</summary>
    public AnimationEasing ConnectedEasing { get; init; } = AnimationEasing.EaseInOut;

    internal void Validate()
    {
        if (Order is < UiPageTransitionOrder.Together or > UiPageTransitionOrder.ExitThenEnter || ConnectedDuration.Ticks < 0
            || ConnectedEasing is < AnimationEasing.Linear or > AnimationEasing.EaseInOut)
        {
            throw new ArgumentOutOfRangeException(nameof(UiPageTransitions));
        }

        if ((Enter is not null && Enter.To != new UiVisualPose()) || (Exit is not null && Exit.From != new UiVisualPose()))
        {
            throw new ArgumentException("Page effects must meet the ordinary identity pose.");
        }
    }
}
