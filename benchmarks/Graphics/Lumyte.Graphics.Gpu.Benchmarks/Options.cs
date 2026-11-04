using System.Globalization;

namespace Lumyte.Graphics.Gpu.Benchmarks;

internal sealed record Options(string Phase, string Update, string Locality, int Granularity, bool Retained, int Depth, int Frames, int Warmup, string Output)
{
    internal static Options Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] allowed = ["phase", "update", "locality", "granularity", "path", "depth", "frames", "warmup", "output"];
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length || !allowed.Contains(args[i][2..]) || !values.TryAdd(args[i][2..], args[i + 1]))
            {
                throw new ArgumentException("Unknown, duplicate or incomplete option: " + args[i]);
            }
        }

        string Get(string key, string fallback) => values.GetValueOrDefault(key, fallback);
        int Number(string key, int fallback) => int.Parse(Get(key, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        string phase = Get("phase", "smoke");
        if (phase is not ("smoke" or "cold" or "pilot" or "formal"))
        {
            throw new ArgumentException("phase must be smoke, cold, pilot or formal.");
        }

        string update = Get("update", "static"), locality = Get("locality", "contiguous"), path = Get("path", "scene");
        _ = new BenchmarkWorkloads.RectangleTrace(update, locality);
        int granularity = Number("granularity", 64), depth = Number("depth", 1);
        BenchmarkWorkloads.RectangleTrace.ValidateGranularity(granularity);
        if (depth is not (1 or 3) || path is not ("canvas" or "scene"))
        {
            throw new ArgumentException("depth must be 1 or 3; path must be canvas or scene.");
        }

        int frames = Number("frames", phase == "formal" ? 2000 : phase == "pilot" ? 2000 : phase == "cold" ? 1 : 4);
        int warmup = Number("warmup", phase == "formal" ? 200 : 0);
        if (frames < 1 || frames > 1000000 || warmup < 0 || warmup > 2000 || (phase == "formal" && (frames < 2000 || warmup < 200)) || (phase == "pilot" && (frames < 200 || warmup != 0)) || (phase == "cold" && (frames != 1 || warmup != 0)))
        {
            throw new ArgumentException("Invalid frame/warmup budget for phase.");
        }

        return new(phase, update, locality, granularity, path == "scene", depth, frames, warmup, Get("output", "artifacts/graphics-benchmarks/" + Guid.NewGuid().ToString("N")));
    }
}
