namespace Lumyte.Pipelines;

internal abstract class Node(IStageFactory factory, int[] inputs, Type outputType, SchemaDescriptor? hostSchema, Type implementationType, string placement, Func<SchemaDescriptor?> schemaProvider, ValueShape capturedShape, Action<ValueShape> validateShape)
{
    internal IStageFactory Factory { get; } = factory;

    internal int[] Inputs { get; } = inputs;

    internal Type OutputType { get; } = outputType;

    internal SchemaDescriptor? HostSchema { get; } = hostSchema;

    internal Type ImplementationType { get; } = implementationType;

    internal string Placement { get; } = placement;

    internal ValueShape CapturedShape { get; } = capturedShape;

    internal void ValidateSchema()
    {
        SchemaMetadata.Validate(HostSchema, schemaProvider());
        if (CapturedShape.IsComposite)
        {
            validateShape(CapturedShape);
        }
    }

    internal abstract void Visit(IPipelineDefinitionVisitor visitor, int output);

    internal abstract Node Rebind(int[] inputs);

    internal abstract Processor Create(StageBuildContext context);
}
