using System.Globalization;

namespace Lumyte.Graphics.BenchmarkWorkloads;

/// <summary>Strict options for the dedicated retention process, independent of BenchmarkDotNet arguments.</summary>
/// <param name="Path">Canvas, Scene, or the default all-path sweep.</param>
/// <param name="SnapshotHistory">Selected budget, or null for the default 0/1/3 sweep.</param>
/// <param name="Cycles">Fixed bounded number of mutate/return cycles.</param>
public sealed record RetentionMemoryOptions(string Path, int? SnapshotHistory, int Cycles)
{
    /// <summary>Detects memory-mode flags, including incomplete/unknown memory options requiring rejection.</summary>
    /// <param name="args">Command-line tokens.</param>
    /// <returns>Whether these arguments must be handled by the strict memory parser.</returns>
    public static bool IsRequested(string[] args) => args.Any(arg => arg.StartsWith("--retained-memory", StringComparison.Ordinal) || arg.StartsWith("--memory", StringComparison.Ordinal) || arg.StartsWith("--snapshot-history", StringComparison.Ordinal));

    /// <summary>Parses one memory-mode invocation without accepting unknown or duplicate options.</summary>
    /// <param name="args">Command-line tokens, including the --retained-memory switch.</param>
    /// <returns>Validated fixed options.</returns>
    public static RetentionMemoryOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool mode = false;
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (key == "--retained-memory")
            {
                if (mode)
                {
                    throw new ArgumentException("Duplicate --retained-memory switch.", nameof(args));
                }

                mode = true;
                continue;
            }

            if (key is not ("--memory-path" or "--snapshot-history" or "--memory-cycles") || i + 1 >= args.Length || !values.TryAdd(key, args[++i]))
            {
                throw new ArgumentException("Unknown, duplicate or incomplete retained-memory option: " + key, nameof(args));
            }
        }

        if (!mode)
        {
            throw new ArgumentException("Memory options require --retained-memory.", nameof(args));
        }

        string path = values.GetValueOrDefault("--memory-path", "all");
        if (values.ContainsKey("--memory-path") && path is not ("canvas" or "scene"))
        {
            throw new ArgumentException("memory-path must be canvas or scene.", nameof(args));
        }

        int? history = null;
        if (values.TryGetValue("--snapshot-history", out string? historyText))
        {
            history = historyText switch
            {
                "0" => 0,
                "1" => 1,
                "3" => 3,
                _ => throw new ArgumentException("snapshot-history must be 0, 1 or 3.", nameof(args)),
            };
        }

        int cycles = 8;
        if (values.TryGetValue("--memory-cycles", out string? cycleText) && (!int.TryParse(cycleText, NumberStyles.None, CultureInfo.InvariantCulture, out cycles) || cycles is < 1 or > 64))
        {
            throw new ArgumentException("memory-cycles must be an integer from 1 through 64.", nameof(args));
        }

        return new(path, history, cycles);
    }
}
