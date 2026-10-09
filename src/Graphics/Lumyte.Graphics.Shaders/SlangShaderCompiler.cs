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
        if ((desc.Target is { } requested && !Enum.IsDefined(requested)) || !Enum.IsDefined(desc.Stage))
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
            await File.WriteAllTextAsync(source, ShaderSourcePreparation.Prepare(desc.Source), cancellationToken);
            using Stream helper = typeof(SlangShaderCompiler).Assembly.GetManifestResourceStream("Lumyte.Shaders.lumyte.slang")!;
            using (FileStream helperFile = File.Create(Path.Combine(directory, "lumyte.slang")))
            {
                await helper.CopyToAsync(helperFile, cancellationToken);
            }

            string compilerVersion = (await RunCompilerAsync(["-version"], cancellationToken)).Trim();
            ShaderTarget[] targets = desc.Target is { } selected ? [selected] : Enum.GetValues<ShaderTarget>();
            var results = new List<ShaderTargetData>();
            foreach (ShaderTarget target in targets)
            {
                string[] arguments = [source, "-entry", desc.EntryPoint, "-stage", desc.Stage.ToString().ToLowerInvariant(), "-target", target == ShaderTarget.Wgsl ? "wgsl" : "spirv", "-matrix-layout-row-major", target == ShaderTarget.SpirV ? "-DLUMYTE_SPIRV=1" : "-DLUMYTE_WGSL=1", "-I", directory, "-reflection-json", reflection, "-o", output];
                _ = await RunCompilerAsync(arguments, cancellationToken);
                byte[] code = await File.ReadAllBytesAsync(output, cancellationToken);
                string reflectionJson = ShaderSourcePreparation.CompleteReflection(await File.ReadAllTextAsync(reflection, cancellationToken), desc.Source);
                results.Add(new(target, desc.Stage, desc.EntryPoint, compilerVersion, "row-major", code, reflectionJson));
            }

            return ShaderArtifact.PackTargets(results);
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
