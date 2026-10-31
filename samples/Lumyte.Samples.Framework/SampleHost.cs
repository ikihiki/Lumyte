using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Lumyte.Samples.Framework;

/// <summary>Runs native samples in their own process and serves published browser samples.</summary>
public static class SampleHost
{
    /// <summary>Finds the source repository from a working or executable directory.</summary>
    /// <param name="start">Directory to search upwards from.</param>
    /// <returns>The repository root.</returns>
    public static string FindRoot(string start)
    {
        for (DirectoryInfo? directory = new(start); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Lumyte.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Run the launcher from a Lumyte source checkout.");
    }

    /// <summary>Builds and launches the selected adapter, preserving the child exit code.</summary>
    /// <param name="root">Source repository root.</param>
    /// <param name="sample">Selected sample.</param>
    /// <param name="backend">Selected backend.</param>
    /// <param name="options">Build and forwarded arguments.</param>
    /// <param name="cancellationToken">Stops the child process tree or HTTP server.</param>
    /// <returns>The child exit code.</returns>
    public static async Task<int> RunAsync(string root, SampleDefinition sample, SampleBackend backend, SampleOptions options, CancellationToken cancellationToken = default)
    {
        if (!backend.Browser)
        {
            return await RunDotNetAsync(root, ["run", "--project", backend.Project, "--configuration", options.Configuration, "--", .. backend.Arguments, .. options.Arguments], cancellationToken);
        }

        if (options.Arguments.Length != 0)
        {
            throw new ArgumentException("Browser samples do not accept console arguments; use the page controls.");
        }

        string output = Path.Combine(root, "artifacts", "samples", sample.Id, backend.Id);
        int exitCode = await RunDotNetAsync(root, ["publish", backend.Project, "--configuration", options.Configuration, "--output", output], cancellationToken);
        if (exitCode != 0)
        {
            return exitCode;
        }

        string webRoot = Path.Combine(output, "wwwroot");
        if (!File.Exists(Path.Combine(webRoot, "index.html")))
        {
            throw new FileNotFoundException("Browser publish produced no wwwroot/index.html.");
        }

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = output, WebRootPath = webRoot, Args = [] });
        builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");
        await using WebApplication app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            context.Response.Headers["Cross-Origin-Embedder-Policy"] = "require-corp";
            await next(context);
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        Console.WriteLine($"Open http://127.0.0.1:{options.Port} in your browser. Ctrl+C stops the server.");
        await app.RunAsync(cancellationToken);
        return 0;
    }

    private static async Task<int> RunDotNetAsync(string root, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The child can exit between the check and Kill.
            }

            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }
}
