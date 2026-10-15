namespace Lumyte.Windowing;

/// <summary>Defines WindowNodeId.</summary>
/// <param name="SystemId">The SystemId value.</param>
/// <param name="Value">The Value value.</param>
internal readonly record struct WindowNodeId(Guid SystemId, ulong Value);
