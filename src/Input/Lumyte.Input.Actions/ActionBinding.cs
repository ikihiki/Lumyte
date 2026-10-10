namespace Lumyte.Input.Actions;
/// <summary>Defines action binding.</summary>
/// <param name = "Id">The Id value.</param>
/// <param name = "ActionId">The ActionId value.</param>
/// <param name = "ContextId">The ContextId value.</param>
/// <param name = "Control">The Control value.</param>
/// <param name = "Scale">The Scale value.</param>
/// <param name = "PressThreshold">The PressThreshold value.</param>
/// <param name = "ReleaseThreshold">The ReleaseThreshold value.</param>
public sealed record ActionBinding(string Id, string ActionId, string ContextId, InputControl Control, System.Numerics.Vector2 Scale, float PressThreshold = 0.5f, float ReleaseThreshold = 0.4f);
