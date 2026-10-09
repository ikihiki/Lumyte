using System.Diagnostics;
using System.Runtime.Versioning;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shaders;

/// <summary>Compiles Slang source using the installed slangc executable.</summary>
/// <param name="compilerPath">The executable path or command resolved from PATH.</param>
[UnsupportedOSPlatform("browser")]
public sealed class SlangShaderCompiler(string compilerPath = "slangc") : IShaderCompiler
{
    /// <inheritdoc />
    public async Task<ShaderArtifact> CompileAsync(ShaderCompilationDesc desc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(desc);
        ArgumentException.ThrowIfNullOrWhiteSpace(desc.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(desc.EntryPoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(compilerPath);
        if (!Enum.IsDefined(desc.Target) || !Enum.IsDefined(desc.Stage))
        {
            throw new ArgumentException("Unknown shader target or stage.", nameof(desc));
        }

        cancellationToken.ThrowIfCancellationRequested();
        string directory = Path.Combine(Path.GetTempPath(), $"lumyte-shader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "shader.slang");
            string output = Path.Combine(directory, "shader.bin");
            string reflection = Path.Combine(directory, "reflection.json");
            await File.WriteAllTextAsync(source, desc.Source, cancellationToken);
            string[] arguments = [source, "-entry", desc.EntryPoint, "-stage", desc.Stage.ToString().ToLowerInvariant(), "-target", desc.Target == ShaderTarget.Wgsl ? "wgsl" : "spirv", "-matrix-layout-row-major", "-reflection-json", reflection, "-o", output];
            _ = await RunCompilerAsync(arguments, cancellationToken);
            byte[] code = await File.ReadAllBytesAsync(output, cancellationToken);
            string reflectionJson = await File.ReadAllTextAsync(reflection, cancellationToken);
            string compilerVersion = await RunCompilerAsync(["-version"], cancellationToken);
            return ShaderArtifact.PackTarget(desc.Target, desc.Stage, desc.EntryPoint, compilerVersion.Trim(), code, reflectionJson);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private async Task<string> RunCompilerAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(compilerPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start slangc.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                }
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }

        string diagnostics = (await stderr) + (await stdout);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Slang compilation failed ({process.ExitCode}): {diagnostics}");
        }

        return diagnostics;
    }
}
