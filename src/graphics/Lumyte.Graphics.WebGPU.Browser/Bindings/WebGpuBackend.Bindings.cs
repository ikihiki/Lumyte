using System.Runtime.InteropServices.JavaScript;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser;

public sealed partial class WebGpuBackend
{
    private sealed class BindingsResource(WebGpuBackend owner, JSObject handle, BindingLayoutResource layout,
        P.GpuBindingEntry[] entries, TextureViewLease[] views, SamplerLease[] samplers,
        Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics) : P.GpuBindingsHandle
    {
        internal readonly WebGpuBackend Owner = owner;
        internal readonly JSObject Handle = handle;
        internal readonly BindingLayoutResource Layout = layout;
        internal readonly P.GpuBindingEntry[] Entries = entries;
        internal readonly TextureViewLease[] Views = views;
        internal readonly SamplerLease[] Samplers = samplers;
        internal readonly Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal bool Destroyed;
    }

    public P.GpuBindingsHandle CreateBindings(P.GpuBindingLayoutHandle layout, ReadOnlySpan<P.GpuBindingEntry> entries)
    {
        RequireAvailable();
        BindingLayoutResource layoutResource = RequireBindingLayout(layout);
        P.GpuBindingEntry[] copy = entries.ToArray();
        var nativeEntries = new object[copy.Length];
        var references = new List<JSObject>(copy.Length + 1) { layoutResource.Handle };
        var views = new List<TextureViewLease>(copy.Length);
        var samplers = new List<SamplerLease>(copy.Length);
        var dependencies = new List<Task<IReadOnlyList<P.GpuDiagnostic>>>(copy.Length + 2) { layoutResource.Diagnostics };
        JSObject? handle = null;
        try
        {
            for (int index = 0; index < copy.Length; index++)
            {
                P.GpuBindingEntry entry = copy[index];
                object? resource = null;
                switch (entry.Kind)
                {
                    case P.GpuBindingResourceKind.Undefined: break;
                    case P.GpuBindingResourceKind.Buffer:
                        BufferResource buffer = RequireBuffer(entry.BufferRange.Buffer);
                        resource = new
                        {
                            Buffer = Ref(references.Count),
                            Offset = Exact(entry.BufferRange.Offset, nameof(entries)),
                            Size = entry.BufferRange.Length.HasValue ? (double?)Exact(entry.BufferRange.Length.Value, nameof(entries)) : null,
                        };
                        references.Add(buffer.Handle);
                        dependencies.Add(buffer.Diagnostics);
                        break;
                    case P.GpuBindingResourceKind.Texture:
                        TextureViewLease view = AcquireTextureView(entry.TextureView, TextureBindingUsage(layoutResource, entry.Binding));
                        views.Add(view);
                        resource = Ref(references.Count);
                        references.Add(view.Handle);
                        dependencies.Add(view.Diagnostics);
                        break;
                    case P.GpuBindingResourceKind.Sampler:
                        SamplerLease sampler = AcquireSampler(entry.SamplerDescription);
                        samplers.Add(sampler);
                        resource = Ref(references.Count);
                        references.Add(sampler.Handle);
                        dependencies.Add(sampler.Diagnostics);
                        break;
                    default: throw new ArgumentOutOfRangeException(nameof(entries));
                }
                nativeEntries[index] = new { entry.Binding, Resource = resource };
            }
            var created = CreateObject(device, "bindings", new { Layout = Ref(0), Entries = nativeEntries }, references.ToArray());
            handle = created.Handle;
            dependencies.Add(created.Diagnostics);
            return new BindingsResource(this, handle, layoutResource, copy, views.ToArray(), samplers.ToArray(),
                BrowserDiagnostics.CombineAsync(dependencies.ToArray()));
        }
        catch
        {
            handle?.Dispose();
            foreach (TextureViewLease view in views) { ReleaseTextureView(view); }
            foreach (SamplerLease sampler in samplers) { ReleaseSampler(sampler); }
            throw;
        }
    }

    public void DestroyBindings(P.GpuBindingsHandle bindings)
    {
        runtime.RequireThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        BindingsResource resource = RequireBindings(bindings);
        resource.Destroyed = true;
        resource.Handle.Dispose();
        foreach (TextureViewLease view in resource.Views) { ReleaseTextureView(view); }
        foreach (SamplerLease sampler in resource.Samplers) { ReleaseSampler(sampler); }
    }

    private BindingsResource RequireBindings(P.GpuBindingsHandle bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (bindings is not BindingsResource resource || !ReferenceEquals(resource.Owner, this))
        { throw new ArgumentException("Bindings belong to another device.", nameof(bindings)); }
        ObjectDisposedException.ThrowIf(resource.Destroyed, bindings);
        return resource;
    }

    internal Task<IReadOnlyList<P.GpuDiagnostic>> GetCreationDiagnostics(P.GpuBindingsHandle bindings)
    { runtime.RequireThread(); return RequireBindings(bindings).Diagnostics; }

    private static uint TextureBindingUsage(BindingLayoutResource layout, uint binding)
    {
        foreach (P.GpuBindingLayoutEntry entry in layout.Entries)
        {
            if (entry.Binding == binding) { return entry.Kind == P.GpuBindingLayoutKind.StorageTexture ? 8u : 4u; }
        }
        return 4;
    }
}
