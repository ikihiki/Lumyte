namespace Lumyte.Graphics.Portable.Shaders.Offline;

public enum PortableShaderSourceLanguage { Wgsl, Slang }

/// <summary>Build input text and optional entry selection. This is not a runtime loader.</summary>
public sealed class PortableShaderSource
{
    public PortableShaderSource(string module, IEnumerable<string>? entryPoints = null,
        PortableShaderSourceLanguage language = PortableShaderSourceLanguage.Wgsl)
    {
        ArgumentNullException.ThrowIfNull(module);
        Module = module;
        Language = language;
        EntryPoints = Array.AsReadOnly(entryPoints?.ToArray() ?? []);
    }

    public string Module { get; }
    public PortableShaderSourceLanguage Language { get; }
    public IReadOnlyList<string> EntryPoints { get; }
}

/// <summary>Names select prepared host types; offsets and sizes always come from Tint.</summary>
public sealed class PortableShaderCompileOptions
{
    public PortableShaderCompileOptions(string tintInfoPath, string generatedNamespace, string generatedName,
        string? rootTypeName = null, IEnumerable<string>? parameterTypeNames = null, string? tintPath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(tintInfoPath);
        ArgumentException.ThrowIfNullOrEmpty(generatedNamespace);
        ArgumentException.ThrowIfNullOrEmpty(generatedName);
        TintInfoPath = tintInfoPath;
        GeneratedNamespace = generatedNamespace;
        GeneratedName = generatedName;
        RootTypeName = rootTypeName;
        ParameterTypeNames = Array.AsReadOnly(parameterTypeNames?.ToArray() ?? []);
        TintPath = tintPath;
    }

    public string TintInfoPath { get; }
    public string GeneratedNamespace { get; }
    public string GeneratedName { get; }
    public string? RootTypeName { get; }
    public IReadOnlyList<string> ParameterTypeNames { get; }
    public string? TintPath { get; }
}

public sealed record PortableShaderGeneratedFile(string FileName, string Content);

/// <summary>Owned, prepared runtime data plus build artifacts. The caller decides where to save them.</summary>
public sealed class PortableShaderBuildResult
{
    internal PortableShaderBuildResult(PortableShaderPackage package, IEnumerable<PortableShaderGeneratedFile> sources,
        IEnumerable<PortableShaderGeneratedFile> resourceInputs, string reflectionJson, string reflectionText, string reflectionIr,
        IEnumerable<string> diagnostics)
    {
        Package = package;
        GeneratedSources = Array.AsReadOnly(sources.ToArray());
        ResourceInputs = Array.AsReadOnly(resourceInputs.ToArray());
        ReflectionJson = reflectionJson;
        ReflectionText = reflectionText;
        ReflectionIr = reflectionIr;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public PortableShaderPackage Package { get; }
    public IReadOnlyList<PortableShaderGeneratedFile> GeneratedSources { get; }
    public IReadOnlyList<PortableShaderGeneratedFile> ResourceInputs { get; }
    public string ReflectionJson { get; }
    public string ReflectionText { get; }
    public string ReflectionIr { get; }
    public IReadOnlyList<string> Diagnostics { get; }
}
