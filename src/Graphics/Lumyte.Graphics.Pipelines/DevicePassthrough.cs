using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Forwards an independent use of device storage without transfer or execution.</summary>
/// <typeparam name="TSchema">The canonical schema.</typeparam>
public sealed class DevicePassthrough<TSchema> : IStage<Device<TSchema>, Device<TSchema>>
    where TSchema : ISchema
{
    /// <summary>Gets the immutable forwarding factory.</summary>
    public IStageFactory<Device<TSchema>, Device<TSchema>> Factory { get; } = new FactoryImpl();

    private sealed class FactoryImpl : IStageFactory<Device<TSchema>, Device<TSchema>>, IDeviceProjectionFactory
    {
        public StageRequirements Describe(StageInputDescriptions inputs) => new(checked(Requirements.ValueLeaseBytes + IntPtr.Size), []) { OutputMaxElements = inputs.Extents[0].ElementCount };

        public IStageProcessor<Device<TSchema>, Device<TSchema>> Create(StageBuildContext context)
        {
            if (context.Capabilities is not IGraphicsStageCapabilities)
            {
                throw new NotSupportedException("Device passthrough requires an explicit graphics host.");
            }

            return new ForwardProcessor();
        }
    }

    private sealed class ForwardProcessor : IStageProcessor<Device<TSchema>, Device<TSchema>>
    {
        public ValueTask<ValueLease<Device<TSchema>>> PrepareAsync(StagePreparationContext context, ValueLease<Device<TSchema>> a, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var graphics = (IGraphicsStageCapabilities)context.Capabilities;
            return ValueTask.FromResult(graphics.Preparation.ForwardDevice(a));
        }

        public void Dispose()
        {
        }
    }
}
