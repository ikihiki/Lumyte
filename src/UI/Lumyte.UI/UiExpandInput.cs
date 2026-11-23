using System.Numerics;

namespace Lumyte.UI;

/// <summary>Requests desired expansion through ordinary prepared header input.</summary>
/// <param name="Target">The mounted owner.</param>
/// <param name="IsExpanded">The desired state.</param>
/// <param name="Position">The finite surface-local header point.</param>
public sealed record UiExpandInput(UiAutomationId Target, bool IsExpanded, Vector2 Position) : UiInput;
