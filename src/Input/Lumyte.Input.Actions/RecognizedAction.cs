namespace Lumyte.Input.Actions;
/// <summary>Defines recognized action.</summary>
/// <param name = "RecognitionId">The RecognitionId value.</param>
/// <param name = "ContextId">The ContextId value.</param>
/// <param name = "At">The At value.</param>
/// <param name = "Value">The Value value.</param>
/// <param name = "Devices">The Devices value.</param>
public sealed record RecognizedAction(string RecognitionId, string ContextId, TimeSpan At, System.Numerics.Vector2 Value, System.Collections.Immutable.ImmutableArray<InputDeviceId> Devices);
