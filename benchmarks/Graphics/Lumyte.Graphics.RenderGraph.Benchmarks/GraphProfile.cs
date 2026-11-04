namespace Lumyte.Graphics.RenderGraph.Benchmarks;

internal sealed record GraphProfile(string Id, string Shape, int Passes, int UnusedPercent, int Lanes)
{
    internal int Unused => Passes * UnusedPercent / 100;

    internal int Live => Passes - Unused;

    internal int Outputs => Shape == "multioutput" ? 4 : 1;

    internal int Edges => Shape == "fanout" ? (2 * Live) - 4 : Live - Outputs;

    internal static IReadOnlyList<GraphProfile> All { get; } =
    [
        new("chain-p32-r32-u0", "chain", 32, 0, 1),
        new("chain-p128-r128-u0", "chain", 128, 0, 1),
        new("fanout-p32-r32-u0", "fanout", 32, 0, 1),
        new("fanout-p128-r128-u0", "fanout", 128, 0, 1),
        new("multioutput-p32-r32-u0", "multioutput", 32, 0, 1),
        new("multioutput-p128-r128-u0", "multioutput", 128, 0, 1),
        new("chain-p128-r128-u25", "chain", 128, 25, 1),
        new("chain-p128-r128-u50", "chain", 128, 50, 1),
        new("fanout-p128-r128-u50", "fanout", 128, 50, 1),
        new("multioutput-p128-r128-u50", "multioutput", 128, 50, 1),
        new("chain-p128-r256-u0", "chain", 128, 0, 2),
    ];

    internal static GraphProfile Find(string id) => All.Single(profile => profile.Id == id);

    internal int[] Inputs(int pass)
    {
        if (pass >= Live || pass == 0)
        {
            return [];
        }

        return Shape switch
        {
            "chain" => [pass - 1],
            "multioutput" => pass < 4 ? [] : [pass - 4],
            "fanout" => pass == Live - 1 ? Enumerable.Range(1, Live - 2).ToArray() : [0],
            _ => throw new InvalidOperationException("Unknown graph shape."),
        };
    }

    internal bool IsOutput(int pass) => pass < Live && pass >= Live - Outputs;
}
