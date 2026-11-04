namespace Lumyte.UI.Windowing;

/// <summary>A detached UiWindowRequestResult value.</summary>
/// <param name="Id">The Id value.</param>
/// <param name="Window">The Window value.</param>
/// <param name="Status">The Status value.</param>
/// <param name="Code">The Code value.</param>
public sealed record UiWindowRequestResult(UiWindowRequestId Id, UiWindowId Window, UiWindowRequestStatus Status, string Code);
