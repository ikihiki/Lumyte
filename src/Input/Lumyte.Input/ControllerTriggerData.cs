namespace Lumyte.Input;

/// <summary>Records a trigger value from zero to one.</summary>
/// <param name="Trigger">The Trigger value.</param>
/// <param name="Value">The Value value.</param>
public sealed record ControllerTriggerData(ControllerTrigger Trigger, float Value) : InputData;
