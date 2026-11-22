namespace Lumyte.Pipelines;

internal sealed class StageNode<TA, TB, TO>(IStageFactory<TA, TB, TO> factory, int[] inputs, Type implementationType) : Node(factory, inputs, typeof(TO), default(TO) is IHostPlacement host ? host.Descriptor : default(TO) is INonCpuReadablePlacement device ? device.Descriptor : null, implementationType, default(TO) is IHostPlacement ? "Host" : default(TO) is INonCpuReadablePlacement extension ? extension.DiagnosticPlacement : "Value", SchemaMetadata.For<TO>)
{
    internal override Processor Create(StageBuildContext context) => new TypedProcessor(factory.Create(context) ?? throw new InvalidOperationException("The stage factory returned a null processor."));

    private sealed class TypedProcessor(IStageProcessor<TA, TB, TO> processor) : Processor
    {
        public override void Dispose() => processor.Dispose();

        internal override async ValueTask<object> PrepareAsync(StagePreparationContext context, object[] inputs, CancellationToken cancellationToken)
        {
            ValueLease<TO> output = await processor.PrepareAsync(context, (ValueLease<TA>)inputs[0], (ValueLease<TB>)inputs[1], cancellationToken);
            context.ValidateOutput(output);
            return output;
        }
    }
}
