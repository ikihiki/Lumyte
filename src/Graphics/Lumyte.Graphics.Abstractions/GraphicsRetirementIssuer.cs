namespace Lumyte.Graphics.Abstractions;

// All friends share internals: access modifiers alone cannot protect issuance.
// Both adapter and nonce must be genuine private nested types of an allowlisted
// device. Neither nonce is exposed; issuance subsequently checks exact identity.
// Pipelines can call this broker, but cannot construct the required capability.
// Reflection/unsafe or replacement of trusted assemblies are outside this public
// capability model. Type-name checks are confined to this issuer boundary.
internal static class GraphicsRetirementIssuer
{
    internal static void Validate(IGraphicDevice device, IGraphicsDeviceRetirementAuthority authority, object issuer)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(issuer);
        Type owner = device.GetType();
        string? assembly = owner.Assembly.GetName().Name;
        bool trusted = (assembly, owner.FullName) switch
        {
            ("Lumyte.Graphics.Vulkan", "Lumyte.Graphics.Vulkan.VulkanDevice") => true,
            ("Lumyte.Graphics.Wgpu", "Lumyte.Graphics.Wgpu.WgpuDevice") => true,
            ("Lumyte.Graphics.Direct3D12", "Lumyte.Graphics.Direct3D12.Direct3D12Device") => true,
            ("Lumyte.Graphics.Browser", "Lumyte.Graphics.Browser.BrowserDevice") => true,
            _ => false,
        };
        if (!trusted || !IsPrivateOwnedType(issuer.GetType(), owner, "RetirementIssuer") ||
            !IsPrivateOwnedType(authority.GetType(), owner, "RetirementAuthorityAdapter") ||
            !ReferenceEquals(authority.Device, device))
        {
            throw new ArgumentException("A genuine device-private backend retirement capability is required.", nameof(issuer));
        }
    }

    private static bool IsPrivateOwnedType(Type type, Type owner, string name)
        => type.IsNestedPrivate && type.IsSealed && type.DeclaringType == owner && type.Assembly == owner.Assembly && type.Name == name;
}
