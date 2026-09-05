namespace Lumyte.Input;

public sealed record TextCandidateList
{
    public required IReadOnlyList<string> Items { get; init; }

    public required int SelectedIndex { get; init; }

    public required int PageStart { get; init; }

    public required int PageSize { get; init; }
}
