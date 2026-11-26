namespace Lumyte.Pipelines;

internal sealed class CompiledSegments
{
    internal CompiledSegments(Node[] nodes)
    {
        int[] consumers = new int[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            foreach (int producer in nodes[i].Inputs.Distinct())
            {
                if (producer != 0)
                {
                    consumers[producer - 1]++;
                }
            }
        }

        var segments = new List<List<int>>();
        int[] mapping = new int[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            int[] dependencies = nodes[i].Inputs.Where(input => input != 0).Distinct().ToArray();
            if (dependencies.Length == 1 && consumers[dependencies[0] - 1] == 1)
            {
                int segment = mapping[dependencies[0] - 1];
                mapping[i] = segment;
                segments[segment].Add(i);
            }
            else
            {
                mapping[i] = segments.Count;
                segments.Add([i]);
            }
        }

        Nodes = segments.Select(segment => segment.ToArray()).ToArray();
        Dependencies = new int[Nodes.Length][];
        for (int i = 0; i < Nodes.Length; i++)
        {
            Dependencies[i] = nodes[Nodes[i][0]].Inputs.Where(input => input != 0).Select(input => mapping[input - 1]).Distinct().ToArray();
        }
    }

    internal int[][] Nodes { get; }

    internal int[][] Dependencies { get; }
}
