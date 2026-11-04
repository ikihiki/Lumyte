namespace Lumyte.TextEditing;

/// <summary>Defines ime candidate snapshot.</summary>
/// <param name = "Id">The id.</param>
/// <param name = "Revision">The revision.</param>
/// <param name = "DocumentRevision">The document revision.</param>
/// <param name = "Items">The items.</param>
/// <param name = "SelectedIndex">The selected index.</param>
/// <param name = "PageStarts">The page starts.</param>
/// <param name = "CurrentPage">The current page.</param>
/// <param name = "Operations">The operations.</param>
public sealed record ImeCandidateSnapshot(ImeCandidateListId Id, ulong Revision, ulong DocumentRevision, IReadOnlyList<ImeCandidateItem> Items, int? SelectedIndex, IReadOnlyList<int> PageStarts, int? CurrentPage, ImeCandidateOperations Operations);
