namespace Lumyte.Graphics.Native.Shaders.Offline;

/// <summary>All dictionaries use flat file names. Outputs belong to one compiler invocation.</summary>
public sealed class NativeShaderBuildResult
{
    internal NativeShaderBuildResult(NativeShaderPackage package, byte[] packageBytes,
        IDictionary<string, string> hostSourceFiles, IDictionary<string, string> resourceInputFiles, string compilerVersion)
    {
        Package = package;
        this.packageBytes = packageBytes.ToArray();
        HostSourceFiles = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(hostSourceFiles);
        ResourceInputFiles = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(resourceInputFiles);
        CompilerVersion = compilerVersion;
    }

    private readonly byte[] packageBytes;
    public NativeShaderPackage Package { get; }
    public ReadOnlyMemory<byte> PackageBytes => packageBytes;
    public IReadOnlyDictionary<string, string> HostSourceFiles { get; }
    public IReadOnlyDictionary<string, string> ResourceInputFiles { get; }
    public string CompilerVersion { get; }
}
