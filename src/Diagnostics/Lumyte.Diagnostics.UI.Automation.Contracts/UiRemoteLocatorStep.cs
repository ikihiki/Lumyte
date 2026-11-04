namespace Lumyte.Diagnostics.UI.Automation.Contracts;

/// <summary>One closed semantic query step.</summary>
/// <param name="Kind">The Kind value.</param>
/// <param name="Value">The Value value.</param>
/// <param name="Role">The Role value.</param>
/// <param name="Name">The Name value.</param>
/// <param name="IncludeHidden">The IncludeHidden value.</param>
public sealed record UiRemoteLocatorStep(string Kind, string? Value = null, string? Role = null, string? Name = null, bool IncludeHidden = false);
