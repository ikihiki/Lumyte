using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Lumyte.Graphics.Shaders.Offline;

namespace Lumyte.Graphics.Native.Shaders.Offline;

internal static class NativeShaderBuildCommand
{
    internal static async Task RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Length; index += 2)
        {
            if (index + 1 == arguments.Length ||
                arguments[index] is not ("--requests" or "--output" or "--compiler" or "--downstream") ||
                !options.TryAdd(arguments[index], arguments[index + 1]))
            {
                throw new ArgumentException("Usage: --requests <path-list> --output <directory> --compiler <slangc> [--downstream <directory>]");
            }
        }
        foreach (string required in new[] { "--requests", "--output", "--compiler" })
        {
            if (!options.ContainsKey(required))
            {
                throw new ArgumentException($"Missing {required}.");
            }
        }

        NativeShaderCompiler? compiler = null;
        string[] paths = await File.ReadAllLinesAsync(options["--requests"], cancellationToken);
        var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string path in paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.Ordinal))
        {
            string requestPath = Path.GetFullPath(path);
            NativeShaderBuildRequest request = ReadRequest(requestPath);
            compiler ??= new NativeShaderCompiler(options["--compiler"], options.GetValueOrDefault("--downstream"));
            NativeShaderBuildResult result = await compiler.BuildAsync(request, cancellationToken);
            string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestPath)));
            foreach ((string name, string source) in result.HostSourceFiles)
            {
                outputs.Add($"{id}/{name}", Encoding.UTF8.GetBytes(source));
            }

            foreach ((string name, string schema) in result.ResourceInputFiles)
            {
                outputs.Add($"{id}/{name}", Encoding.UTF8.GetBytes(schema));
            }

            outputs.Add($"{id}/package.native.shader.json", result.PackageBytes.ToArray());
        }

        // Publish only after every compile succeeded. The inventory replaces the
        // previous build's inputs, including when a shader or binding was removed.
        await ShaderBuildOutputs.PublishAsync(options["--output"], outputs, cancellationToken);
    }

    internal static NativeShaderBuildRequest ReadRequest(string requestPath)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        Request input = JsonSerializer.Deserialize<Request>(File.ReadAllText(requestPath), options)
            ?? throw new InvalidDataException("A Native shader request must be a JSON object.");
        string directory = Path.GetDirectoryName(Path.GetFullPath(requestPath))!;
        return new NativeShaderBuildRequest(Path.GetFullPath(input.Source, directory),
            input.EntryPoints.Select(entry => new NativeShaderEntryPoint(entry.Name, entry.Stage)),
            input.Targets.Select(target => new NativeShaderBuildTarget(target.Target, target.Profile,
                target.RequiredCapabilities, target.DescriptorHeapAbi is { } kind ? new(kind) : null)),
            input.HostNamespace, input.RootParameterName, input.ParameterTypes,
            input.IncludeDirectories.Select(path => Path.GetFullPath(path, directory)), input.Defines);
    }

    private sealed class Request
    {
        public required string Source { get; init; }
        public required string HostNamespace { get; init; }
        public required Entry[] EntryPoints { get; init; }
        public required BuildTargetInput[] Targets { get; init; }
        public string? RootParameterName { get; init; } = "root";
        public string[] ParameterTypes { get; init; } = [];
        public string[] IncludeDirectories { get; init; } = [];
        public Dictionary<string, string> Defines { get; init; } = [];
    }

    private sealed class Entry
    {
        public required string Name { get; init; }
        public required GpuShaderStage Stage { get; init; }
    }

    private sealed class BuildTargetInput
    {
        public required NativeShaderTarget Target { get; init; }
        public string? Profile { get; init; }
        public NativeShaderCapabilities RequiredCapabilities { get; init; }
        public NativeShaderDescriptorHeapAbiKind? DescriptorHeapAbi { get; init; }
    }
}
