using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal static class SamplerValidation
{
    internal static void Validate(SamplerDesc desc, DeviceCaps caps)
    {
        ArgumentNullException.ThrowIfNull(desc);
        if (!Enum.IsDefined(desc.MinFilter) || !Enum.IsDefined(desc.MagFilter) || !Enum.IsDefined(desc.MipmapFilter) ||
            !Enum.IsDefined(desc.AddressU) || !Enum.IsDefined(desc.AddressV) || !Enum.IsDefined(desc.AddressW) ||
            (desc.Compare is { } compare && !Enum.IsDefined(compare)) ||
            !float.IsFinite(desc.LodMinClamp) || !float.IsFinite(desc.LodMaxClamp) ||
            desc.LodMinClamp < 0 || desc.LodMaxClamp < desc.LodMinClamp || desc.MaxAnisotropy == 0 ||
            (desc.MaxAnisotropy > 1 && (desc.MinFilter != FilterMode.Linear || desc.MagFilter != FilterMode.Linear || desc.MipmapFilter != FilterMode.Linear)))
        {
            throw new ArgumentException("Invalid sampler filters, addressing, comparison, LOD or anisotropy.", nameof(desc));
        }

        if (desc.MaxAnisotropy > caps.MaxSamplerAnisotropy ||
            (desc.MaxAnisotropy > 1 && (caps.Features & GraphicsFeatures.AnisotropicFiltering) == 0))
        {
            throw new NotSupportedException("Requested sampler anisotropy exceeds enabled device capabilities.");
        }
    }
}
