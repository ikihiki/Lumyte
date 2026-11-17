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
    public Task<ShaderArtifact> CompileAsync(ShaderCompilationDesc desc, CancellationToken cancellationToken = default)
        => CompileOfflineAsync(desc, [], cancellationToken);

    internal async Task<ShaderArtifact> CompileOfflineAsync(ShaderCompilationDesc desc, IReadOnlyList<string> additionalArguments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(desc);
        ArgumentException.ThrowIfNullOrWhiteSpace(desc.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(desc.EntryPoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(compilerPath);
        if ((desc.Target is { } requested && !Enum.IsDefined(requested)) || !Enum.IsDefined(desc.Stage))
        {
            throw new ArgumentException("Unknown shader target or stage.", nameof(desc));
        }

        bool meshStage = desc.Stage is ShaderStage.Mesh or ShaderStage.Task;
        if (meshStage && desc.Target == ShaderTarget.Wgsl)
        {
            throw new NotSupportedException("WGSL does not support Mesh or Task shaders.");
        }

        string stage = desc.Stage switch
        {
            ShaderStage.Vertex => "vertex",
            ShaderStage.Fragment => "fragment",
            ShaderStage.Compute => "compute",
            ShaderStage.Mesh => "mesh",
            ShaderStage.Task => "amplification",
            _ => throw new ArgumentException("Unknown shader stage.", nameof(desc)),
        };
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
            ShaderTarget[] targets = desc.Target is { } selected ? [selected] : meshStage ? [ShaderTarget.SpirV, ShaderTarget.Dxil] : [ShaderTarget.Wgsl, ShaderTarget.SpirV, ShaderTarget.Dxil];
            var results = new List<ShaderTargetData>();
            foreach (ShaderTarget target in targets)
            {
                string prepared = ShaderSourcePreparation.Prepare(desc.Source);
                await File.WriteAllTextAsync(source, prepared, cancellationToken);
                string[] arguments = [source, "-entry", desc.EntryPoint, "-stage", stage, "-target", target switch { ShaderTarget.Wgsl => "wgsl", ShaderTarget.Dxil => "dxil", _ => "spirv" }, "-matrix-layout-row-major", target switch { ShaderTarget.SpirV => "-DLUMYTE_SPIRV=1", ShaderTarget.Dxil => "-DLUMYTE_DXIL=1", _ => "-DLUMYTE_WGSL=1" }, "-I", directory, "-reflection-json", reflection, "-o", output];
                arguments = [.. arguments, .. additionalArguments];
                if (target == ShaderTarget.Dxil)
                {
                    arguments = [.. arguments, "-profile", "sm_6_6"];
                }

                if (meshStage && target == ShaderTarget.SpirV)
                {
                    arguments = [.. arguments, "-capability", "spvMeshShadingEXT", "-emit-spirv-directly"];
                }

                _ = await RunCompilerAsync(arguments, cancellationToken);
                byte[] code = await File.ReadAllBytesAsync(output, cancellationToken);
                string reflectionJson = ShaderSourcePreparation.CompleteReflection(await File.ReadAllTextAsync(reflection, cancellationToken), desc.Source, target);
                if (meshStage || (desc.Stage == ShaderStage.Fragment && target != ShaderTarget.Wgsl))
                {
                    string declarations = Path.Combine(directory, "declarations.hlsl");
                    string define = target == ShaderTarget.SpirV ? "-DLUMYTE_SPIRV=1" : "-DLUMYTE_DXIL=1";
                    _ = await RunCompilerAsync([source, "-entry", desc.EntryPoint, "-stage", stage, "-target", "hlsl", "-profile", "sm_6_6", "-matrix-layout-row-major", define, "-I", directory, .. additionalArguments, "-o", declarations], cancellationToken);
                    string emitted = await File.ReadAllTextAsync(declarations, cancellationToken);
                    reflectionJson = meshStage ? MeshShaderReflection.Complete(reflectionJson, emitted, desc.Stage, desc.EntryPoint, target, code)
                        : MeshShaderReflection.CompleteFragment(reflectionJson, emitted, desc.EntryPoint, target, code);
                }

                if (meshStage)
                {
                    reflectionJson = MeshShaderMetadata.CompleteIntegrity(reflectionJson, code);
                }

                if (desc.Stage == ShaderStage.Fragment && target != ShaderTarget.Wgsl)
                {
                    reflectionJson = MeshShaderMetadata.CompleteFragmentIntegrity(reflectionJson, code);
                }

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
