namespace Lumyte.UI.Navigation;

/// <summary>Provides the current entry and proposed destination to a leave guard.</summary>
/// <param name="Current">The Current value.</param>
/// <param name="Destination">The Destination value.</param>
/// <param name="Kind">The Kind value.</param>
/// <param name="Dispatcher">The Dispatcher value.</param>
public sealed record UiPageLeaveContext(UiNavigationEntry Current, UiPageDestination Destination, UiNavigationKind Kind, IUiDispatcher Dispatcher);
