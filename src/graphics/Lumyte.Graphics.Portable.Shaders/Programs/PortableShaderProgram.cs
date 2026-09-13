using System.Runtime.ExceptionServices;

namespace Lumyte.Graphics.Portable.Shaders;

/// <summary>Owns the module and layouts initialized from one prepared package.</summary>
/// <remarks>
/// The backend is borrowed. End all binding, pipeline, recording and GPU uses before disposal.
/// Copying Description or any handle does not extend their lifetime. Disposal performs no GPU wait.
/// </remarks>
public sealed class PortableShaderProgram : IDisposable
{
    private readonly IPortableGpuBackend backend;
    private readonly GpuShaderModuleHandle module;
    private readonly GpuBindingLayoutHandle[] layouts;
    private bool disposed;

    internal PortableShaderProgram(IPortableGpuBackend backend, PortableShaderPackage package,
        PortableShaderProgramKind kind, GpuShaderModuleHandle module, GpuBindingLayoutHandle[] layouts)
    {
        this.backend = backend;
        this.module = module;
        this.layouts = layouts;
        Package = package;
        Kind = kind;
        GpuShaderEntryPoint[] entries = package.EntryPoints
            .Select(entry => new GpuShaderEntryPoint(module, entry.Stage, entry.Name)).ToArray();
        Description = new(entries, layouts, package.RootLayout?.Size ?? 0);
    }

    public PortableShaderPackage Package { get; }
    public PortableShaderProgramKind Kind { get; }
    public GpuShaderProgramDescription Description { get; }
    public IReadOnlyList<GpuShaderEntryPoint> EntryPoints => Description.EntryPoints;
    public IReadOnlyList<GpuBindingLayoutHandle> BindingLayouts => Description.BindingLayouts;
    public uint ImmediateSize => Description.ImmediateSize;
    public PortableShaderDataLayout? RootLayout => Package.RootLayout;
    public IReadOnlyList<PortableShaderDataLayout> ParameterLayouts => Package.ParameterLayouts;
    public PortableShaderBindingSchema BindingSchema => Package.BindingSchema;
    public string AbiHash => Package.AbiHash;

    /// <summary>Attempts every owned release once, even if a backend release fails.</summary>
    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        ReleaseObjects(backend, module, layouts);
    }

    internal static void ReleaseObjects(IPortableGpuBackend backend, GpuShaderModuleHandle? module,
        IReadOnlyList<GpuBindingLayoutHandle> layouts)
    {
        List<Exception>? errors = null;
        for (int index = layouts.Count - 1; index >= 0; index--)
        {
            try { backend.DestroyBindingLayout(layouts[index]); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        if (module is not null)
        {
            try { backend.DestroyShaderModule(module); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        if (errors is { Count: 1 }) { ExceptionDispatchInfo.Capture(errors[0]).Throw(); }
        if (errors is not null) { throw new AggregateException("Releasing Portable shader objects failed.", errors); }
    }
}
