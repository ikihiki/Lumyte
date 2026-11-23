namespace Lumyte.Pipelines;

internal abstract class Node(IStageFactory factory, int[] inputs, Type outputType, SchemaDescriptor? hostSchema, Type implementationType, string placement, Func<SchemaDescriptor?> schemaProvider)
{
    internal IStageFactory Factory { get; } = factory;

    internal int[] Inputs { get; } = inputs;

    internal Type OutputType { get; } = outputType;

    internal SchemaDescriptor? HostSchema { get; } = hostSchema;

    internal Type ImplementationType { get; } = implementationType;

    internal string Placement { get; } = placement;

    internal void ValidateSchema() => SchemaMetadata.Validate(HostSchema, schemaProvider());

    internal abstract Processor Create(StageBuildContext context);
}
