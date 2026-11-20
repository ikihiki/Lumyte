using System.Numerics;

namespace Lumyte.UI;

/// <summary>Ordinary lease-owned realization request addressed to an actual item owner.</summary>
/// <param name="Target">The mounted group identity.</param>
/// <param name="Key">The nonempty ordinal model key, at most 1024 characters.</param>
/// <param name="Position">The finite actionable group point.</param>
public sealed record UiBringItemInput(UiAutomationId Target, string Key, Vector2 Position) : UiInput
{
    /// <summary>Gets the optional ordinal column key for an actual grid viewport.</summary>
    public string? ColumnKey { get; init; }
}
