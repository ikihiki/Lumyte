namespace Lumyte.Pipelines;

internal sealed class StageNode<TA, TO>(IStageFactory<TA, TO> factory, int[] inputs, Type implementationType) : Node(factory, inputs, typeof(TO), default(TO) is IHostPlacement host ? host.Descriptor : default(TO) is INonCpuReadablePlacement device ? device.Descriptor : null, implementationType, default(TO) is IHostPlacement ? "Host" : default(TO) is INonCpuReadablePlacement extension ? extension.DiagnosticPlacement : "Value", SchemaMetadata.For<TO>)
{
    internal override Processor Create(StageBuildContext context) => new TypedProcessor(factory.Create(context) ?? throw new InvalidOperationException("The stage factory returned a null processor."));

    private sealed class TypedProcessor(IStageProcessor<TA, TO> processor) : Processor
    {
        public override void Dispose() => processor.Dispose();

        internal override ValueTask<object> PrepareAsync(StagePreparationContext context, object[] inputs, CancellationToken cancellationToken)
        {
            ValueTask<ValueLease<TO>> operation = processor.PrepareAsync(context, (ValueLease<TA>)inputs[0], cancellationToken);
            if (operation.IsCompletedSuccessfully)
            {
                ValueLease<TO> output = operation.Result;
                context.ValidateOutput(output);
                return new ValueTask<object>(output);
            }

            return AwaitOutputAsync(operation, context);
        }

        private static async ValueTask<object> AwaitOutputAsync(ValueTask<ValueLease<TO>> operation, StagePreparationContext context)
        {
            ValueLease<TO> output = await operation;
            context.ValidateOutput(output);
            return output;
        }
    }
}
