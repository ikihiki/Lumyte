using System.Runtime.InteropServices.JavaScript;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser;

public sealed partial class WebGpuBackend
{
    private sealed class SamplerLease(P.GpuSamplerDescription description, JSObject handle,
        Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics)
    {
        internal readonly P.GpuSamplerDescription Description = description;
        internal readonly JSObject Handle = handle;
        internal readonly Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal int References = 1;
    }
    private readonly Dictionary<P.GpuSamplerDescription, SamplerLease> samplers = [];
    private long samplerCreations;

    private SamplerLease AcquireSampler(P.GpuSamplerDescription description)
    {
        if (samplers.TryGetValue(description, out SamplerLease? existing))
        {
            existing.References = checked(existing.References + 1);
            return existing;
        }
        var created = CreateObject(device, "sampler", new
        {
            MinFilter = MapSamplerFilter(description.MinFilter),
            MagFilter = MapSamplerFilter(description.MagFilter),
            MipmapFilter = MapSamplerFilter(description.MipFilter),
            AddressModeU = MapSamplerAddressMode(description.AddressU),
            AddressModeV = MapSamplerAddressMode(description.AddressV),
            AddressModeW = MapSamplerAddressMode(description.AddressW),
            LodMinClamp = description.MinLod,
            LodMaxClamp = description.MaxLod,
            description.MaxAnisotropy,
            Compare = description.Compare.HasValue ? MapCompareFunction(description.Compare.Value) : null,
        }, []);
        try
        {
            var lease = new SamplerLease(description, created.Handle, created.Diagnostics);
            samplers.Add(description, lease);
            samplerCreations++;
            return lease;
        }
        catch { created.Handle.Dispose(); throw; }
    }

    private void ReleaseSampler(SamplerLease lease)
    {
        if (--lease.References != 0) { return; }
        samplers.Remove(lease.Description);
        lease.Handle.Dispose();
    }

    internal (int ViewEntries, int SamplerEntries, long ViewCreations, long SamplerCreations) CacheStatistics
    {
        get { runtime.RequireThread(); return (textureViews.Count, samplers.Count, textureViewCreations, samplerCreations); }
    }

    private static string MapSamplerFilter(P.GpuSamplerFilter filter) => filter switch
    {
        P.GpuSamplerFilter.Nearest => "nearest",
        P.GpuSamplerFilter.Linear => "linear",
        _ => throw new ArgumentOutOfRangeException(nameof(filter)),
    };

    private static string MapSamplerAddressMode(P.GpuSamplerAddressMode mode) => mode switch
    {
        P.GpuSamplerAddressMode.ClampToEdge => "clamp-to-edge",
        P.GpuSamplerAddressMode.Repeat => "repeat",
        P.GpuSamplerAddressMode.MirrorRepeat => "mirror-repeat",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static string MapCompareFunction(GpuCompareOp operation) => operation switch
    {
        GpuCompareOp.Never => "never",
        GpuCompareOp.Less => "less",
        GpuCompareOp.Equal => "equal",
        GpuCompareOp.LessEqual => "less-equal",
        GpuCompareOp.Greater => "greater",
        GpuCompareOp.NotEqual => "not-equal",
        GpuCompareOp.GreaterEqual => "greater-equal",
        GpuCompareOp.Always => "always",
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
