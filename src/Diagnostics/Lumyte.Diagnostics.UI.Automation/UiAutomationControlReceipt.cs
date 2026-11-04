namespace Lumyte.Diagnostics.UI.Automation;

/// <summary>A detached scalar operation receipt.</summary>
/// <param name="ControlId">The ControlId value.</param>
/// <param name="State">The State value.</param>
/// <param name="RemainingMilliseconds">The RemainingMilliseconds value.</param>
/// <param name="Revision">The Revision value.</param>
/// <param name="NextSequence">The NextSequence value.</param>
public sealed record UiAutomationControlReceipt(string ControlId, string State, long RemainingMilliseconds, long Revision, long NextSequence);
