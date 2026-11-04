namespace Lumyte.UI.Automation;

internal sealed record UiQuery(string? TestId, UiRole? Role, string? Name, bool IncludeHidden)
{
    internal static UiQuery ByTestId(string testId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testId);
        return new(testId, null, null, true);
    }

    internal static UiQuery ByRole(UiRole role, string? name, bool includeHidden)
    {
        if (role == UiRole.None)
        {
            throw new ArgumentException("None is not a searchable role.", nameof(role));
        }

        return new(null, role, name, includeHidden);
    }

    internal bool Matches(UiAutomationNode node) =>
        (TestId is null || string.Equals(TestId, node.TestId, StringComparison.Ordinal))
        && (Role is null || node.Role == Role)
        && (Name is null || string.Equals(Name, node.Name, StringComparison.Ordinal))
        && (IncludeHidden || node.IsVisible);
}
