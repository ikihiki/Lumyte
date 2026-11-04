namespace Lumyte.Diagnostics.UI.Automation;

/// <summary>A detached scalar operation receipt.</summary>
/// <param name="JobId">The JobId value.</param>
/// <param name="State">The State value.</param>
/// <param name="Code">The Code value.</param>
/// <param name="InputMayHaveBeenSent">The InputMayHaveBeenSent value.</param>
/// <param name="ResultArtifactId">The ResultArtifactId value.</param>
/// <param name="TraceArtifactId">The TraceArtifactId value.</param>
/// <param name="Revision">The Revision value.</param>
public sealed record UiAutomationJobReceipt(string JobId, string State, string Code, bool InputMayHaveBeenSent, string ResultArtifactId, string TraceArtifactId, long Revision);
