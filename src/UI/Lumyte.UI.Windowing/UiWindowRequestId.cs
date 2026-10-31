namespace Lumyte.UI.Windowing;

/// <summary>A detached UiWindowRequestId value.</summary>
/// <param name="ManagerId">The ManagerId value.</param>
/// <param name="Value">The Value value.</param>
public readonly record struct UiWindowRequestId(Guid ManagerId, ulong Value);
