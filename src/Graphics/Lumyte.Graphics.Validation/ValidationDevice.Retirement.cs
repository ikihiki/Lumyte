using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

/// <summary>Provides the device retirement capability boundary.</summary>
public sealed partial class ValidationDevice
{
    /// <summary>Gets no authority: backend retirement alone does not certify this validation domain's retained owners.</summary>
    /// <remarks>Unknown submissions remain quarantined. This decorator does not impersonate the raw backend authority or mint wrapper generations.</remarks>
    public IGraphicsDeviceRetirementAuthority? RetirementAuthority => null;
}
