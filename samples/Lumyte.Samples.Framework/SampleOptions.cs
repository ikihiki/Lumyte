namespace Lumyte.Samples.Framework;

/// <summary>Validated launcher arguments, separate from arguments forwarded to the sample.</summary>
public sealed class SampleOptions
{
    /// <summary>Gets the requested sample.</summary>
    public string? Sample { get; private set; }

    /// <summary>Gets the requested backend.</summary>
    public string? Backend { get; private set; }

    /// <summary>Gets the build configuration.</summary>
    public string Configuration { get; private set; } = "Debug";

    /// <summary>Gets the browser server port.</summary>
    public int Port { get; private set; } = 8000;

    /// <summary>Gets a value indicating whether to list the catalog.</summary>
    public bool List { get; private set; }

    /// <summary>Gets a value indicating whether to show usage.</summary>
    public bool Help { get; private set; }

    /// <summary>Gets the unmodified sample arguments following the separator.</summary>
    public string[] Arguments { get; private set; } = [];

    /// <summary>Parses arguments, rejecting unknown, repeated, or incomplete options.</summary>
    /// <param name="args">Launcher arguments.</param>
    /// <returns>Validated options.</returns>
    public static SampleOptions Parse(string[] args)
    {
        var result = new SampleOptions();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string option = args[i];
            if (option == "--")
            {
                result.Arguments = args[(i + 1)..];
                break;
            }

            if (!seen.Add(option))
            {
                throw new ArgumentException($"Repeated option: {option}");
            }

            if (option == "--list")
            {
                result.List = true;
                continue;
            }

            if (option is "--help" or "-h")
            {
                result.Help = true;
                continue;
            }

            if (option is not ("--sample" or "--backend" or "--configuration" or "--port"))
            {
                throw new ArgumentException($"Unknown option: {option}. Sample arguments must follow --.");
            }

            if (++i == args.Length || args[i].StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(args[i]))
            {
                throw new ArgumentException($"Missing value for {option}.");
            }

            string value = args[i];
            switch (option)
            {
                case "--sample": result.Sample = value; break;
                case "--backend": result.Backend = value; break;
                case "--configuration":
                    result.Configuration = value is "Debug" or "Release" ? value : throw new ArgumentException("Configuration must be Debug or Release.");
                    break;
                case "--port":
                    result.Port = int.TryParse(value, out int port) && port is > 0 and <= 65535 ? port : throw new ArgumentException("Port must be between 1 and 65535.");
                    break;
            }
        }

        return result;
    }
}
