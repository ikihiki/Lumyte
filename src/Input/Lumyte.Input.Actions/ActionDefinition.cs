namespace Lumyte.Input.Actions;
/// <summary>Defines action definition.</summary>
/// <param name = "Id">The Id value.</param>
/// <param name = "Kind">The Kind value.</param>
/// <param name = "Sensitivity">The Sensitivity value.</param>
/// <param name = "Normalize">The Normalize value.</param>
/// <param name = "SmoothingSeconds">The SmoothingSeconds value.</param>
public sealed record ActionDefinition(string Id, ActionValueKind Kind, float Sensitivity = 1, bool Normalize = false, float SmoothingSeconds = 0);
