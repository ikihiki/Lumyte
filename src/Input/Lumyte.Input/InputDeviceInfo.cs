namespace Lumyte.Input;

/// <summary>Represents InputDeviceInfo.</summary>
/// <param name="Id">The Id value.</param>
/// <param name="Kind">The Kind value.</param>
/// <param name="Name">The Name value.</param>
/// <param name="IdentityKind">The IdentityKind value.</param>
public sealed record InputDeviceInfo(InputDeviceId Id, InputDeviceKind Kind, string Name, InputDeviceIdentityKind IdentityKind);
