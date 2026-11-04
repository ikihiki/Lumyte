using Lumyte.Input.Abstractions;

namespace Lumyte.UI.Automation;

/// <summary>A lazy chain of semantic queries, resolved against fresh state for every operation and frame.</summary>
public sealed partial class UiLocator
{
    private readonly UiQuery[] _queries;
    private readonly string _description;

    internal UiLocator(UiAutomationPage page, UiQuery[] queries)
    {
        Page = page;
        _queries = queries;
        _description = string.Join(" > ", queries.Select(query => query.ToString()));
    }

    internal UiAutomationPage Page { get; }

    internal string Description => _description;

    /// <summary>Adds an exact identity query restricted to descendants of current matches.</summary>
    /// <param name="testId">The explicit identity.</param>
    /// <returns>A new locator.</returns>
    public UiLocator GetByTestId(string testId) => new(Page, [.. _queries, UiQuery.ByTestId(testId)]);

    /// <summary>Adds a role/name query restricted to descendants of current matches.</summary>
    /// <param name="role">The non-None semantic role.</param>
    /// <param name="name">The optional exact name.</param>
    /// <param name="includeHidden">Whether to include hidden nodes.</param>
    /// <returns>A new locator.</returns>
    public UiLocator GetByRole(UiRole role, string? name = null, bool includeHidden = false) => new(Page, [.. _queries, UiQuery.ByRole(role, name, includeHidden)]);

    /// <summary>Counts current matches once, including zero or multiple matches.</summary>
    /// <param name="cancellationToken">Cancels capture.</param>
    /// <returns>The match count.</returns>
    public Task<int> CountAsync(CancellationToken cancellationToken = default) => Page.ExecuteAsync(async run => Resolve(await run.SnapshotAsync()).Count, null, cancellationToken);

    /// <summary>Waits for a stable actionable target and dispatches exactly one Move/Down/Up series.</summary>
    /// <param name="options">Optional position and shared deadline.</param>
    /// <param name="cancellationToken">Cancels waiting, never interrupts a synchronous callback.</param>
    /// <returns>The completed input series.</returns>
    public Task ClickAsync(UiClickOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (options?.Position is { } point && (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || point.X < 0 || point.Y < 0))
        {
            throw new ArgumentException("Position must be finite and nonnegative.", nameof(options));
        }

        return Page.ExecuteAsync(run => ClickAsync(run, options), options, cancellationToken);
    }

    /// <summary>Acquires focus through ordinary Tab Down/Up, sharing one deadline and input lease.</summary>
    /// <param name="options">Optional deadline.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>Completion when the latest target has focus.</returns>
    public Task FocusAsync(UiWaitOptions? options = null, CancellationToken cancellationToken = default) => Page.ExecuteAsync(run => run.WithInputAsync(lease => FocusAsync(run, lease)), options, cancellationToken);

    /// <summary>Focuses through Tab and dispatches physical modifier and key Down/Up without callback shortcuts.</summary>
    /// <param name="key">The ordinary input key.</param>
    /// <param name="modifiers">The requested modifier state.</param>
    /// <param name="options">Optional shared deadline.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The completed key series.</returns>
    public Task PressAsync(Key key, UiModifiers modifiers = UiModifiers.None, UiWaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        if ((modifiers & ~(UiModifiers.Shift | UiModifiers.Control | UiModifiers.Alt | UiModifiers.Meta)) != 0)
        {
            throw new ArgumentException("Unknown modifier bits.", nameof(modifiers));
        }

        return Page.ExecuteAsync(run => PressAsync(run, key, modifiers), options, cancellationToken);
    }

    internal List<UiAutomationNode> Resolve(UiAutomationSnapshot snapshot)
    {
        IEnumerable<UiAutomationNode> current = snapshot.Nodes;
        var parents = snapshot.Nodes.ToDictionary(node => node.Id, node => node.ParentId);
        HashSet<UiAutomationId>? scopes = null;
        foreach (UiQuery query in _queries)
        {
            current = snapshot.Nodes.Where(node => query.Matches(node) && (scopes is null || IsDescendant(node, scopes, parents))).ToArray();
            scopes = current.Select(node => node.Id).ToHashSet();
        }

        return current.ToList();
    }

    internal UiAutomationNode? Single(UiAutomationRun run, UiAutomationSnapshot snapshot)
    {
        List<UiAutomationNode> nodes = Resolve(snapshot);
        run.Trace(_description + $" matched={nodes.Count} sequence={snapshot.Sequence}");
        if (nodes.Count > 1)
        {
            throw run.Failure(UiAutomationFailureKind.AmbiguousMatch, "A strict locator matched multiple elements.");
        }

        UiAutomationNode? node = nodes.Count == 0 ? null : nodes[0];
        if (node is not null)
        {
            run.Trace("target=" + node.Id);
        }

        return node;
    }

    private static bool IsDescendant(UiAutomationNode node, HashSet<UiAutomationId> scopes, Dictionary<UiAutomationId, UiAutomationId?> parents)
    {
        for (UiAutomationId? parent = node.ParentId; parent is { } id; parent = parents[id])
        {
            if (scopes.Contains(id))
            {
                return true;
            }
        }

        return false;
    }
}
