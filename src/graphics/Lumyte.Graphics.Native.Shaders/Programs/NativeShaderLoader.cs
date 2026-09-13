namespace Lumyte.Graphics.Native.Shaders;

/// <summary>Selects prepared CPU artifacts for a borrowed backend. It creates no GPU objects and performs no I/O.</summary>
public sealed class NativeShaderLoader
{
    private readonly INativeGpuBackend backend;

    public NativeShaderLoader(INativeGpuBackend native)
    {
        ArgumentNullException.ThrowIfNull(native);
        backend = native;
    }

    public NativeShaderProgram Load(NativeShaderPackage package, string? expectedAbiHash = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Version != NativeShaderPackage.CurrentVersion)
        {
            throw new NotSupportedException($"Native shader package version {package.Version} is not supported.");
        }
        GpuShaderCodeFormat format = backend.ShaderCodeFormat;
        NativeShaderTarget target = format switch
        {
            GpuShaderCodeFormat.Dxil => NativeShaderTarget.DirectX12,
            GpuShaderCodeFormat.SpirV => NativeShaderTarget.Vulkan,
            _ => throw new NotSupportedException($"Code format {format} is not a Native shader target."),
        };
        NativeShaderCapabilities available = Features(backend.Capabilities);
        NativeGpuDescriptorLimits? descriptors = backend.Limits.Descriptors;
        NativeShaderArtifact? selected = null;
        foreach (NativeShaderArtifact artifact in package.Artifacts)
        {
            if (artifact.Target != target || artifact.CodeFormat != format ||
                (artifact.RequiredCapabilities & available) != artifact.RequiredCapabilities ||
                !artifact.DescriptorHeapAbi.Matches(target, descriptors))
            {
                continue;
            }
            if (selected is not null)
            {
                throw new InvalidOperationException($"Native shader package has multiple compatible artifacts for {target}; selection must be unambiguous.");
            }
            selected = artifact;
        }
        if (selected is null)
        {
            throw new NotSupportedException($"Native shader package has no artifact matching {target}, its enabled shader capabilities, and descriptor heap ABI.");
        }
        if (expectedAbiHash is not null && !string.Equals(selected.AbiHash, expectedAbiHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The selected Native shader artifact does not match the expected host ABI hash.");
        }
        return new NativeShaderProgram(selected);
    }

    private static NativeShaderCapabilities Features(NativeGpuCapabilities value)
    {
        NativeShaderCapabilities result = NativeShaderCapabilities.None;
        if (value.RawShaderPointers) { result |= NativeShaderCapabilities.RawShaderPointers; }
        if (value.BufferDescriptors) { result |= NativeShaderCapabilities.BufferDescriptors; }
        if (value.MeshShaders) { result |= NativeShaderCapabilities.MeshShaders; }
        if (value.AmplificationShaders) { result |= NativeShaderCapabilities.AmplificationShaders; }
        return result;
    }
}
