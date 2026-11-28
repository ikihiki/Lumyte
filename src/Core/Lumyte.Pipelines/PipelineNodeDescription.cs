namespace Lumyte.Pipelines;

/// <summary>A captured declaration node, without runtime handles.</summary>
/// <param name="Index">The declaration index.</param>
/// <param name="ImplementationType">The captured factory implementation.</param>
/// <param name="OutputType">The typed output.</param>
/// <param name="Inputs">The immutable producer indices.</param>
/// <param name="Schema">The captured canonical schema, when present.</param>
/// <param name="Placement">The diagnostic placement; not an implementation selector.</param>
public sealed record PipelineNodeDescription(int Index, Type ImplementationType, Type OutputType, IReadOnlyList<int> Inputs, SchemaDescriptor? Schema = null, string Placement = "Value");
