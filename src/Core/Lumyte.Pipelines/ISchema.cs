namespace Lumyte.Pipelines;

/// <summary>Defines a canonical immutable element layout.</summary>
public interface ISchema
{
    /// <summary>Gets the canonical descriptor.</summary>
    static abstract SchemaDescriptor Descriptor { get; }
}
