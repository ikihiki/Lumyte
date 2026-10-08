namespace Lumyte.Input;

/// <summary>Identifies a device uniquely within one input system lifetime.</summary>
/// <param name="Value">The Value value.</param>
public readonly record struct InputDeviceId(ulong Value);
