using System.Globalization;

namespace Lumyte.Graphics.Gpu.Benchmarks;

internal sealed record SuiteOptions(string Scenario, string Resource, string Updates, string Order, int Depth, int Frames, string Output)
{
    internal static SuiteOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] allowed = ["scenario", "resource", "updates", "order", "depth", "frames", "output"];
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal) || !allowed.Contains(args[i][2..]) || !values.TryAdd(args[i][2..], args[i + 1]))
            {
                throw new ArgumentException("Unknown, duplicate or incomplete option.");
            }
        }

        string scenario = values.GetValueOrDefault("scenario", "D06");
        string resource = values.GetValueOrDefault("resource", scenario == "D06" ? "atlas" : "buffer");
        string updates = values.GetValueOrDefault("updates", scenario == "D06" ? "resident" : "contiguous");
        string order = values.GetValueOrDefault("order", "alternating");
        int depth = int.Parse(values.GetValueOrDefault("depth", "1"), CultureInfo.InvariantCulture);
        int frames = int.Parse(values.GetValueOrDefault("frames", "6"), CultureInfo.InvariantCulture);
        if (depth is not (1 or 3) || frames is < 3 or > 100000 || order is not ("alternating" or "grouped"))
        {
            throw new ArgumentException("Require depth1/3, frames3..100000 and alternating/grouped order.");
        }

        bool valid = scenario switch
        {
            "D06" => (resource is "atlas" or "distinct") && (updates is "resident" or "upload"),
            "G03" => (resource is "buffer" or "texture") && (updates is "contiguous" or "dispersed") && !values.ContainsKey("order"),
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException("D06 requires atlas/distinct + resident/upload; G03 buffer/texture + contiguous/dispersed, no order option.");
        }

        return new(scenario, resource, updates, order, depth, frames, values.GetValueOrDefault("output", "artifacts/graphics-benchmarks/" + Guid.NewGuid().ToString("N")));
    }
}
