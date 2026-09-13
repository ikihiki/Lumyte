namespace Lumyte.Graphics.Portable.Shaders;

/// <summary>Initializes prepared packages on a borrowed backend without I/O, shader parsing or input conversion.</summary>
public sealed class PortableShaderLoader
{
    private readonly IPortableGpuBackend backend;

    public PortableShaderLoader(IPortableGpuBackend backend)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <summary>Creates owned module and group layouts; native shader diagnostics remain attached to those objects.</summary>
    /// <remarks>Optional expectedAbiHash checks generated-code correspondence before creating GPU objects.</remarks>
    public PortableShaderProgram Load(PortableShaderPackage package, string? expectedAbiHash = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Version != PortableShaderPackage.CurrentVersion)
        { throw new NotSupportedException($"Portable shader package version {package.Version} is not supported."); }
        if (expectedAbiHash is not null && !StringComparer.Ordinal.Equals(package.AbiHash, expectedAbiHash))
        { throw new ArgumentException("The Portable shader package ABI hash does not match the generated inputs.", nameof(package)); }
        PortableShaderProgramKind kind = RequireKind(package);
        RequireFeatures(package);
        RequireSchema(package);

        GpuShaderModuleHandle? module = null;
        var layouts = new List<GpuBindingLayoutHandle>(package.GroupLayouts.Count);
        try
        {
            module = backend.CreateShaderModule(package.Module);
            foreach (PortableShaderGroupLayout group in package.GroupLayouts)
            { layouts.Add(backend.CreateBindingLayout(group.Entries.ToArray())); }
            return new(backend, package, kind, module, layouts.ToArray());
        }
        catch (Exception error)
        {
            try { PortableShaderProgram.ReleaseObjects(backend, module, layouts); }
            catch (Exception cleanupError)
            { throw new AggregateException("Portable shader initialization and rollback failed.", error, cleanupError); }
            throw;
        }
    }

    private void RequireFeatures(PortableShaderPackage package)
    {
        const PortableShaderFeatures known = PortableShaderFeatures.ImmediateAddressSpace | PortableShaderFeatures.DualSourceBlend;
        if ((package.RequiredFeatures & ~known) != 0)
        { throw new NotSupportedException("The package requires a feature not represented by this Portable shader runtime."); }
        uint immediateSize = package.RootLayout?.Size ?? 0;
        bool requiresImmediate = immediateSize != 0 || package.RequiredFeatures.HasFlag(PortableShaderFeatures.ImmediateAddressSpace);
        if (requiresImmediate && !backend.Capabilities.DirectRootData)
        { throw new NotSupportedException("The package requires direct root data (immediate_address_space)."); }
        if (package.RequiredFeatures.HasFlag(PortableShaderFeatures.DualSourceBlend) && !backend.Capabilities.DualSourceBlend)
        { throw new NotSupportedException("The package requires dual-source blending."); }
        if (immediateSize > backend.Limits.MaxImmediateSize)
        { throw new NotSupportedException($"The package requires {immediateSize} immediate bytes, exceeding the device limit {backend.Limits.MaxImmediateSize}."); }
    }

    private static PortableShaderProgramKind RequireKind(PortableShaderPackage package)
    {
        GpuShaderStage stages = GpuShaderStage.None;
        foreach (PortableShaderEntryPoint entry in package.EntryPoints)
        {
            if (string.IsNullOrEmpty(entry.Name)
                || entry.Stage is not (GpuShaderStage.Vertex or GpuShaderStage.Pixel or GpuShaderStage.Compute)
                || (stages & entry.Stage) != 0)
            { throw new ArgumentException("Package entries require one named entry per distinct supported stage.", nameof(package)); }
            stages |= entry.Stage;
        }
        return stages switch
        {
            GpuShaderStage.Compute => PortableShaderProgramKind.Compute,
            GpuShaderStage.Vertex or (GpuShaderStage.Vertex | GpuShaderStage.Pixel) => PortableShaderProgramKind.Raster,
            _ => throw new ArgumentException("A package defines compute, vertex-only, or vertex/pixel entries.", nameof(package)),
        };
    }

    private static void RequireSchema(PortableShaderPackage package)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var locations = new HashSet<(uint Group, uint Binding)>();
        foreach (PortableShaderBindingSchemaEntry entry in package.BindingSchema.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) || !names.Add(entry.Name) || !locations.Add((entry.Group, entry.Binding)))
            { throw new ArgumentException("Binding schema names and locations must identify distinct package inputs.", nameof(package)); }
            if (entry.Group >= package.GroupLayouts.Count
                || !package.GroupLayouts[(int)entry.Group].Entries.Any(layout => layout.Binding == entry.Binding && layout.Kind == entry.Kind))
            { throw new ArgumentException($"Binding schema input '{entry.Name}' does not match a declared group binding kind.", nameof(package)); }
        }
    }
}
