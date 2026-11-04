namespace Lumyte.Diagnostics.UI.Automation.Contracts;

/// <summary>An explicit candidate list identity; 64-bit counters travel as decimal strings.</summary>
public sealed record UiRemoteCandidateToken(Guid SessionId, string SessionGeneration, string ElementGeneration, string Revision);
