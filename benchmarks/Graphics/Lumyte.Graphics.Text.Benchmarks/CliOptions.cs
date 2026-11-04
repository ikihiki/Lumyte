namespace Lumyte.Graphics.Text.Benchmarks;

internal static class CliOptions
{
    internal static string Validate(string[] args)
    {
        int cpuCount = args.Count(argument => argument == "--cpu");
        if (cpuCount > 0)
        {
            if (cpuCount != 1 || args.Any(argument => argument is "--check-layouts" or "--smoke" or "--run"))
            {
                throw new ArgumentException("Use --cpu exactly once and without another runner mode.");
            }

            // --list (including its value) and all other BDN options remain passthrough.
            return "--cpu";
        }

        string[] modes = ["--list", "--check-layouts", "--smoke", "--run"];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? mode = null;
        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            if (!seen.Add(option))
            {
                throw new ArgumentException($"Duplicate option: {option}.");
            }

            if (modes.Contains(option, StringComparer.Ordinal))
            {
                if (mode is not null)
                {
                    throw new ArgumentException("Choose exactly one runner mode.");
                }

                mode = option;
                continue;
            }

            if (option is not ("--case" or "--method" or "--source" or "--output" or "--depth" or "--warmup" or "--frames"))
            {
                throw new ArgumentException($"Unknown option: {option}.");
            }

            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Missing value for {option}.");
            }

            index++;
        }

        if (mode is null)
        {
            throw new ArgumentException("Choose exactly one of --list, --check-layouts, --smoke, --run.");
        }

        foreach (string option in seen)
        {
            bool compatible = option switch
            {
                "--case" => mode is "--smoke" or "--run",
                "--method" => mode is "--smoke" or "--run",
                "--source" => mode is "--smoke" or "--run",
                "--output" => mode is "--smoke" or "--run",
                "--depth" => mode is "--run",
                "--warmup" => mode is "--run",
                "--frames" => mode is "--run",
                _ => true,
            };
            if (!compatible)
            {
                throw new ArgumentException($"{option} is not supported with {mode}.");
            }
        }

        return mode;
    }
}
