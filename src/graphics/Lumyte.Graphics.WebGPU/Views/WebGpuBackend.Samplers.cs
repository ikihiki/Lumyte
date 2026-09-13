using P = Lumyte.Graphics.Portable;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private sealed class SamplerLease(P.GpuSamplerDescription description, F.SamplerHandle handle,
        Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics)
    {
        internal readonly P.GpuSamplerDescription Description = description;
        internal readonly F.SamplerHandle Handle = handle;
        internal readonly Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal int References = 1;
    }

    private readonly Dictionary<P.GpuSamplerDescription, SamplerLease> samplers = new();
    private long samplerCreations;

    private unsafe SamplerLease AcquireSampler(P.GpuSamplerDescription description)
    {
        if (samplers.TryGetValue(description, out SamplerLease? existing))
        {
            existing.References = checked(existing.References + 1);
            return existing;
        }
        var native = new F.SamplerDescriptorFFI
        {
            MinFilter = MapSamplerFilter(description.MinFilter),
            MagFilter = MapSamplerFilter(description.MagFilter),
            MipmapFilter = description.MipFilter switch
            {
                P.GpuSamplerFilter.Nearest => N.MipmapFilterMode.Nearest,
                P.GpuSamplerFilter.Linear => N.MipmapFilterMode.Linear,
                _ => throw new ArgumentOutOfRangeException(nameof(description)),
            },
            AddressModeU = MapSamplerAddressMode(description.AddressU),
            AddressModeV = MapSamplerAddressMode(description.AddressV),
            AddressModeW = MapSamplerAddressMode(description.AddressW),
            LodMinClamp = description.MinLod,
            LodMaxClamp = description.MaxLod,
            MaxAnisotropy = MapSamplerAnisotropy(description.MaxAnisotropy, nameof(description.MaxAnisotropy)),
            Compare = description.Compare.HasValue ? MapCompareFunction(description.Compare.Value) : N.CompareFunction.Undefined,
        };
        F.SamplerHandle handle = default;
        try
        {
            PushScopes();
            Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics;
            try { handle = F.WebGPU_FFI.DeviceCreateSampler(device, &native); }
            finally { diagnostics = PopScopes(); }
            if ((nuint)handle == 0)
            {
                status.Lose("WebGPU sampler creation returned no object.");
                status.ThrowIfFailed();
            }
            samplerCreations++;
            var lease = new SamplerLease(description, handle, diagnostics);
            samplers.Add(description, lease);
            return lease;
        }
        catch
        {
            if ((nuint)handle != 0) { F.WebGPU_FFI.SamplerRelease(handle); }
            throw;
        }
    }

    private void ReleaseSampler(SamplerLease lease)
    {
        if (--lease.References != 0) { return; }
        samplers.Remove(lease.Description);
        F.WebGPU_FFI.SamplerRelease(lease.Handle);
    }

    internal (int ViewEntries, int SamplerEntries, long ViewCreations, long SamplerCreations) CacheStatistics
    {
        get
        {
            lock (gate) { return (textureViews.Count, samplers.Count, textureViewCreations, samplerCreations); }
        }
    }

    private static N.FilterMode MapSamplerFilter(P.GpuSamplerFilter filter) => filter switch
    {
        P.GpuSamplerFilter.Nearest => N.FilterMode.Nearest,
        P.GpuSamplerFilter.Linear => N.FilterMode.Linear,
        _ => throw new ArgumentOutOfRangeException(nameof(filter)),
    };

    private static ushort MapSamplerAnisotropy(uint value, string name)
    {
        if (value > ushort.MaxValue)
        { throw new ArgumentOutOfRangeException(name, "Anisotropy must fit WebGPU's native ushort field."); }
        return (ushort)value;
    }

    private static N.AddressMode MapSamplerAddressMode(P.GpuSamplerAddressMode mode) => mode switch
    {
        P.GpuSamplerAddressMode.ClampToEdge => N.AddressMode.ClampToEdge,
        P.GpuSamplerAddressMode.Repeat => N.AddressMode.Repeat,
        P.GpuSamplerAddressMode.MirrorRepeat => N.AddressMode.MirrorRepeat,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static N.CompareFunction MapCompareFunction(GpuCompareOp operation) => operation switch
    {
        GpuCompareOp.Never => N.CompareFunction.Never,
        GpuCompareOp.Less => N.CompareFunction.Less,
        GpuCompareOp.Equal => N.CompareFunction.Equal,
        GpuCompareOp.LessEqual => N.CompareFunction.LessEqual,
        GpuCompareOp.Greater => N.CompareFunction.Greater,
        GpuCompareOp.NotEqual => N.CompareFunction.NotEqual,
        GpuCompareOp.GreaterEqual => N.CompareFunction.GreaterEqual,
        GpuCompareOp.Always => N.CompareFunction.Always,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
