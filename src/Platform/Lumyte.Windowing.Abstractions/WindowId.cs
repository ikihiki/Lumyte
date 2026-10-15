namespace Lumyte.Windowing.Abstractions;

/// <summary>Defines WindowId.</summary>
/// <param name="PlatformId">The PlatformId value.</param>
/// <param name="Value">The Value value.</param>
public readonly record struct WindowId(Guid PlatformId, ulong Value);
