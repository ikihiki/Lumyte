namespace Lumyte.UI.Application;

/// <summary>A close request; notification alone never changes the window model.</summary>
/// <param name="Key">The declaration key.</param>
/// <param name="Reason">The request origin.</param>
public sealed record UiWindowCloseRequest(string Key, UiWindowCloseReason Reason);
