namespace Lumyte.UI.Windowing;

/// <summary>A detached UiWindowId value.</summary>
/// <param name="ManagerId">The ManagerId value.</param>
/// <param name="Value">The Value value.</param>
public readonly record struct UiWindowId(Guid ManagerId, ulong Value);
