namespace Lumyte.Execution;

/// <summary>An immutable reusable graph with explicit typed work.</summary>
/// <typeparam name="TContext">The shared graph input type.</typeparam>
public sealed class JobGraph<TContext>
    where TContext : struct
{
    internal JobGraph(GraphNode<TContext>[] nodes, int[] incoming, int[][] outgoing, int edgeCount)
    {
        Nodes = nodes;
        Incoming = incoming;
        Outgoing = outgoing;
        EdgeCount = edgeCount;
    }

    /// <summary>Gets the node count.</summary>
    public int NodeCount => Nodes.Length;

    internal GraphNode<TContext>[] Nodes { get; }

    internal int[] Incoming { get; }

    internal int[][] Outgoing { get; }

    internal int EdgeCount { get; }
}
