namespace Lumyte.Input.Actions;
/// <summary>Defines recognition definition.</summary>
/// <param name = "Id">The Id value.</param>
/// <param name = "ContextId">The ContextId value.</param>
/// <param name = "Kind">The Kind value.</param>
/// <param name = "Actions">The Actions value.</param>
/// <param name = "Window">The Window value.</param>
/// <param name = "TapCount">The TapCount value.</param>
public sealed record RecognitionDefinition(string Id, string ContextId, RecognitionKind Kind, System.Collections.Immutable.ImmutableArray<string> Actions, TimeSpan Window, int TapCount = 2);
