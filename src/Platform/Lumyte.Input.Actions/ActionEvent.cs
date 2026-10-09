namespace Lumyte.Input.Actions;
/// <summary>Defines action event.</summary>
/// <param name = "ActionId">The ActionId value.</param>
/// <param name = "Phase">The Phase value.</param>
/// <param name = "Value">The Value value.</param>
/// <param name = "At">The At value.</param>
/// <param name = "Devices">The Devices value.</param>
/// <param name = "ContextId">The ContextId value.</param>
public sealed record ActionEvent(string ActionId, ActionPhase Phase, System.Numerics.Vector2 Value, TimeSpan At, System.Collections.Immutable.ImmutableArray<InputDeviceId> Devices, string ContextId);
