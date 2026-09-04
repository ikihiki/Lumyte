using System.Diagnostics;
using System.Text.Json;

using Lumyte.Graphics;
using Lumyte.Graphics.Shader;

namespace Lumyte.Graphics.Shader.Offline;

public static class SlangPackageCompiler
{
    private static readonly (GpuShaderCodeFormat Format, string Target, string Profile, string Capability)[] s_targets =
    [
        (GpuShaderCodeFormat.Dxil, "dxil", "sm_6_0", "direct3d12"),
        (GpuShaderCodeFormat.SpirV, "spirv", "sm_6_0", "vulkan1.2"),
        (GpuShaderCodeFormat.Wgsl, "wgsl", string.Empty, "webgpu-experimental"),
    ];

    public static async Task CompileAsync(string compiler, string source, string output, CancellationToken cancellationToken = default)
    {
        CopyDxcBesideCompiler(compiler);
        string temporaryDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, $"slang-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            IReadOnlyList<SlangEntryPoint> entryPoints = await DiscoverEntryPointsAsync(
                compiler, source, temporaryDirectory, cancellationToken);
            var artifacts = new List<GpuShaderArtifactSource>();
            foreach ((GpuShaderCodeFormat format, string target, string profile, string capability) in s_targets)
            {
                foreach (SlangEntryPoint entryPoint in entryPoints)
                {
                    string effectiveProfile = entryPoint.Stage == GpuShaderStage.Mesh && profile.Length > 0 ? "sm_6_5" : profile;
                    string artifactPath = Path.Combine(temporaryDirectory, $"{format}-{entryPoint.Stage}-{entryPoint.Name}.bin");
                    await CompileEntryPointAsync(compiler, source, artifactPath, target, effectiveProfile, entryPoint, cancellationToken);
                    artifacts.Add(new(format, entryPoint.Stage, entryPoint.Name, target, effectiveProfile, capability,
                        GpuShaderBindingConvention.AbiHash, await File.ReadAllBytesAsync(artifactPath, cancellationToken)));
                }
            }

            byte[] package = GpuShaderPackageWriter.Write(artifacts);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            byte[]? existing = File.Exists(output) ? await File.ReadAllBytesAsync(output, cancellationToken) : null;
            if (existing is null || !package.AsSpan().SequenceEqual(existing))
            {
                await File.WriteAllBytesAsync(output, package, cancellationToken);
            }
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static async Task<IReadOnlyList<SlangEntryPoint>> DiscoverEntryPointsAsync(
        string compiler,
        string source,
        string temporaryDirectory,
        CancellationToken cancellationToken)
    {
        string reflectionPath = Path.Combine(temporaryDirectory, "reflection.json");
        string modulePath = Path.Combine(temporaryDirectory, "reflection.spv");
        await RunAsync(compiler,
            [source, "-target", "spirv", "-reflection-json", reflectionPath, "-o", modulePath],
            "entry-point discovery", cancellationToken);

        await using FileStream stream = File.OpenRead(reflectionPath);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var result = new List<SlangEntryPoint>();
        foreach (JsonElement value in document.RootElement.GetProperty("entryPoints").EnumerateArray())
        {
            string name = value.GetProperty("name").GetString()
                ?? throw new InvalidDataException("Slang returned an entry point without a name.");
            string stageName = value.GetProperty("stage").GetString()
                ?? throw new InvalidDataException($"Slang returned no stage for {name}.");
            result.Add(new(name, ParseStage(stageName), stageName));
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException($"{Path.GetFileName(source)} does not declare a [shader] entry point.");
        }

        return result;
    }

    private static GpuShaderStage ParseStage(string stage) => stage switch
    {
        "vertex" => GpuShaderStage.Vertex,
        "fragment" => GpuShaderStage.Pixel,
        "compute" => GpuShaderStage.Compute,
        "mesh" => GpuShaderStage.Mesh,
        _ => throw new NotSupportedException($"Slang shader stage '{stage}' is not supported by GpuShaderPackage."),
    };

    private static void CopyDxcBesideCompiler(string compiler)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string toolDirectory = AppContext.BaseDirectory;
        string compilerDirectory = Path.GetDirectoryName(Path.GetFullPath(compiler))!;
        foreach (string fileName in new[] { "dxcompiler.dll", "dxil.dll" })
        {
            string source = Path.Combine(toolDirectory, fileName);
            string destination = Path.Combine(compilerDirectory, fileName);
            if (File.Exists(source) && !File.Exists(destination))
            {
                File.Copy(source, destination);
            }
        }
    }

    private static Task CompileEntryPointAsync(
        string compiler,
        string source,
        string output,
        string target,
        string profile,
        SlangEntryPoint entryPoint,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            source, "-entry", entryPoint.Name, "-stage", entryPoint.SlangStage, "-target", target, "-o", output,
        };
        if (profile.Length > 0)
        {
            arguments.Add("-profile");
            arguments.Add(profile);
        }

        return RunAsync(compiler, arguments, $"{target}/{entryPoint.SlangStage}", cancellationToken);
    }

    private static async Task RunAsync(
        string compiler,
        IEnumerable<string> arguments,
        string operation,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(compiler)
        {
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(compiler))!,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start slangc.");
        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        string standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"slangc failed during {operation}: {error}{standardOutput}");
        }
    }

    private sealed record SlangEntryPoint(string Name, GpuShaderStage Stage, string SlangStage);
}
