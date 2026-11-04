namespace Lumyte.UI.Navigation;

/// <summary>Approves leaving, or rejects it with a short fixed code.</summary>
/// <param name="Allowed">The Allowed value.</param>
/// <param name="Code">The Code value.</param>
public sealed record UiPageLeaveDecision(bool Allowed, string Code);
