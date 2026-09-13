namespace Lumyte.Graphics.Portable.Shaders;

/// <summary>Prepared, immutable WGSL and ABI metadata, independent of devices and asset I/O.</summary>
/// <remarks>Sequence inputs are copied; all nested package values are immutable. The ABI hash is an opaque build identity.</remarks>
public sealed class PortableShaderPackage
{
    public const uint CurrentVersion = 1;

    public PortableShaderPackage(uint version, string module,
        ReadOnlySpan<PortableShaderEntryPoint> entryPoints, PortableShaderFeatures requiredFeatures,
        ReadOnlySpan<PortableShaderGroupLayout> groupLayouts, PortableShaderDataLayout? rootLayout,
        ReadOnlySpan<PortableShaderDataLayout> parameterLayouts,
        PortableShaderBindingSchema bindingSchema, string abiHash)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(bindingSchema);
        ArgumentException.ThrowIfNullOrEmpty(abiHash);
        foreach (PortableShaderGroupLayout layout in groupLayouts) { ArgumentNullException.ThrowIfNull(layout); }
        foreach (PortableShaderDataLayout layout in parameterLayouts) { ArgumentNullException.ThrowIfNull(layout); }
        Version = version;
        Module = module;
        EntryPoints = Array.AsReadOnly(entryPoints.ToArray());
        RequiredFeatures = requiredFeatures;
        GroupLayouts = Array.AsReadOnly(groupLayouts.ToArray());
        RootLayout = rootLayout;
        ParameterLayouts = Array.AsReadOnly(parameterLayouts.ToArray());
        BindingSchema = bindingSchema;
        AbiHash = abiHash;
    }

    public uint Version { get; }
    public string Module { get; }
    public IReadOnlyList<PortableShaderEntryPoint> EntryPoints { get; }
    public PortableShaderFeatures RequiredFeatures { get; }
    public IReadOnlyList<PortableShaderGroupLayout> GroupLayouts { get; }
    public PortableShaderDataLayout? RootLayout { get; }
    public IReadOnlyList<PortableShaderDataLayout> ParameterLayouts { get; }
    public PortableShaderBindingSchema BindingSchema { get; }
    public string AbiHash { get; }
}
