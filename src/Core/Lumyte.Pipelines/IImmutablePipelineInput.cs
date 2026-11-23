namespace Lumyte.Pipelines;

/// <summary>Describes immutable application input metadata without inspecting domain-specific fields.</summary>
public interface IImmutablePipelineInput : IImmutableSchemaMetadata
{
    /// <summary>Gets the immutable actual-input description used before stage allocation.</summary>
    SchemaExtent Description { get; }
}
