namespace Lumyte.Input;

/// <summary>Identifies a touch contact without reuse within a device lifetime.</summary>
/// <param name="Value">The Value value.</param>
public readonly record struct TouchContactId(ulong Value);
