using System.Collections.ObjectModel;

namespace Lumyte.Graphics.Native.Shaders.Offline;

public sealed record NativeShaderEntryPoint(string Name, GpuShaderStage Stage);

public sealed record NativeShaderBuildTarget(
    NativeShaderTarget Target,
    string? Profile = null,
    NativeShaderCapabilities RequiredCapabilities = NativeShaderCapabilities.None,
    NativeShaderDescriptorHeapAbi? DescriptorHeapAbi = null);

/// <summary>Offline source inputs. ParameterTypes are shader types stored as StructuredBuffer elements.</summary>
public sealed class NativeShaderBuildRequest
{
    public NativeShaderBuildRequest(string sourcePath, IEnumerable<NativeShaderEntryPoint> entryPoints,
        IEnumerable<NativeShaderBuildTarget> targets, string hostNamespace, string? rootParameterName = "root",
        IEnumerable<string>? parameterTypes = null, IEnumerable<string>? includeDirectories = null,
        IReadOnlyDictionary<string, string>? defines = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostNamespace);
        SourcePath = Path.GetFullPath(sourcePath);
        EntryPoints = Array.AsReadOnly(entryPoints.ToArray());
        Targets = Array.AsReadOnly(targets.ToArray());
        HostNamespace = hostNamespace;
        RootParameterName = rootParameterName;
        ParameterTypes = Array.AsReadOnly((parameterTypes ?? []).ToArray());
        IncludeDirectories = Array.AsReadOnly((includeDirectories ?? []).Select(Path.GetFullPath).ToArray());
        Defines = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(defines ?? new Dictionary<string, string>(), StringComparer.Ordinal));
        if (EntryPoints.Count == 0 || Targets.Count == 0 || Targets.Select(t => t.Target).Distinct().Count() != Targets.Count)
        {
            throw new ArgumentException("A build requires entries and distinct Native targets.");
        }
        foreach (NativeShaderBuildTarget target in Targets)
        {
            if (!Enum.IsDefined(target.Target)) { throw new ArgumentOutOfRangeException(nameof(targets), "Unknown Native shader target."); }
            if (target.DescriptorHeapAbi is not { } abi) { continue; }
            NativeShaderDescriptorHeapAbiKind expected = target.Target == NativeShaderTarget.DirectX12
                ? NativeShaderDescriptorHeapAbiKind.DirectX12 : NativeShaderDescriptorHeapAbiKind.VulkanUnified;
            if (abi.Version != 1 || abi.Layout is not null || (abi.Kind != NativeShaderDescriptorHeapAbiKind.None && abi.Kind != expected))
            {
                throw new ArgumentException("The descriptor ABI must match the target's emitted ABI (version 1, no fixed layout).", nameof(targets));
            }
        }
    }

    public string SourcePath { get; }
    public IReadOnlyList<NativeShaderEntryPoint> EntryPoints { get; }
    public IReadOnlyList<NativeShaderBuildTarget> Targets { get; }
    public string HostNamespace { get; }
    public string? RootParameterName { get; }
    public IReadOnlyList<string> ParameterTypes { get; }
    public IReadOnlyList<string> IncludeDirectories { get; }
    public IReadOnlyDictionary<string, string> Defines { get; }
}
