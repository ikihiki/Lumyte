using Spectre.Console;

namespace Lumyte.Samples.Framework;

/// <summary>Resolves explicit selections and prompts only for missing selections.</summary>
public static class SampleSelector
{
    /// <summary>Validates a supplied value or prompts for a missing value.</summary>
    /// <param name="value">Explicit selection, if supplied.</param>
    /// <param name="choices">Supported values.</param>
    /// <param name="title">Selection label.</param>
    /// <returns>The selected value.</returns>
    public static string SelectValue(string? value, IEnumerable<string> choices, string title)
    {
        string[] values = choices.ToArray();
        return value == null ? Choose(values, title, item => item, AnsiConsole.Console)
            : values.Contains(value, StringComparer.Ordinal) ? value
            : throw new ArgumentException($"Unknown {title}: {value}. Choose {string.Join(", ", values)}.");
    }

    /// <summary>Selects an available sample and backend.</summary>
    /// <param name="samples">Registered samples.</param>
    /// <param name="options">Requested selections.</param>
    /// <param name="console">Console used for interactive selection.</param>
    /// <returns>The selected sample and backend.</returns>
    public static (SampleDefinition Sample, SampleBackend Backend) Select(IReadOnlyList<SampleDefinition> samples, SampleOptions options, IAnsiConsole console)
    {
        SampleDefinition[] candidates = samples.Where(s => s.Backends.Any(b => b.IsAvailable && (options.Backend == null || b.Id == options.Backend))).ToArray();
        SampleDefinition sample = options.Sample == null
            ? Choose(candidates, "Sample", s => $"{s.Id} — {s.Role}", console)
            : samples.SingleOrDefault(s => s.Id == options.Sample) ?? throw new ArgumentException($"Unknown sample: {options.Sample}. Use --list.");
        SampleBackend[] backends = sample.Backends.Where(b => b.IsAvailable).ToArray();
        SampleBackend backend = options.Backend == null
            ? Choose(backends, "Backend", b => b.Id, console)
            : backends.SingleOrDefault(b => b.Id == options.Backend) ?? throw new ArgumentException($"Backend '{options.Backend}' is unsupported or unavailable for '{sample.Id}'. Use --list.");
        return (sample, backend);
    }

    private static T Choose<T>(T[] choices, string title, Func<T, string> label, IAnsiConsole console)
        where T : notnull
    {
        if (choices.Length == 0)
        {
            throw new ArgumentException($"No available {title.ToLowerInvariant()} matches the selection. Use --list.");
        }

        if (choices.Length == 1)
        {
            return choices[0];
        }

        if (!console.Profile.Capabilities.Interactive)
        {
            throw new ArgumentException($"Missing {title.ToLowerInvariant()} in a non-interactive terminal. Supply it explicitly. Choices: {string.Join(", ", choices.Select(label))}.");
        }

        return console.Prompt(new SelectionPrompt<T>().Title(title).PageSize(15).UseConverter(value => Markup.Escape(label(value))).AddChoices(choices));
    }
}
