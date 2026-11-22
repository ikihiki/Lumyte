using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Explicitly pauses for host-owned submission, real completion and canonical readback.</summary>
/// <typeparam name="TSchema">The canonical schema.</typeparam>
public sealed class Download<TSchema> : IStage<Device<TSchema>, Host<TSchema>>
    where TSchema : ISchema
{
    /// <summary>Gets the immutable scheduler-owned transfer factory.</summary>
    public IStageFactory<Device<TSchema>, Host<TSchema>> Factory { get; } = new FactoryImpl();

    private sealed class FactoryImpl : IStageFactory<Device<TSchema>, Host<TSchema>>, ITransferFactory
    {
        public StageRequirements Describe(StageInputDescriptions inputs)
        {
            SchemaDescriptor schema = TSchema.Descriptor;
            if ((schema.Metadata.TryGetValue("dependencyOnly", out object? dependency) && Equals(dependency, true)) ||
                (schema.Metadata.TryGetValue("resourceRole", out object? role) && role is "target" or "resource-views"))
            {
                throw new NotSupportedException("Native view/dependency schemas are projections, not canonical buffer transfers.");
            }

            return new(checked(1024 + (2 * inputs.Extents[0].ElementCount * TSchema.Descriptor.ElementStride)), [new GraphicsStageRequirements(GraphicsExecutionDomain.Download, 0, [], [new StageInputAccess(0, ResourceAccess.CopyRead)])]) { OutputMaxElements = inputs.Extents[0].ElementCount };
        }

        public IStageProcessor<Device<TSchema>, Host<TSchema>> Create(StageBuildContext context)
        {
            if (context.Capabilities is not IGraphicsStageCapabilities)
            {
                throw new NotSupportedException("Download requires an explicit graphics execution host.");
            }

            return new DownloadProcessor();
        }
    }

    private sealed class DownloadProcessor : IStageProcessor<Device<TSchema>, Host<TSchema>>
    {
        public ValueTask<ValueLease<Host<TSchema>>> PrepareAsync(StagePreparationContext context, ValueLease<Device<TSchema>> a, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var graphics = (IGraphicsStageCapabilities)context.Capabilities;
            DeviceValue value = graphics.Preparation.GetValue(a);
            return value.Storage.DownloadAsync<TSchema>(graphics.Preparation, value.Extent, cancellationToken);
        }

        public void Dispose()
        {
        }
    }
}
