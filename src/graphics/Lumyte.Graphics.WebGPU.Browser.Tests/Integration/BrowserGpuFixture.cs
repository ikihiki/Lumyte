using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.WebGPU.Browser.Tests;

[CollectionDefinition("BrowserGpu", DisableParallelization = true)]
public sealed class BrowserGpuCollection : ICollectionFixture<BrowserGpuFixture> { }

public sealed class BrowserGpuFixture : IAsyncLifetime
{
    private readonly ConcurrentQueue<string> log = new();
    private readonly BrowserCdpConnection cdp = new();
    private GpuBackendTestGate? gate;
    private BrowserStaticServer? server;
    private Process? browser;
    private string sessionId = "";
    private string artifactDirectory = "";
    private int disposed;

    public JsonElement RuntimeImportRetry { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            gate = new GpuBackendTestGate();
            string webRoot = Path.Combine(AppContext.BaseDirectory, "BrowserHost", "wwwroot");
            if (!File.Exists(Path.Combine(webRoot, "index.html"))) { throw new FileNotFoundException("Build the test project before running browser conformance tests.", webRoot); }
            server = new BrowserStaticServer(webRoot);
            string browserPath = Environment.GetEnvironmentVariable("LUMYTE_WEBGPU_BROWSER")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe");
            if (!File.Exists(browserPath)) { throw new FileNotFoundException("Set LUMYTE_WEBGPU_BROWSER to a Chromium browser supporting WebGPU immediates.", browserPath); }
            artifactDirectory = Path.Combine(FindRepositoryRoot(), "artifacts", "tests", "webgpu-browser", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(artifactDirectory);
            string profile = Path.Combine(artifactDirectory, "profile");
            ProcessStartInfo start = new(browserPath)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
            };
            foreach (string argument in new[] { "--headless=new", "--no-first-run", "--no-default-browser-check", "--remote-debugging-port=0", "--user-data-dir=" + profile, "about:blank" })
            {
                start.ArgumentList.Add(argument);
            }
            TaskCompletionSource<Uri> endpoint = new(TaskCreationOptions.RunContinuationsAsynchronously);
            browser = new Process { StartInfo = start, EnableRaisingEvents = true };
            browser.ErrorDataReceived += (_, data) =>
            {
                if (data.Data is not { } line) { return; }
                log.Enqueue(line);
                const string prefix = "DevTools listening on ";
                int position = line.IndexOf(prefix, StringComparison.Ordinal);
                if (position >= 0 && Uri.TryCreate(line[(position + prefix.Length)..], UriKind.Absolute, out Uri? uri)) { endpoint.TrySetResult(uri); }
            };
            browser.OutputDataReceived += (_, data) => { if (data.Data is { } line) { log.Enqueue(line); } };
            browser.Exited += (_, _) => endpoint.TrySetException(new InvalidOperationException("Browser exited before connecting. " + string.Join(Environment.NewLine, log)));
            browser.Start();
            browser.BeginErrorReadLine();
            browser.BeginOutputReadLine();
            await cdp.ConnectAsync(await endpoint.Task.WaitAsync(TimeSpan.FromSeconds(30)));
            JsonElement version = await cdp.SendAsync("Browser.getVersion");
            await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "browser.json"), version.GetRawText());
            JsonElement target = await cdp.SendAsync("Target.createTarget", new { url = "about:blank" });
            JsonElement attached = await cdp.SendAsync("Target.attachToTarget", new { targetId = target.GetProperty("targetId").GetString(), flatten = true });
            sessionId = attached.GetProperty("sessionId").GetString()!;
            await cdp.SendAsync("Page.enable", sessionId: sessionId);
            await cdp.SendAsync("Runtime.enable", sessionId: sessionId);
            await LoadHostAsync();
            // This case must run before the runtime's module URL has been fixed by any other case.
            RuntimeImportRetry = await RunAsync("RuntimeImportRetry");
            // A rejected import must not poison unrelated cases if the retry regression fails.
            await LoadHostAsync();
        }
        catch (Exception error)
        {
            await DisposeAsync();
            throw new InvalidOperationException("Browser GPU host startup failed. " + string.Join(Environment.NewLine, log), error);
        }
    }

    private async Task LoadHostAsync()
    {
        Task loaded = cdp.ExpectEventAsync("Page.loadEventFired", sessionId);
        await cdp.SendAsync("Page.navigate", new { url = server!.BaseAddress.AbsoluteUri }, sessionId);
        await loaded;
        string runtime = await EvaluateStringAsync("(async () => (await globalThis.lumyteHost).RuntimeSmoke())()");
        if (runtime != "browser-wasm") { throw new InvalidOperationException("Conformance must execute the C# implementation inside browser WebAssembly."); }
    }

    public async Task<JsonElement> RunAsync(string scenario)
    {
        string result = await EvaluateStringAsync($"(async () => (await globalThis.lumyteHost).Run({JsonSerializer.Serialize(scenario)}))()");
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, scenario + ".json"), result);
        using JsonDocument document = JsonDocument.Parse(result);
        return document.RootElement.Clone();
    }

    private async Task<string> EvaluateStringAsync(string expression)
    {
        JsonElement result = await cdp.SendAsync("Runtime.evaluate", new { expression, awaitPromise = true, returnByValue = true }, sessionId);
        if (result.TryGetProperty("exceptionDetails", out JsonElement failure))
        {
            string details = failure.GetRawText();
            if (failure.TryGetProperty("exception", out JsonElement error) && error.TryGetProperty("objectId", out JsonElement objectId))
            {
                JsonElement expanded = await cdp.SendAsync("Runtime.callFunctionOn", new
                {
                    objectId = objectId.GetString(),
                    functionDeclaration = "function() { return String(this.message ?? this) + '\\n' + String(this.stack ?? ''); }",
                    returnByValue = true,
                }, sessionId);
                details = expanded.GetProperty("result").GetProperty("value").GetString() ?? details;
            }
            throw new InvalidOperationException("Browser C# execution failed: " + details);
        }
        return result.GetProperty("result").GetProperty("value").GetString()!;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lumyte.slnx"))) { directory = directory.Parent; }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository for browser test artifacts.");
    }

    public async Task DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) { return; }
        if (browser is not null)
        {
            try
            {
                if (!browser.HasExited)
                {
                    try { await cdp.SendAsync("Browser.close").WaitAsync(TimeSpan.FromSeconds(3)); }
                    catch (Exception) { /* The browser may close its socket before acknowledging shutdown. */ }
                    try { await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                    catch (TimeoutException) { browser.Kill(entireProcessTree: true); await browser.WaitForExitAsync(); }
                }
            }
            finally { browser.Dispose(); browser = null; }
        }
        await cdp.DisposeAsync();
        if (server is not null) { await server.DisposeAsync(); server = null; }
        if (artifactDirectory.Length != 0) { await File.WriteAllLinesAsync(Path.Combine(artifactDirectory, "browser.log"), log); }
        gate?.Dispose();
        gate = null;
    }
}
