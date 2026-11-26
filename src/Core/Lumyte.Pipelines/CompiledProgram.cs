namespace Lumyte.Pipelines;

internal sealed class CompiledProgram
{
    internal CompiledProgram(Node[] nodes)
    {
        Incoming = new int[nodes.Length];
        var outgoing = new List<int>[nodes.Length];
        for (int i = 0; i < outgoing.Length; i++)
        {
            outgoing[i] = [];
        }

        for (int i = 0; i < nodes.Length; i++)
        {
            foreach (int producer in nodes[i].Inputs.Distinct())
            {
                if (producer != 0)
                {
                    Incoming[i]++;
                    outgoing[producer - 1].Add(i);
                }
            }
        }

        Outgoing = outgoing.Select(edges => edges.ToArray()).ToArray();
    }

    internal int[] Incoming { get; }

    internal int[][] Outgoing { get; }
}
