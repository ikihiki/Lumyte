using System.Numerics;

namespace Lumyte.UI;

/// <summary>Requests a desired checked state through prepared normal input geometry.</summary>
/// <param name="Target">The current semantic owner.</param>
/// <param name="IsChecked">The desired checked state.</param>
/// <param name="Position">The finite surface-local action point.</param>
public sealed record UiCheckInput(UiAutomationId Target, bool IsChecked, Vector2 Position) : UiInput;
