namespace Lumyte.Diagnostics.UI.Automation.Contracts;

/// <summary>A point-in-time detached job status.</summary>
/// <param name="JobId">The JobId value.</param>
/// <param name="State">The State value.</param>
/// <param name="Code">The Code value.</param>
/// <param name="InputMayHaveBeenSent">The InputMayHaveBeenSent value.</param>
/// <param name="ResultArtifactId">The ResultArtifactId value.</param>
/// <param name="TraceArtifactId">The TraceArtifactId value.</param>
/// <param name="Revision">The Revision value.</param>
public sealed record UiRemoteJobStatus(Guid JobId, string State, string Code, bool InputMayHaveBeenSent, Guid? ResultArtifactId, Guid? TraceArtifactId, long Revision);
