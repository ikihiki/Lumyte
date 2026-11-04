namespace Lumyte.Diagnostics.UI.Automation.Contracts;

/// <summary>Opt-in text inspection of one detached candidate list.</summary>
public sealed record UiRemoteCandidates(UiRemoteCandidateToken Token, string[] Candidates, int? SelectedIndex, int[] PageStarts, int? CurrentPage, string[] Operations);
