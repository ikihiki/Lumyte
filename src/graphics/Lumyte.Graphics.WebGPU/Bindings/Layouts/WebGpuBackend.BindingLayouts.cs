using P = Lumyte.Graphics.Portable;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private sealed class BindingLayoutResource(WebGpuBackend owner, F.BindGroupLayoutHandle handle,
        P.GpuBindingLayoutEntry[] entries, Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics) : P.GpuBindingLayoutHandle
    {
        internal readonly WebGpuBackend Owner = owner;
        internal readonly F.BindGroupLayoutHandle Handle = handle;
        internal readonly P.GpuBindingLayoutEntry[] Entries = entries;
        internal readonly Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal bool Destroyed;
    }

    public unsafe P.GpuBindingLayoutHandle CreateBindingLayout(ReadOnlySpan<P.GpuBindingLayoutEntry> entries)
    {
        P.GpuBindingLayoutEntry[] copy = entries.ToArray();
        var nativeEntries = new N.BindGroupLayoutEntry[copy.Length];
        for (int index = 0; index < copy.Length; index++) { nativeEntries[index] = MapBindingLayoutEntry(copy[index]); }
        lock (gate)
        {
            RequireAvailable();
            F.BindGroupLayoutHandle handle = default;
            try
            {
                Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics;
                fixed (N.BindGroupLayoutEntry* pointer = nativeEntries)
                {
                    var description = new F.BindGroupLayoutDescriptorFFI { Entries = pointer, EntryCount = (nuint)copy.Length };
                    PushScopes();
                    try { handle = F.WebGPU_FFI.DeviceCreateBindGroupLayout(device, &description); }
                    finally { diagnostics = PopScopes(); }
                }
                if ((nuint)handle == 0)
                {
                    status.Lose("WebGPU binding layout creation returned no object.");
                    status.ThrowIfFailed();
                }
                return new BindingLayoutResource(this, handle, copy, diagnostics);
            }
            catch
            {
                if ((nuint)handle != 0) { F.WebGPU_FFI.BindGroupLayoutRelease(handle); }
                throw;
            }
        }
    }

    public void DestroyBindingLayout(P.GpuBindingLayoutHandle layout)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            BindingLayoutResource resource = RequireBindingLayout(layout);
            resource.Destroyed = true;
            F.WebGPU_FFI.BindGroupLayoutRelease(resource.Handle);
        }
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
    {
        lock (gate) { return RequireBindingLayout(layout).Diagnostics; }
    }

    private static unsafe N.BindGroupLayoutEntry MapBindingLayoutEntry(P.GpuBindingLayoutEntry entry)
    {
        var result = new N.BindGroupLayoutEntry { Binding = entry.Binding, Visibility = MapShaderStage(entry.Visibility) };
        switch (entry.Kind)
        {
            case P.GpuBindingLayoutKind.Undefined: break;
            case P.GpuBindingLayoutKind.Buffer:
                result.Buffer = new()
                {
                    Type = entry.BufferLayout.Type switch
                    {
                        P.GpuBufferBindingType.Uniform => N.BufferBindingType.Uniform,
                        P.GpuBufferBindingType.ReadOnlyStorage => N.BufferBindingType.ReadOnlyStorage,
                        P.GpuBufferBindingType.Storage => N.BufferBindingType.Storage,
                        _ => throw new ArgumentOutOfRangeException(nameof(entry)),
                    },
                    MinBindingSize = entry.BufferLayout.MinBindingSize,
                    HasDynamicOffset = entry.BufferLayout.HasDynamicOffset,
                };
                break;
            case P.GpuBindingLayoutKind.Texture:
                result.Texture = new()
                {
                    SampleType = entry.TextureLayout.SampleType switch
                    {
                        P.GpuTextureSampleType.Float => N.TextureSampleType.Float,
                        P.GpuTextureSampleType.UnfilterableFloat => N.TextureSampleType.UnfilterableFloat,
                        P.GpuTextureSampleType.Depth => N.TextureSampleType.Depth,
                        P.GpuTextureSampleType.Sint => N.TextureSampleType.Sint,
                        P.GpuTextureSampleType.Uint => N.TextureSampleType.Uint,
                        _ => throw new ArgumentOutOfRangeException(nameof(entry)),
                    },
                    ViewDimension = MapViewDimension(entry.TextureLayout.ViewDimension),
                    Multisampled = entry.TextureLayout.Multisampled,
                };
                break;
            case P.GpuBindingLayoutKind.StorageTexture:
                result.StorageTexture = new()
                {
                    Access = entry.StorageTextureLayout.Access switch
                    {
                        P.GpuStorageTextureAccess.ReadOnly => N.StorageTextureAccess.ReadOnly,
                        P.GpuStorageTextureAccess.WriteOnly => N.StorageTextureAccess.WriteOnly,
                        P.GpuStorageTextureAccess.ReadWrite => N.StorageTextureAccess.ReadWrite,
                        _ => throw new ArgumentOutOfRangeException(nameof(entry)),
                    },
                    Format = MapTextureFormat(entry.StorageTextureLayout.Format),
                    ViewDimension = MapViewDimension(entry.StorageTextureLayout.ViewDimension),
                };
                break;
            case P.GpuBindingLayoutKind.Sampler:
                result.Sampler = new()
                {
                    Type = entry.SamplerLayout.Type switch
                    {
                        P.GpuSamplerBindingType.Filtering => N.SamplerBindingType.Filtering,
                        P.GpuSamplerBindingType.NonFiltering => N.SamplerBindingType.NonFiltering,
                        P.GpuSamplerBindingType.Comparison => N.SamplerBindingType.Comparison,
                        _ => throw new ArgumentOutOfRangeException(nameof(entry)),
                    },
                };
                break;
            default: throw new ArgumentOutOfRangeException(nameof(entry));
        }
        return result;
    }

    private static N.ShaderStage MapShaderStage(P.GpuShaderStage stage)
    {
        const P.GpuShaderStage known = P.GpuShaderStage.Vertex | P.GpuShaderStage.Pixel | P.GpuShaderStage.Compute;
        if ((stage & ~known) != 0) { throw new ArgumentOutOfRangeException(nameof(stage)); }
        N.ShaderStage result = 0;
        if ((stage & P.GpuShaderStage.Vertex) != 0) { result |= N.ShaderStage.Vertex; }
        if ((stage & P.GpuShaderStage.Pixel) != 0) { result |= N.ShaderStage.Fragment; }
        if ((stage & P.GpuShaderStage.Compute) != 0) { result |= N.ShaderStage.Compute; }
        return result;
    }
}
