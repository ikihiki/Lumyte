using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Lumyte.Build;

// The general ProcessRunner deliberately has no timeout. Screening needs a single
// shared deadline, disk logs, and tree termination, not a timeout per command.
internal sealed class ComponentScreeningProcess(string output, double seconds)
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly string _output = output;
    private readonly double _seconds = Math.Min(seconds, 900);

    public List<object> Commands { get; } = [];

    public double ElapsedSeconds => _clock.Elapsed.TotalSeconds;

    public double LimitSeconds => _seconds;

    public async Task<string> CallAsync(string tool, IEnumerable<string> arguments, string directory, string label, IReadOnlyDictionary<string, string?>? environment = null, bool trimOutput = true)
    {
        double remaining = _seconds - ElapsedSeconds;
        if (remaining <= 0 || !double.IsFinite(remaining))
        {
            throw new ScreeningFailure("timeout", "Total execution deadline exhausted");
        }

        string[] args = arguments.ToArray();
        string log = Path.Combine(_output, label + ".log");
        Commands.Add(new { command = new[] { tool }.Concat(args).ToArray(), cwd = directory, log = Path.GetFileName(log) });
        bool grouped = OperatingSystem.IsLinux() && File.Exists("/usr/bin/setsid");
        var start = new ProcessStartInfo(grouped ? "/usr/bin/setsid" : tool) { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if (grouped)
        {
            start.ArgumentList.Add("--wait");
            start.ArgumentList.Add(tool);
        }

        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        if (environment is not null)
        {
            foreach ((string key, string? value) in environment)
            {
                start.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = start };
        using var stream = new StreamWriter(log, append: false) { AutoFlush = true };
        var captured = new StringBuilder();
        using var gate = new SemaphoreSlim(1);
        using var cancellation = new CancellationTokenSource();
        async Task DrainAsync(StreamReader reader, bool capture)
        {
            while (await reader.ReadLineAsync(cancellation.Token).ConfigureAwait(false) is string line)
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    await stream.WriteLineAsync(line).ConfigureAwait(false);
                    if (capture)
                    {
                        captured.AppendLine(line);
                    }
                }
                finally
                {
                    gate.Release();
                }
            }
        }

        process.Start();
        Task stdout = DrainAsync(process.StandardOutput, true);
        Task stderr = DrainAsync(process.StandardError, false);
        try
        {
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(remaining)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (grouped)
            {
                // setsid gives descendants an owned session even after the direct child exits.
                KillProcessGroup(-process.Id, 9);
            }

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await cancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Read cancellation closes our ownership of inherited pipes even if an orphan retains them.
            }

            throw new ScreeningFailure("timeout", "Deadline exceeded in " + label + "; partial artifacts retained");
        }

        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new ScreeningFailure("execution_error", label + " exited " + process.ExitCode + "; see " + Path.GetFileName(log));
        }

        string result = captured.ToString();
        return trimOutput ? result.Trim() : result.TrimEnd('\r', '\n');
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int KillProcessGroup(int pid, int signal);
}
