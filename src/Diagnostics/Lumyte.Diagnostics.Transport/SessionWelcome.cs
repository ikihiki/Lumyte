namespace Lumyte.Diagnostics.Transport;

/// <summary>The authenticated session and granted capabilities.</summary>
/// <param name="SessionId">The SessionId value.</param>
/// <param name="SessionSecret">The SessionSecret value.</param>
/// <param name="Permissions">The Permissions value.</param>
public sealed record SessionWelcome(Guid SessionId, string SessionSecret, DiagnosticPermission[] Permissions);
