using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Exercises sampler allocation and state through the common API only.</summary>
public static class SamplerExercise
{
    /// <summary>Checks exact sampling state, invalid inputs, capabilities and ownership.</summary>
    /// <param name="device">The already created backend device.</param>
    /// <returns>A report after all allocation checks succeed.</returns>
    public static string Run(IGraphicDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var desc = new SamplerDesc();
        Require(device.Caps.MaxSamplerAnisotropy >= 1, "Sampler anisotropy limit is invalid.");
        using IGraphicsSampler sampler = device.CreateSampler(desc);
        Require(sampler.Desc == desc, "Sampler state was changed.");
        if (device is IDisposable owner)
        {
            Expect<InvalidOperationException>(owner.Dispose);
        }

        using IGraphicsSampler baseMip = device.CreateSampler(desc with { LodMinClamp = 0, LodMaxClamp = 0 });
        Require(baseMip.Desc.LodMaxClamp == 0, "Zero LOD clamp was replaced by a default.");
        using IGraphicsSampler range = device.CreateSampler(desc with { LodMinClamp = 2, LodMaxClamp = 5 });
        Require(range.Desc.LodMinClamp == 2 && range.Desc.LodMaxClamp == 5, "LOD range was changed.");
        foreach (AddressMode address in Enum.GetValues<AddressMode>())
        {
            using IGraphicsSampler addressed = device.CreateSampler(desc with { AddressU = address, AddressV = address, AddressW = address });
            Require(addressed.Desc.AddressU == address && addressed.Desc.AddressV == address && addressed.Desc.AddressW == address, "Address mode was changed.");
        }

        foreach (FilterMode filter in Enum.GetValues<FilterMode>())
        {
            using IGraphicsSampler filtered = device.CreateSampler(desc with { MinFilter = filter, MagFilter = filter, MipmapFilter = filter });
            Require(filtered.Desc.MinFilter == filter && filtered.Desc.MagFilter == filter && filtered.Desc.MipmapFilter == filter, "Filter state was changed.");
        }

        using IGraphicsSampler mixed = device.CreateSampler(desc with { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Linear, MipmapFilter = FilterMode.Nearest });
        Require(mixed.Desc.MinFilter != mixed.Desc.MagFilter && mixed.Desc.MipmapFilter == FilterMode.Nearest, "Independent filters were coupled.");
        foreach (CompareFunction compare in Enum.GetValues<CompareFunction>())
        {
            using IGraphicsSampler comparison = device.CreateSampler(desc with { Compare = compare });
            Require(comparison.Desc.Compare == compare, "Comparison state was changed.");
        }

        Expect<ArgumentNullException>(() => device.CreateSampler(null!));
        Expect<ArgumentException>(() => device.CreateSampler(desc with { MinFilter = (FilterMode)99 }));
        Expect<ArgumentException>(() => device.CreateSampler(desc with { MagFilter = (FilterMode)99 }));
        Expect<ArgumentException>(() => device.CreateSampler(desc with { MipmapFilter = (FilterMode)99 }));
        Expect<ArgumentException>(() => device.CreateSampler(desc with { AddressU = (AddressMode)99 }));
        Expect<ArgumentException>(() => device.CreateSampler(desc with { AddressV = (AddressMode)99 }));
        Expect<ArgumentException>(() => device.CreateSampler(desc with { AddressW = (AddressMode)99 }));
        Expect<ArgumentException>(() => device.CreateSampler(desc with { Compare = (CompareFunction)99 }));
        Expect<ArgumentException>(() => device.CreateSampler(desc with { LodMinClamp = -1 }));
        Expect<ArgumentException>(() => device.CreateSampler(desc with { LodMaxClamp = -1 }));
        Expect<ArgumentException>(() => device.CreateSampler(desc with { LodMinClamp = 4, LodMaxClamp = 3 }));
        foreach (float nonFinite in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Expect<ArgumentException>(() => device.CreateSampler(desc with { LodMinClamp = nonFinite }));
            Expect<ArgumentException>(() => device.CreateSampler(desc with { LodMaxClamp = nonFinite }));
        }

        Expect<ArgumentException>(() => device.CreateSampler(desc with { MaxAnisotropy = 0 }));
        Expect<ArgumentException>(() => device.CreateSampler(desc with { MaxAnisotropy = 2, MinFilter = FilterMode.Nearest }));
        Expect<ArgumentException>(() => device.CreateSampler(desc with { MaxAnisotropy = 2, MagFilter = FilterMode.Nearest }));
        Expect<ArgumentException>(() => device.CreateSampler(desc with { MaxAnisotropy = 2, MipmapFilter = FilterMode.Nearest }));
        Expect<NotSupportedException>(() => device.CreateSampler(desc with { MaxAnisotropy = ushort.MaxValue }));
        if (device.Caps.MaxSamplerAnisotropy > 1)
        {
            using IGraphicsSampler anisotropic = device.CreateSampler(desc with { MaxAnisotropy = device.Caps.MaxSamplerAnisotropy });
            Require(anisotropic.Desc.MaxAnisotropy == device.Caps.MaxSamplerAnisotropy, "Anisotropy was corrected implicitly.");
        }
        else
        {
            Expect<NotSupportedException>(() => device.CreateSampler(desc with { MaxAnisotropy = 2 }));
        }

        sampler.Dispose();
        sampler.Dispose();
        return "Sampler checks passed: filters, addressing, LOD, comparison, anisotropy and lifetime.";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Expect<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
