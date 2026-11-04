using System.Globalization;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Presentation.Benchmarks;

internal sealed record Options(string Command, string Scenario, int Frames, int Warmup, int Depth, int IdleMs, PresentMode Mode, double RefreshHz)
{
    internal static Options Parse(string[] args)
    {
        string[] commands = ["--list", "--validate-options", "--check-schedule", "--check-pixels", "--run"];
        if (args.Length == 0 || !commands.Contains(args[0]))
        {
            throw new ArgumentException("First argument: --list | --validate-options | --check-schedule | --check-pixels | --run.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] allowed = ["--scenario", "--frames", "--warmup", "--depth", "--idle-ms", "--present-mode", "--refresh-hz"];
        for (int i = 1; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length || !allowed.Contains(args[i]) || !values.TryAdd(args[i], args[i + 1]))
            {
                throw new ArgumentException("Unknown, missing, or duplicate option: " + args[i]);
            }
        }

        if ((args[0] is "--list" or "--check-schedule") && values.Count != 0)
        {
            throw new ArgumentException(args[0] + " does not accept options.");
        }

        if (args[0] == "--check-pixels" && values.Keys.Any(key => key != "--frames"))
        {
            throw new ArgumentException("--check-pixels accepts only --frames; U03 and headless target are fixed.");
        }

        string Get(string key, string fallback) => values.GetValueOrDefault(key, fallback);
        int Number(string key, int fallback, int min, int max)
        {
            if (!int.TryParse(Get(key, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < min || n > max)
            {
                throw new ArgumentException($"{key} must be an integer in [{min},{max}].");
            }

            return n;
        }

        string scenario = Get("--scenario", "u03-forced-draw");
        if (scenario is not ("forced-clear" or "u03-forced-draw" or "benchmark-idle-u03"))
        {
            throw new ArgumentException("Unsupported scenario.");
        }

        PresentMode mode = Get("--present-mode", "fifo") switch
        {
            "fifo" => PresentMode.Fifo,
            "mailbox" => PresentMode.Mailbox,
            "immediate" => PresentMode.Immediate,
            _ => throw new ArgumentException("Present mode must be fifo, mailbox, or immediate."),
        };
        if (!double.TryParse(Get("--refresh-hz", "60"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double refresh) || !double.IsFinite(refresh) || refresh < 1 || refresh > 1000)
        {
            throw new ArgumentException("Refresh assumption must be finite in [1,1000] Hz.");
        }

        int depth = Number("--depth", 1, 1, 3);
        if (depth is not (1 or 3))
        {
            throw new ArgumentException("--depth must be exactly 1 or 3.");
        }

        return new(args[0], scenario, Number("--frames", 120, 2, 100000), Number("--warmup", 8, 0, 10000), depth, Number("--idle-ms", 1000, 1, 60000), mode, refresh);
    }
}
