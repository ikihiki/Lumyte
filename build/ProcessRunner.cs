using System.Diagnostics;
using System.Text;

namespace Lumyte.Build;

internal static class ProcessRunner
{
    public static async Task<string> RunAsync(string tool, IEnumerable<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string?>? environment = null, bool captureOutput = false)
    {
        var start = new ProcessStartInfo(tool)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach ((string key, string? value) in environment)
            {
                if (value is null)
                {
                    start.Environment.Remove(key);
                }
                else
                {
                    start.Environment[key] = value;
                }
            }
        }

        Console.WriteLine($"Running: {tool} {string.Join(' ', start.ArgumentList.Select(argument => argument.Contains(' ') ? $"\"{argument}\"" : argument))}");
        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start {tool}.");
        }

        var output = new StringBuilder();
        var errors = new StringBuilder();
        void Cancel(object? sender, ConsoleCancelEventArgs args)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        Console.CancelKeyPress += Cancel;
        try
        {
            Task stdout = ReadAsync(process.StandardOutput, output, captureOutput ? TextWriter.Null : Console.Out);
            Task stderr = ReadAsync(process.StandardError, errors, Console.Error);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync()).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"{Path.GetFileName(tool)} failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{errors}");
            }

            return output.ToString();
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
        }
    }

    private static async Task ReadAsync(StreamReader reader, StringBuilder? output, TextWriter destination)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is string line)
        {
            if (output is not null)
            {
                output.AppendLine(line);
            }
            await destination.WriteLineAsync(line).ConfigureAwait(false);
        }
    }
}
