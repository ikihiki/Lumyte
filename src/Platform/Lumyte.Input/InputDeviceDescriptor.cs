namespace Lumyte.Input;

/// <summary>Represents InputDeviceDescriptor.</summary>
/// <param name="Kind">The Kind value.</param>
/// <param name="Name">The Name value.</param>
/// <param name="IdentityKind">The IdentityKind value.</param>
public sealed record InputDeviceDescriptor(InputDeviceKind Kind, string Name, InputDeviceIdentityKind IdentityKind);
