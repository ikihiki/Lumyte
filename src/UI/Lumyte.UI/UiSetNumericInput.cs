using System.Numerics;

namespace Lumyte.UI;

/// <summary>A closed scalar or atomic range user command, checked against the live input target.</summary>
/// <param name="Target">The exact connected owner.</param>
/// <param name="Value">The finite scalar or lower range endpoint.</param>
/// <param name="Upper">The finite upper endpoint, or null for a scalar.</param>
/// <param name="Position">The finite surface-local operation point.</param>
public sealed record UiSetNumericInput(UiAutomationId Target, double Value, double? Upper, Vector2 Position) : UiInput;
