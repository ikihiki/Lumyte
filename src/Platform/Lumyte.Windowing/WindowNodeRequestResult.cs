using Lumyte.Windowing.Abstractions;

namespace Lumyte.Windowing;

/// <summary>Defines WindowNodeRequestResult.</summary>
/// <param name="Node">The Node value.</param>
/// <param name="Request">The Request value.</param>
/// <param name="Status">The Status value.</param>
/// <param name="Error">The Error value.</param>
public sealed record WindowNodeRequestResult(
    WindowNodeId Node, WindowNodeRequestId Request, WindowRequestStatus Status, string? Error);
