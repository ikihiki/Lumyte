namespace Lumyte.UI;

/// <summary>Reports a reactive construction or delivery failure that stops normal view work.</summary>
/// <param name="innerException">The original update failure.</param>
public sealed class UiStateUpdateException(Exception innerException) : Exception("A reactive UI update failed.", innerException);
