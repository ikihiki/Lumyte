namespace Lumyte.UI.Navigation;

/// <summary>Bounds retained destinations, including forward history.</summary>
public sealed record UiNavigationOptions
{
    /// <summary>Gets the positive history limit.</summary>
    public int MaxHistoryEntries { get; init; } = 64;
}
