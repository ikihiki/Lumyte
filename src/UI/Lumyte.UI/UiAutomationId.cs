namespace Lumyte.UI;

/// <summary>Identifies one connection of an element to one surface; zero is invalid.</summary>
/// <param name="SurfaceId">The surface identity.</param>
/// <param name="Value">The never reused connection identity.</param>
public readonly record struct UiAutomationId(Guid SurfaceId, ulong Value);
