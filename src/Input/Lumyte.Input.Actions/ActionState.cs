namespace Lumyte.Input.Actions;
/// <summary>Defines action state.</summary>
/// <param name = "Kind">The Kind value.</param>
/// <param name = "Value">The Value value.</param>
public sealed record ActionState(ActionValueKind Kind, System.Numerics.Vector2 Value);
