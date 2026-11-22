using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Explicitly prepares an upload copy without submitting it.</summary>
/// <typeparam name="TSchema">The canonical schema.</typeparam>
public sealed class Upload<TSchema> : IStage<Host<TSchema>, Device<TSchema>>
    where TSchema : ISchema
{
    /// <summary>Gets the immutable transfer factory.</summary>
    public IStageFactory<Host<TSchema>, Device<TSchema>> Factory { get; } = new FactoryImpl();

    private sealed class FactoryImpl : IStageFactory<Host<TSchema>, Device<TSchema>>, ITransferFactory
    {
        public StageRequirements Describe(StageInputDescriptions inputs)
        {
            SchemaDescriptor schema = TSchema.Descriptor;
            if ((schema.Metadata.TryGetValue("dependencyOnly", out object? dependency) && Equals(dependency, true)) ||
                (schema.Metadata.TryGetValue("resourceRole", out object? role) && role is "target" or "resource-views"))
            {
                throw new NotSupportedException("Native view/dependency schemas are projections, not canonical buffer transfers.");
            }

            return new(checked(4096 + (inputs.Extents[0].ElementCount * schema.ElementStride)), [new GraphicsStageRequirements(GraphicsExecutionDomain.Upload, 0, [], [])]) { OutputMaxElements = inputs.Extents[0].ElementCount };
        }

        public IStageProcessor<Host<TSchema>, Device<TSchema>> Create(StageBuildContext context)
        {
            if (context.Capabilities is not IGraphicsStageCapabilities)
            {
                throw new NotSupportedException("Upload requires the explicit graphics host.");
            }

            return new UploadProcessor();
        }
    }

    private sealed class UploadProcessor : IStageProcessor<Host<TSchema>, Device<TSchema>>
    {
        public async ValueTask<ValueLease<Device<TSchema>>> PrepareAsync(StagePreparationContext context, ValueLease<Host<TSchema>> a, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SchemaData<TSchema> data = context.ReadHost(a);
            var graphics = (IGraphicsStageCapabilities)context.Capabilities;
            var visitor = new UploadVisitor(graphics.Preparation, data.Extent, cancellationToken);
            DeviceValue output = await data.Storage.Accept(visitor);
            return context.PublishPlatform(new Device<TSchema>(output));
        }

        public void Dispose()
        {
        }
    }

    private sealed class UploadVisitor(GraphicsStagePreparationContext preparation, SchemaExtent extent, CancellationToken token) : ICanonicalDataVisitor<ValueTask<DeviceValue>>
    {
        public ValueTask<DeviceValue> Visit<TElement>(ReadOnlySpan<TElement> elements)
            where TElement : unmanaged
        {
            long bytes = checked(extent.ElementCount * TSchema.Descriptor.ElementStride);
            preparation.Run.ReserveTransfer(bytes);
            preparation.Run.ReserveScratch(checked(bytes * 2));
            if (elements.Length == 0)
            {
                throw new ArgumentException("An empty schema requires an explicit empty-value representation.");
            }

            return UploadAsync(elements.ToArray());
        }

        private async ValueTask<DeviceValue> UploadAsync<TElement>(TElement[] elements)
            where TElement : unmanaged
        {
            GraphicsRun run = preparation.Run;
            IGraphicsBuffer<TElement> staging = run.Device.CreateBuffer<TElement>(new BufferDesc<TElement>
            {
                Count = checked((ulong)elements.Length),
                Usage = BufferUsage.CopySource,
                Memory = MemoryPreference.Upload,
            });
            run.Own(new MappedBufferOwner<TElement>(staging));
            IGraphicsBuffer<TElement> destination = run.Device.CreateBuffer<TElement>(new BufferDesc<TElement>
            {
                Count = checked((ulong)elements.Length),
                Usage = BufferUsage.CopyDestination | BufferUsage.CopySource | BufferUsage.ShaderRead | BufferUsage.ShaderWrite,
            });
            run.Own(destination);
            await staging.MapAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                staging.CopyFrom(elements);
            }
            catch (Exception primary)
            {
                try
                {
                    staging.Unmap();
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(primary, cleanup);
                }

                throw;
            }

            staging.Unmap();
            var output = new DeviceValue(run, TSchema.Descriptor, extent);
            output.SetBuffer(destination);
            output.Encode();
            run.Encode(preparation.Node, context =>
            {
                context.Commands.CopyBuffer(staging.SliceElements(0, staging.Count), destination.SliceElements(0, destination.Count));
                context.Commands.Barrier(new MemoryBarrierDesc
                {
                    Before = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite),
                    After = new BarrierScope(PipelineStage.AllCommands, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite | ResourceAccess.CopyRead),
                });
            });
            return output;
        }
    }
}
