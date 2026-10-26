namespace Lumyte.Resources;

/// <summary>Selects the execution environment for the stage producing one exact output type.</summary>
/// <param name="OutputType">The output type of a registered stage.</param>
/// <param name="Runtime">The borrowed, stable environment used for construction and destruction.</param>
/// <remarks>The default struct value is not a valid binding.</remarks>
public readonly record struct ResourceStageRuntime(Type OutputType, IResourceRuntime Runtime);
