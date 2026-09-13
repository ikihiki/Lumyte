using P = Lumyte.Graphics.Portable;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private sealed class BindingsResource(WebGpuBackend owner, F.BindGroupHandle handle,
        BindingLayoutResource layout, P.GpuBindingEntry[] entries, TextureViewLease[] views,
        SamplerLease[] samplers, Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics) : P.GpuBindingsHandle
    {
        internal readonly WebGpuBackend Owner = owner;
        internal readonly F.BindGroupHandle Handle = handle;
        internal readonly BindingLayoutResource Layout = layout;
        internal readonly P.GpuBindingEntry[] Entries = entries;
        internal readonly TextureViewLease[] Views = views;
        internal readonly SamplerLease[] Samplers = samplers;
        internal readonly Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal bool Destroyed;
    }

    public unsafe P.GpuBindingsHandle CreateBindings(P.GpuBindingLayoutHandle layout, ReadOnlySpan<P.GpuBindingEntry> entries)
    {
        P.GpuBindingEntry[] copy = entries.ToArray();
        var nativeEntries = new F.BindGroupEntryFFI[copy.Length];
        var views = new List<TextureViewLease>(copy.Length);
        var samplers = new List<SamplerLease>(copy.Length);
        var dependencies = new List<Task<IReadOnlyList<P.GpuDiagnostic>>>(copy.Length + 2);
        lock (gate)
        {
            RequireAvailable();
            BindingLayoutResource layoutResource = RequireBindingLayout(layout);
            dependencies.Add(layoutResource.Diagnostics);
            F.BindGroupHandle handle = default;
            try
            {
                for (int index = 0; index < copy.Length; index++)
                {
                    P.GpuBindingEntry entry = copy[index];
                    var native = new F.BindGroupEntryFFI { Binding = entry.Binding };
                    switch (entry.Kind)
                    {
                        case P.GpuBindingResourceKind.Undefined: break;
                        case P.GpuBindingResourceKind.Buffer:
                            BufferResource buffer = RequireBuffer(entry.BufferRange.Buffer);
                            native.Buffer = buffer.Handle;
                            native.Offset = entry.BufferRange.Offset;
                            native.Size = MapBindingLength(entry.BufferRange.Length, nameof(P.GpuBufferRange.Length));
                            dependencies.Add(buffer.Diagnostics);
                            break;
                        case P.GpuBindingResourceKind.Texture:
                            TextureViewLease view = AcquireTextureView(entry.TextureView, TextureBindingUsage(layoutResource, entry.Binding));
                            views.Add(view);
                            native.TextureView = view.Handle;
                            dependencies.Add(view.Diagnostics);
                            break;
                        case P.GpuBindingResourceKind.Sampler:
                            SamplerLease sampler = AcquireSampler(entry.SamplerDescription);
                            samplers.Add(sampler);
                            native.Sampler = sampler.Handle;
                            dependencies.Add(sampler.Diagnostics);
                            break;
                        default: throw new ArgumentOutOfRangeException(nameof(entries));
                    }
                    nativeEntries[index] = native;
                }
                fixed (F.BindGroupEntryFFI* pointer = nativeEntries)
                {
                    var description = new F.BindGroupDescriptorFFI
                    {
                        Layout = layoutResource.Handle,
                        Entries = pointer,
                        EntryCount = (nuint)nativeEntries.Length,
                    };
                    PushScopes();
                    Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics;
                    try { handle = F.WebGPU_FFI.DeviceCreateBindGroup(device, &description); }
                    finally { diagnostics = PopScopes(); }
                    dependencies.Add(diagnostics);
                }
                if ((nuint)handle == 0)
                {
                    status.Lose("WebGPU bindings creation returned no object.");
                    status.ThrowIfFailed();
                }
                return new BindingsResource(this, handle, layoutResource, copy, views.ToArray(), samplers.ToArray(),
                    WebGpuDiagnostics.CombineAsync(dependencies.ToArray()));
            }
            catch
            {
                if ((nuint)handle != 0) { F.WebGPU_FFI.BindGroupRelease(handle); }
                foreach (TextureViewLease view in views) { ReleaseTextureView(view); }
                foreach (SamplerLease sampler in samplers) { ReleaseSampler(sampler); }
                throw;
            }
        }
    }

    public void DestroyBindings(P.GpuBindingsHandle bindings)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            BindingsResource resource = RequireBindings(bindings);
            resource.Destroyed = true;
            F.WebGPU_FFI.BindGroupRelease(resource.Handle);
            foreach (TextureViewLease view in resource.Views) { ReleaseTextureView(view); }
            foreach (SamplerLease sampler in resource.Samplers) { ReleaseSampler(sampler); }
        }
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
    {
        lock (gate) { return RequireBindings(bindings).Diagnostics; }
    }

    private static N.TextureUsage TextureBindingUsage(BindingLayoutResource layout, uint binding)
    {
        // Select the native view's intended use; layout legality and duplicate binding numbers remain native validation.
        foreach (P.GpuBindingLayoutEntry entry in layout.Entries)
        {
            if (entry.Binding == binding)
            {
                return entry.Kind == P.GpuBindingLayoutKind.StorageTexture
                    ? N.TextureUsage.StorageBinding : N.TextureUsage.TextureBinding;
            }
        }
        return N.TextureUsage.TextureBinding;
    }

    private static ulong MapBindingLength(ulong? length, string name)
    {
        if (length == ulong.MaxValue)
        { throw new ArgumentOutOfRangeException(name, "An explicit length cannot use WebGPU's whole-size sentinel."); }
        return length ?? F.WebGPU_FFI.WHOLE_SIZE;
    }
}
