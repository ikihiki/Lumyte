namespace Lumyte.UI.Navigation;

/// <summary>Bounds retained destinations, including forward history.</summary>
public sealed record UiNavigationOptions
{
    /// <summary>Gets optional presentation effects, excluded from retained history.</summary>
    public UiPageTransitions? Transitions { get; init; }

    /// <summary>Gets the positive history limit.</summary>
    public int MaxHistoryEntries { get; init; } = 64;
}
