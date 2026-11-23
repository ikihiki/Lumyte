namespace Lumyte.Graphics.Abstractions;

/// <summary>Identifies a device lifetime without exposing issuance capabilities.</summary>
public sealed class GraphicsDeviceGeneration
{
    private readonly IGraphicDevice _device;
    private readonly IGraphicsDeviceRetirementAuthority? _authority;
    private readonly object? _issuer;

    private GraphicsDeviceGeneration(IGraphicDevice device, IGraphicsDeviceRetirementAuthority? authority = null, object? issuer = null)
    {
        _device = device;
        _authority = authority;
        _issuer = issuer;
    }

    internal static GraphicsDeviceGeneration CreateLocal(IGraphicDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("A local generation requires a ready device.");
        }

        return new(device);
    }

    internal static GraphicsDeviceGeneration CreateTrusted(IGraphicDevice device, IGraphicsDeviceRetirementAuthority authority, object issuer)
    {
        GraphicsRetirementIssuer.Validate(device, authority, issuer);
        return new(device, authority, issuer);
    }

    internal void ValidateAuthority(IGraphicsDeviceRetirementAuthority authority)
    {
        if (_authority is null || !ReferenceEquals(_authority, authority) || !ReferenceEquals(authority.Generation, this))
        {
            throw new ArgumentException("The authority did not issue this generation.", nameof(authority));
        }

        ValidateDevice(authority.Device);
    }

    internal void ValidateIssuer(object issuer)
    {
        if (_issuer is null || !ReferenceEquals(_issuer, issuer))
        {
            throw new ArgumentException("A private backend issuer capability is required.", nameof(issuer));
        }
    }

    internal void ValidateDevice(IGraphicDevice device)
    {
        if (!ReferenceEquals(_device, device))
        {
            throw new ArgumentException("The generation belongs to another device.", nameof(device));
        }
    }
}
