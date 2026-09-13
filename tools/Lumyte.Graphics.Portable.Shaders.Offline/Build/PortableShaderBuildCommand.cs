using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Lumyte.Graphics.Shaders.Offline;

namespace Lumyte.Graphics.Portable.Shaders.Offline;

internal static class PortableShaderBuildCommand
{
    internal static async Task RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Length; index += 2)
        {
            if (index + 1 == arguments.Length ||
                arguments[index] is not ("--requests" or "--output" or "--tint-info") ||
                !options.TryAdd(arguments[index], arguments[index + 1]))
            {
                throw new ArgumentException("Usage: --requests <path-list> --output <directory> --tint-info <tint_info>");
            }
        }
        foreach (string required in new[] { "--requests", "--output", "--tint-info" })
        {
            if (!options.ContainsKey(required))
            {
                throw new ArgumentException($"Missing {required}.");
            }
        }

        string[] paths = await File.ReadAllLinesAsync(options["--requests"], cancellationToken);
        var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string path in paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.Ordinal))
        {
            string requestPath = Path.GetFullPath(path);
            var (source, compileOptions) = ReadRequest(requestPath, options["--tint-info"]);
            PortableShaderBuildResult result = await PortableShaderCompiler.CompileAsync(source, compileOptions, cancellationToken);
            string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestPath)));
            foreach (PortableShaderGeneratedFile file in result.GeneratedSources.Concat(result.ResourceInputs))
            {
                outputs.Add($"{id}/{file.FileName}", Encoding.UTF8.GetBytes(file.Content));
            }

            outputs.Add($"{id}/module.wgsl", Encoding.UTF8.GetBytes(result.Package.Module));
            foreach (string diagnostic in result.Diagnostics)
            {
                Console.Error.WriteLine($"{requestPath}: {diagnostic}");
            }
        }
        await ShaderBuildOutputs.PublishAsync(options["--output"], outputs, cancellationToken);
    }

    internal static (PortableShaderSource Source, PortableShaderCompileOptions Options) ReadRequest(string requestPath, string tintInfoPath)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        Request input = JsonSerializer.Deserialize<Request>(File.ReadAllText(requestPath), options)
            ?? throw new InvalidDataException("A Portable shader request must be a JSON object.");
        string directory = Path.GetDirectoryName(Path.GetFullPath(requestPath))!;
        string module = File.ReadAllText(Path.GetFullPath(input.Source, directory));
        return (new PortableShaderSource(module, input.EntryPoints, input.Language),
            new PortableShaderCompileOptions(tintInfoPath, input.HostNamespace, input.Name,
                input.RootTypeName, input.ParameterTypeNames));
    }

    private sealed class Request
    {
        public required string Source { get; init; }
        public required string HostNamespace { get; init; }
        public required string Name { get; init; }
        public PortableShaderSourceLanguage Language { get; init; } = PortableShaderSourceLanguage.Wgsl;
        public string[] EntryPoints { get; init; } = [];
        public string? RootTypeName { get; init; }
        public string[] ParameterTypeNames { get; init; } = [];
    }
}
