using System.Diagnostics;
using System.Text.Json;

namespace Lumyte.UI.Application.Browser.Benchmark;

/// <summary>Owns one persistent private browser and transport, with bounded waits and teardown.</summary>
public sealed class BrowserSession : IAsyncDisposable
{
    private Process? _process;
    private Task<string>? _errors;
    private long _lastFrame;

    /// <summary>Gets the audited actual application adapter evidence.</summary>
    public string AdapterAudit { get; private set; } = string.Empty;

    /// <summary>Gets an explicit environment skip reason, or null when configured.</summary>
    /// <returns>The missing opt-in or artifact.</returns>
    public static string? GetUnavailableReason()
    {
        if (Environment.GetEnvironmentVariable("LUMYTE_UI_BENCHMARK_GPU") != "1")
        {
            return "Requires LUMYTE_UI_BENCHMARK_GPU=1; no software fallback.";
        }

        string? root = Environment.GetEnvironmentVariable("LUMYTE_UI_BENCHMARK_BROWSER_ROOT");
        string? chrome = Environment.GetEnvironmentVariable("LUMYTE_UI_BENCHMARK_CHROME");
        return root is null || !File.Exists(Path.Combine(root, "index.html")) || chrome is null || !File.Exists(chrome)
            ? "Requires published BrowserHost wwwroot and official Chrome paths." : null;
    }

    /// <summary>Starts one browser outside the BenchmarkDotNet operation.</summary>
    /// <param name="profile">The actual retained workload.</param>
    /// <returns>Completed hardware and initial-frame validation.</returns>
    public async Task StartAsync(string profile)
    {
        if (GetUnavailableReason() is { } reason)
        {
            throw new InvalidOperationException("SKIP: " + reason);
        }

        string repository = FindRepository();
        var start = new ProcessStartInfo("node") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(repository, "benchmarks/UI/Lumyte.UI.Application.Browser.Benchmark/browser-transport.mjs"));
        start.ArgumentList.Add(Environment.GetEnvironmentVariable("LUMYTE_UI_BENCHMARK_BROWSER_ROOT")!);
        start.ArgumentList.Add(Environment.GetEnvironmentVariable("LUMYTE_UI_BENCHMARK_CHROME")!);
        start.ArgumentList.Add(profile);
        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start browser transport.");
        _errors = _process.StandardError.ReadToEndAsync();
        try
        {
            using JsonDocument ready = await ReadAsync(TimeSpan.FromMinutes(3));
            AdapterAudit = ready.RootElement.GetProperty("audit").GetRawText();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    /// <summary>Includes IPC, CDP, JS dispatch, owner pacing, rendering and actual presentation completion.</summary>
    /// <returns>The newly accepted matching completed frame.</returns>
    public async Task<long> AdvanceAsync()
    {
        await _process!.StandardInput.WriteLineAsync("{\"operation\":\"frame\"}");
        await _process.StandardInput.FlushAsync();
        using JsonDocument response = await ReadAsync(TimeSpan.FromSeconds(50));
        long frame = response.RootElement.GetProperty("frame").GetInt64();
        if (frame <= _lastFrame)
        {
            throw new InvalidOperationException("A previous frame was returned.");
        }

        _lastFrame = frame;
        return frame;
    }

    /// <summary>Gets a real application GPU readback strictly outside timed operations.</summary>
    /// <returns>The PNG bytes.</returns>
    public async Task<byte[]> CaptureAsync()
    {
        await _process!.StandardInput.WriteLineAsync("{\"operation\":\"capture\"}");
        await _process.StandardInput.FlushAsync();
        using JsonDocument response = await ReadAsync(TimeSpan.FromSeconds(50));
        return Convert.FromBase64String(response.RootElement.GetProperty("png").GetString()!);
    }

    /// <summary>Reads analytic interior/background colors through actual untimed GPU readback.</summary>
    /// <returns>Four RGBA interior samples.</returns>
    public async Task<int[][]> ReadOracleAsync()
    {
        await _process!.StandardInput.WriteLineAsync("{\"operation\":\"oracle\"}");
        await _process.StandardInput.FlushAsync();
        using JsonDocument response = await ReadAsync(TimeSpan.FromSeconds(50));
        return response.RootElement.GetProperty("oracle").EnumerateArray()
            .Select(pixel => pixel.EnumerateArray().Select(component => component.GetInt32()).ToArray()).ToArray();
    }

    /// <summary>Stops the application and transport, killing owned descendants if bounded shutdown fails.</summary>
    /// <returns>Complete owned-resource teardown.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_process is not { } process)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync("{\"operation\":\"stop\"}");
                await process.StandardInput.FlushAsync();
                _ = process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(50));
            }
        }
        catch (Exception error) when (error is TimeoutException or IOException or InvalidOperationException)
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        finally
        {
            process.Dispose();
            _process = null;
        }
    }

    private static string FindRepository()
    {
        DirectoryInfo? directory = new(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tools/graphics/browser-gpu.mjs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot locate benchmark checkout.");
    }

    private async Task<JsonDocument> ReadAsync(TimeSpan timeout)
    {
        string? line = await _process!.StandardOutput.ReadLineAsync().WaitAsync(timeout);
        if (line is null)
        {
            throw new InvalidOperationException("Transport exited: " + (_errors?.IsCompleted == true ? await _errors : string.Empty));
        }

        var document = JsonDocument.Parse(line);
        if (document.RootElement.TryGetProperty("error", out JsonElement error))
        {
            string message = error.GetString()!;
            document.Dispose();
            throw new InvalidOperationException(message);
        }

        return document;
    }
}
