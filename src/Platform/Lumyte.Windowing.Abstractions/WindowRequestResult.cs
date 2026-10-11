namespace Lumyte.Windowing.Abstractions;

/// <summary>Defines WindowRequestResult.</summary>
/// <param name="Window">The Window value.</param>
/// <param name="Request">The Request value.</param>
/// <param name="Status">The Status value.</param>
/// <param name="Error">The Error value.</param>
public sealed record WindowRequestResult(
    WindowId Window, WindowRequestId Request, WindowRequestStatus Status, string? Error);
