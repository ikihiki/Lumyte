namespace Lumyte.UI.Navigation;

/// <summary>Identifies a history destination, never a retained page instance.</summary>
/// <param name="Id">The Id value.</param>
/// <param name="Destination">The Destination value.</param>
public sealed record UiNavigationEntry(Guid Id, UiPageDestination Destination);
