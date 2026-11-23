namespace Lumyte.Pipelines;

/// <summary>Marks an application-owned immutable metadata snapshot; implementations must capture all mutable inputs.</summary>
/// <remarks>Metadata is retained on the host and is never serialized by an explicit canonical upload.</remarks>
public interface IImmutableSchemaMetadata;
