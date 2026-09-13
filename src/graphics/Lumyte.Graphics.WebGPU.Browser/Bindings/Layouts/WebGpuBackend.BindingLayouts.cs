using System.Runtime.InteropServices.JavaScript;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser;

public sealed partial class WebGpuBackend
{
    private sealed class BindingLayoutResource(WebGpuBackend owner, JSObject handle, P.GpuBindingLayoutEntry[] entries,
        Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics) : P.GpuBindingLayoutHandle
    {
        internal readonly WebGpuBackend Owner = owner;
        internal readonly JSObject Handle = handle;
        internal readonly P.GpuBindingLayoutEntry[] Entries = entries;
        internal readonly Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal bool Destroyed;
    }

    public P.GpuBindingLayoutHandle CreateBindingLayout(ReadOnlySpan<P.GpuBindingLayoutEntry> entries)
    {
        RequireAvailable();
        P.GpuBindingLayoutEntry[] copy = entries.ToArray();
        var native = new object[copy.Length];
        for (int index = 0; index < copy.Length; index++) { native[index] = MapBindingLayoutEntry(copy[index]); }
        var created = CreateObject(device, "bindingLayout", new { Entries = native }, []);
        try { return new BindingLayoutResource(this, created.Handle, copy, created.Diagnostics); }
        catch { created.Handle.Dispose(); throw; }
    }

    public void DestroyBindingLayout(P.GpuBindingLayoutHandle layout)
    {
        runtime.RequireThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        BindingLayoutResource resource = RequireBindingLayout(layout);
        resource.Destroyed = true;
        resource.Handle.Dispose();
    }

    private BindingLayoutResource RequireBindingLayout(P.GpuBindingLayoutHandle layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout is not BindingLayoutResource resource || !ReferenceEquals(resource.Owner, this))
        { throw new ArgumentException("Binding layout belongs to another device.", nameof(layout)); }
        ObjectDisposedException.ThrowIf(resource.Destroyed, layout);
        return resource;
    }

    internal Task<IReadOnlyList<P.GpuDiagnostic>> GetCreationDiagnostics(P.GpuBindingLayoutHandle layout)
    { runtime.RequireThread(); return RequireBindingLayout(layout).Diagnostics; }

    private static object MapBindingLayoutEntry(P.GpuBindingLayoutEntry entry)
    {
        object? buffer = null;
        object? texture = null;
        object? storageTexture = null;
        object? sampler = null;
        switch (entry.Kind)
        {
            case P.GpuBindingLayoutKind.Undefined: break;
            case P.GpuBindingLayoutKind.Buffer:
                buffer = new
                {
                    Type = entry.BufferLayout.Type switch
                    {
                        P.GpuBufferBindingType.Uniform => "uniform",
                        P.GpuBufferBindingType.ReadOnlyStorage => "read-only-storage",
                        P.GpuBufferBindingType.Storage => "storage",
                        _ => throw new ArgumentOutOfRangeException(nameof(entry)),
                    },
                    MinBindingSize = Exact(entry.BufferLayout.MinBindingSize, nameof(entry)),
                    entry.BufferLayout.HasDynamicOffset,
                };
                break;
            case P.GpuBindingLayoutKind.Texture:
                texture = new
                {
                    SampleType = entry.TextureLayout.SampleType switch
                    {
                        P.GpuTextureSampleType.Float => "float",
                        P.GpuTextureSampleType.UnfilterableFloat => "unfilterable-float",
                        P.GpuTextureSampleType.Depth => "depth",
                        P.GpuTextureSampleType.Sint => "sint",
                        P.GpuTextureSampleType.Uint => "uint",
                        _ => throw new ArgumentOutOfRangeException(nameof(entry)),
                    },
                    ViewDimension = MapViewDimension(entry.TextureLayout.ViewDimension),
                    entry.TextureLayout.Multisampled,
                };
                break;
            case P.GpuBindingLayoutKind.StorageTexture:
                storageTexture = new
                {
                    Access = entry.StorageTextureLayout.Access switch
                    {
                        P.GpuStorageTextureAccess.ReadOnly => "read-only",
                        P.GpuStorageTextureAccess.WriteOnly => "write-only",
                        P.GpuStorageTextureAccess.ReadWrite => "read-write",
                        _ => throw new ArgumentOutOfRangeException(nameof(entry)),
                    },
                    Format = MapTextureFormat(entry.StorageTextureLayout.Format),
                    ViewDimension = MapViewDimension(entry.StorageTextureLayout.ViewDimension),
                };
                break;
            case P.GpuBindingLayoutKind.Sampler:
                sampler = new
                {
                    Type = entry.SamplerLayout.Type switch
                    {
                        P.GpuSamplerBindingType.Filtering => "filtering",
                        P.GpuSamplerBindingType.NonFiltering => "non-filtering",
                        P.GpuSamplerBindingType.Comparison => "comparison",
                        _ => throw new ArgumentOutOfRangeException(nameof(entry)),
                    },
                };
                break;
            default: throw new ArgumentOutOfRangeException(nameof(entry));
        }
        return new { entry.Binding, Visibility = MapShaderStage(entry.Visibility), Buffer = buffer,
            Texture = texture, StorageTexture = storageTexture, Sampler = sampler };
    }

    private static uint MapShaderStage(P.GpuShaderStage stage)
    {
        const P.GpuShaderStage known = P.GpuShaderStage.Vertex | P.GpuShaderStage.Pixel | P.GpuShaderStage.Compute;
        if ((stage & ~known) != 0) { throw new ArgumentOutOfRangeException(nameof(stage)); }
        return (uint)stage;
    }
}
