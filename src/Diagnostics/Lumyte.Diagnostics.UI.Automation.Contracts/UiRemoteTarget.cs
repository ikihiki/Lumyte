namespace Lumyte.Diagnostics.UI.Automation.Contracts;

/// <summary>A detached registered surface description.</summary>
/// <param name="Id">The Id value.</param>
/// <param name="Name">The Name value.</param>
/// <param name="AllowInput">The AllowInput value.</param>
/// <param name="AllowScreenshots">The AllowScreenshots value.</param>
public sealed record UiRemoteTarget(Guid Id, string Name, bool AllowInput, bool AllowScreenshots);
