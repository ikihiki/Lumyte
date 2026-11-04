namespace Lumyte.UI;

/// <summary>Finite presence effects attached to one declaration identity.</summary>
public sealed record UiElementTransitions
{
    /// <summary>Gets the optional entry effect.</summary>
    public UiVisualTransition? Enter { get; init; }

    /// <summary>Gets the optional exit effect.</summary>
    public UiVisualTransition? Exit { get; init; }

    /// <summary>Gets a value indicating whether first publication also enters.</summary>
    public bool AnimateInitial { get; init; } = true;

    internal void Validate()
    {
        if ((Enter is not null && Enter.To != UiVisualPose.Identity) || (Exit is not null && Exit.From != UiVisualPose.Identity))
        {
            throw new ArgumentException("Entry must end at identity and exit must start at identity.");
        }
    }
}
