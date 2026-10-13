namespace Lumyte.Execution;

/// <summary>Builds an immutable dependency graph with one shared typed input.</summary>
/// <typeparam name="TContext">The shared graph input type.</typeparam>
public sealed class JobGraphBuilder<TContext>
    where TContext : struct
{
    private readonly List<GraphNode<TContext>> _nodes = [];
    private readonly HashSet<(int After, int Before)> _edges = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    /// <summary>Adds unit-result work.</summary>
    /// <param name="name">The unique node name.</param>
    /// <param name="work">The CPU or owner work.</param>
    /// <returns>The node identity.</returns>
    public JobNodeId Add(string name, WorkKey<TContext, Unit> work)
    {
        Claim(name);
        WorkDefinition<TContext, Unit> definition = work.Definition ?? throw new ArgumentException("Invalid work.", nameof(work));
        if (definition.Admission == WorkAdmission.Startup)
        {
            throw new ArgumentException("Startup work cannot be scheduled as normal graph work.", nameof(work));
        }

        _nodes.Add(new CommandGraphNode<TContext>(definition));
        return new JobNodeId(this, _nodes.Count - 1);
    }

    /// <summary>Adds a disjoint CPU range split into bounded chunks.</summary>
    /// <param name="name">The unique name.</param>
    /// <param name="work">The range handler.</param>
    /// <param name="batchSize">The positive chunk size.</param>
    /// <returns>The node identity.</returns>
    public JobNodeId AddRange(string name, RangeWorkKey<TContext> work, int batchSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        Claim(name);
        RangeDefinition<TContext> definition = work.Definition ?? throw new ArgumentException("Invalid range.", nameof(work));
        _nodes.Add(new RangeGraphNode<TContext>(definition, batchSize));
        return new JobNodeId(this, _nodes.Count - 1);
    }

    /// <summary>Requires one node to succeed before another starts.</summary>
    /// <param name="after">The successor.</param>
    /// <param name="before">The predecessor.</param>
    public void DependsOn(JobNodeId after, JobNodeId before)
    {
        if (after.Builder != this || before.Builder != this || (uint)after.Index >= (uint)_nodes.Count || (uint)before.Index >= (uint)_nodes.Count || after.Index == before.Index)
        {
            throw new ArgumentException("Invalid dependency nodes.");
        }

        _edges.Add((after.Index, before.Index));
    }

    /// <summary>Validates runtime membership and acyclicity, freezing the graph.</summary>
    /// <returns>A reusable graph.</returns>
    public JobGraph<TContext> Build()
    {
        if (_nodes.Count == 0 || _nodes.Any(node => node.Runtime != _nodes[0].Runtime))
        {
            throw new InvalidOperationException("Graph must be nonempty and belong to one runtime.");
        }

        int[] incoming = new int[_nodes.Count];
        List<int>[] outgoing = Enumerable.Range(0, _nodes.Count).Select(_ => new List<int>()).ToArray();
        foreach ((int after, int before) in _edges.OrderBy(edge => edge.Before).ThenBy(edge => edge.After))
        {
            incoming[after]++;
            outgoing[before].Add(after);
        }

        int[] remaining = (int[])incoming.Clone();
        var ready = new Queue<int>(Enumerable.Range(0, remaining.Length).Where(index => remaining[index] == 0));
        int visited = 0;
        while (ready.TryDequeue(out int next))
        {
            visited++;
            foreach (int after in outgoing[next])
            {
                if (--remaining[after] == 0)
                {
                    ready.Enqueue(after);
                }
            }
        }

        if (visited != _nodes.Count)
        {
            throw new InvalidOperationException("Graph contains a dependency cycle.");
        }

        return new JobGraph<TContext>(_nodes.ToArray(), incoming, outgoing.Select(edges => edges.ToArray()).ToArray(), _edges.Count);
    }

    private void Claim(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_names.Add(name))
        {
            throw new ArgumentException("Duplicate node name.", nameof(name));
        }
    }
}
