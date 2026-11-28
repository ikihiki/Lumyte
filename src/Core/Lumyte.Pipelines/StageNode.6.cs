namespace Lumyte.Pipelines;

internal sealed class StageNode<TA, TB, TC, TD, TE, TF, TO>(IStageFactory<TA, TB, TC, TD, TE, TF, TO> factory, int[] inputs, Type implementationType, ValueShape? capturedShape = null) : Node(factory, inputs, typeof(TO), default(TO) is IHostPlacement host ? host.Descriptor : default(TO) is INonCpuReadablePlacement device ? device.Descriptor : null, implementationType, default(TO) is IHostPlacement ? "Host" : default(TO) is INonCpuReadablePlacement extension ? extension.DiagnosticPlacement : "Value", SchemaMetadata.For<TO>, capturedShape ?? ValueShape.Capture(default(TO)), shape => shape.Validate(ValueShape.Capture(default(TO))))
{
    internal override void Visit(IPipelineDefinitionVisitor visitor, int output) => visitor.Stage(factory, (new PipelineEdge<TA>(Inputs[0]), new PipelineEdge<TB>(Inputs[1]), new PipelineEdge<TC>(Inputs[2]), new PipelineEdge<TD>(Inputs[3]), new PipelineEdge<TE>(Inputs[4]), new PipelineEdge<TF>(Inputs[5])), new PipelineEdge<TO>(output));

    internal override Node Rebind(int[] inputs) => new StageNode<TA, TB, TC, TD, TE, TF, TO>(factory, inputs, ImplementationType, CapturedShape);

    internal override Processor Create(StageBuildContext context) => new TypedProcessor(factory.Create(context) ?? throw new InvalidOperationException("The stage factory returned a null processor."));

    private sealed class TypedProcessor(IStageProcessor<TA, TB, TC, TD, TE, TF, TO> processor) : Processor
    {
        public override void Dispose() => processor.Dispose();

        internal override ValueTask<object> PrepareAsync(StagePreparationContext context, object[] inputs, CancellationToken cancellationToken)
        {
            ValueTask<ValueLease<TO>> operation = processor.PrepareAsync(context, (ValueLease<TA>)inputs[0], (ValueLease<TB>)inputs[1], (ValueLease<TC>)inputs[2], (ValueLease<TD>)inputs[3], (ValueLease<TE>)inputs[4], (ValueLease<TF>)inputs[5], cancellationToken);
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
