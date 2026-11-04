namespace Lumyte.UI.Automation;

/// <summary>Defines ui ime candidate snapshot.</summary>
/// <param name = "Token">The token.</param>
/// <param name = "Candidates">The candidates.</param>
/// <param name = "SelectedIndex">The selected index.</param>
/// <param name = "PageStarts">The page starts.</param>
/// <param name = "CurrentPage">The current page.</param>
/// <param name = "Operations">The operations.</param>
public sealed record UiImeCandidateSnapshot(UiImeCandidateToken Token, IReadOnlyList<string> Candidates, int? SelectedIndex, IReadOnlyList<int> PageStarts, int? CurrentPage, IReadOnlyList<string> Operations);
