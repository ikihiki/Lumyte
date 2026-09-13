using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lumyte.Graphics.Native.Shaders.Offline;

/// <summary>Invokes Slang offline. No device, package loading, or runtime shader compilation is performed.</summary>
public sealed class NativeShaderCompiler(string compilerPath, string? downstreamCompilerDirectory = null)
{
    private readonly string compilerPath = Path.GetFullPath(compilerPath);
    private readonly string? downstreamDirectory = downstreamCompilerDirectory is null ? null : Path.GetFullPath(downstreamCompilerDirectory);

    public async Task<NativeShaderBuildResult> BuildAsync(NativeShaderBuildRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        NativeShaderGeneration.ValidateNames(request);
        string directory = Path.Combine(Path.GetTempPath(), "LumyteNativeShader", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using Stream annotations = typeof(NativeShaderCompiler).Assembly.GetManifestResourceStream(
                "Lumyte.Graphics.Native.Shaders.Offline.Compiler.LumyteResources.slang")!;
            using (FileStream output = File.Create(Path.Combine(directory, "LumyteResources.slang")))
            {
                await annotations.CopyToAsync(output, cancellationToken);
            }
            string version = (await RunAsync(["-version"], cancellationToken)).Trim();
            if (version != "2026.17")
            {
                throw new NotSupportedException($"Native reflection requires Slang 2026.17; compiler reported '{version}'.");
            }
            var artifacts = new List<NativeShaderArtifact>();
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (NativeShaderBuildTarget target in request.Targets)
            {
                var stages = new List<NativeShaderStageArtifact>();
                NativeReflectedLayout? root = null;
                var reflectionIdentities = new List<string>();
                foreach (NativeShaderEntryPoint entry in request.EntryPoints)
                {
                    string stem = Path.Combine(directory, target.Target + "-" + entry.Stage);
                    string jsonPath = stem + ".json";
                    string codePath = stem + ".bin";
                    List<string> arguments = Arguments(request, target, directory, request.SourcePath,
                        entry.Name, StageName(entry.Stage), jsonPath, codePath);
                    await RunAsync(arguments, cancellationToken);
                    string reflection = await File.ReadAllTextAsync(jsonPath, cancellationToken);
                    using JsonDocument document = JsonDocument.Parse(reflection);
                    NativeReflectedLayout stageRoot = NativeShaderReflection.Root(document.RootElement, request.RootParameterName);
                    if (root is not null && root.Identity != stageRoot.Identity)
                    {
                        throw new InvalidDataException("Selected stages do not share the same Native root ABI.");
                    }
                    root = stageRoot;
                    reflectionIdentities.Add(reflection);
                    stages.Add(new(entry.Stage, entry.Name, await File.ReadAllBytesAsync(codePath, cancellationToken)));
                }
                var parameters = new List<NativeReflectedLayout>();
                if (request.ParameterTypes.Count != 0)
                {
                    string probe = Path.Combine(directory, "parameters.slang");
                    var text = new StringBuilder("#include \"").Append(request.SourcePath.Replace('\\', '/').Replace("\"", "\\\"", StringComparison.Ordinal)).AppendLine("\"");
                    for (int i = 0; i < request.ParameterTypes.Count; i++)
                    {
                        text.Append("StructuredBuffer<").Append(request.ParameterTypes[i]).Append("> __lumyte_parameter_").Append(i).AppendLine(";");
                    }
                    text.AppendLine("[shader(\"compute\")][numthreads(1,1,1)] void __lumyte_reflect() {}");
                    await File.WriteAllTextAsync(probe, text.ToString(), cancellationToken);
                    string jsonPath = Path.Combine(directory, "parameters.json");
                    await RunAsync(Arguments(request, target, directory, probe, "__lumyte_reflect", "compute", jsonPath, Path.Combine(directory, "parameters.bin")), cancellationToken);
                    string reflection = await File.ReadAllTextAsync(jsonPath, cancellationToken);
                    using JsonDocument document = JsonDocument.Parse(reflection);
                    for (int i = 0; i < request.ParameterTypes.Count; i++)
                    {
                        parameters.Add(NativeShaderReflection.Parameter(document.RootElement, "__lumyte_parameter_" + i, request.ParameterTypes[i]));
                    }
                    reflectionIdentities.Add(reflection);
                }
                string settings = JsonSerializer.Serialize(new { version, target, request.EntryPoints, request.Defines,
                    Root = root!.Identity, Parameters = parameters.Select(p => p.Identity), Reflections = reflectionIdentities });
                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settings)));
                NativeShaderDescriptorHeapAbi heap = target.DescriptorHeapAbi ?? (target.Target == NativeShaderTarget.DirectX12
                    ? NativeShaderDescriptorHeapAbi.DirectX12 : NativeShaderDescriptorHeapAbi.VulkanUnified);
                var artifact = new NativeShaderArtifact(target.Target,
                    target.Target == NativeShaderTarget.DirectX12 ? GpuShaderCodeFormat.Dxil : GpuShaderCodeFormat.SpirV,
                    stages, target.RequiredCapabilities, heap, root.ToLayout("Root"), parameters.Select(p => p.ToLayout(p.Name)), hash);
                artifacts.Add(artifact);
                foreach (NativeReflectedLayout layout in new[] { root }.Concat(parameters))
                {
                    NativeShaderGeneration.AddLayout(request.HostNamespace, target.Target, layout, hash, sources, inputs);
                }
            }
            var package = new NativeShaderPackage(NativeShaderPackage.CurrentVersion, artifacts);
            sources.Add("ShaderPackage.g.cs", NativeShaderGeneration.PackageFactory(request.HostNamespace, package));
            byte[] bytes = NativeShaderGeneration.PackageBytes(package, version);
            return new(package, bytes, sources, inputs, version);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static List<string> Arguments(NativeShaderBuildRequest request, NativeShaderBuildTarget target, string directory,
        string source, string entry, string stage, string reflection, string output)
    {
        string profile = target.Profile ?? (target.Target == NativeShaderTarget.DirectX12 ? "sm_6_6" : "spirv_1_6");
        var result = new List<string> { source, "-target", target.Target == NativeShaderTarget.DirectX12 ? "dxil" : "spirv",
            "-profile", profile, "-entry", entry, "-stage", stage, "-matrix-layout-row-major", "-reflection-json", reflection, "-o", output,
            "-I", directory, "-I", Path.GetDirectoryName(request.SourcePath)! };
        if (target.Target == NativeShaderTarget.Vulkan)
        {
            result.AddRange(["-fvk-use-entrypoint-name", "-capability", "spvDescriptorHeapEXT", "-spirv-unified-descriptor-heap-stride"]);
            if (target.DescriptorHeapAbi?.Kind == NativeShaderDescriptorHeapAbiKind.VulkanFixed)
            {
                throw new NotSupportedException("This compiler emits VulkanUnified descriptor heap ABI, not fixed descriptor strides.");
            }
        }
        foreach (string include in request.IncludeDirectories) { result.AddRange(["-I", include]); }
        foreach ((string key, string value) in request.Defines.OrderBy(p => p.Key, StringComparer.Ordinal)) { result.Add("-D" + key + "=" + value); }
        return result;
    }

    private async Task<string> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(compilerPath) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
        if (downstreamDirectory is not null) { start.Environment["PATH"] = downstreamDirectory + Path.PathSeparator + start.Environment["PATH"]; }
        using Process process = Process.Start(start) ?? throw new IOException("Slang could not be started.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellationToken); }
        catch
        {
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        string diagnostic = await stderr;
        string output = await stdout;
        if (process.ExitCode != 0) { throw new InvalidDataException($"Slang failed with exit code {process.ExitCode}: {diagnostic}{output}"); }
        return output + diagnostic;
    }

    private static string StageName(GpuShaderStage stage) => stage switch
    {
        GpuShaderStage.Vertex => "vertex", GpuShaderStage.Pixel => "fragment", GpuShaderStage.Compute => "compute",
        GpuShaderStage.Mesh => "mesh", GpuShaderStage.Amplification => "amplification",
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };
}
