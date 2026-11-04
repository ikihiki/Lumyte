namespace Lumyte.Graphics.UiScenarios;

internal static class CliOptions
{
    internal static string Validate(string[] args)
    {
        string[] modes = ["--list", "--unit", "--smoke", "--gpu"];
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

            if (option is not ("--case" or "--variant" or "--depth" or "--warmup" or "--frames"))
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
            throw new ArgumentException("Choose exactly one of --list, --unit, --smoke, --gpu.");
        }

        foreach (string option in seen)
        {
            bool compatible = option switch
            {
                "--case" => mode is "--smoke" or "--gpu",
                "--variant" => mode is "--smoke" or "--gpu",
                "--depth" => mode is "--gpu",
                "--warmup" => mode is "--gpu",
                "--frames" => mode is "--gpu",
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
