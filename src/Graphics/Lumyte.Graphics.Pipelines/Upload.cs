using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Explicitly prepares an upload copy without submitting it.</summary>
/// <typeparam name="TSchema">The canonical schema.</typeparam>
public sealed class Upload<TSchema> : IStage<Host<TSchema>, Device<TSchema>>
    where TSchema : ISchema
{
    /// <summary>Initializes a new instance of the <see cref="Upload{TSchema}"/> class with source retention eligibility.</summary>
    public Upload()
        : this(UploadRetention.Source)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Upload{TSchema}"/> class.</summary>
    /// <param name="retention">Source eligibility or invocation-owned fresh storage.</param>
    public Upload(UploadRetention retention)
    {
        if (!Enum.IsDefined(retention))
        {
            throw new ArgumentOutOfRangeException(nameof(retention));
        }

        Factory = new FactoryImpl(retention);
    }

    /// <summary>Gets the immutable transfer factory.</summary>
    public IStageFactory<Host<TSchema>, Device<TSchema>> Factory { get; }

    private sealed class FactoryImpl(UploadRetention retention) : IStageFactory<Host<TSchema>, Device<TSchema>>, ITransferFactory
    {
        public bool RetainsImmutableSource => retention == UploadRetention.Source;

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

            return new UploadProcessor(RetainsImmutableSource);
        }
    }

    private sealed class UploadProcessor(bool retainsImmutableSource) : IStageProcessor<Host<TSchema>, Device<TSchema>>
    {
        public async ValueTask<ValueLease<Device<TSchema>>> PrepareAsync(StagePreparationContext context, ValueLease<Host<TSchema>> a, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SchemaData<TSchema> data = context.ReadHost(a);
            var graphics = (IGraphicsStageCapabilities)context.Capabilities;
            var visitor = new UploadVisitor(graphics.Preparation, data.Extent, cancellationToken, retainsImmutableSource);
            DeviceValue output = await data.Storage.Accept(visitor);
            return context.PublishPlatform(new Device<TSchema>(output));
        }

        public void Dispose()
        {
        }
    }

    private sealed class UploadVisitor(GraphicsStagePreparationContext preparation, SchemaExtent extent, CancellationToken token, bool retainsImmutableSource) : ICanonicalDataVisitor<ValueTask<DeviceValue>>
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

            return retainsImmutableSource && preparation.Run.ImmutableUploads is { } cache
                ? UploadRetainedAsync(elements.ToArray(), cache)
                : UploadAsync(elements.ToArray());
        }

        private async ValueTask<DeviceValue> UploadRetainedAsync<TElement>(TElement[] elements, ImmutableUploadCache cache)
            where TElement : unmanaged
        {
            GraphicsRun run = preparation.Run;
            if (TSchema.Descriptor.ElementStride > cache.Policy.ChunkBytes)
            {
                throw new NotSupportedException("A canonical element exceeds the explicit immutable chunk ceiling.");
            }

            int chunkElements = cache.Policy.ChunkBytes / TSchema.Descriptor.ElementStride;
            using IDisposable uploadUse = await cache.EnterUploadAsync(token).ConfigureAwait(false);
            cache.Admit<TElement, TSchema>(preparation.Node, elements, chunkElements);
            var chunks = new List<(IGraphicsBuffer<TElement> Buffer, IGraphicsBuffer<TElement>? Staging, ulong Offset)>();
            for (int offset = 0; offset < elements.Length; offset = checked(offset + chunkElements))
            {
                token.ThrowIfCancellationRequested();
                int length = Math.Min(chunkElements, elements.Length - offset);
                ImmutableUploadCache.Lease<TElement>? lease = cache.Find<TElement, TSchema>(preparation.Node, elements.AsSpan(offset, length));
                IGraphicsBuffer<TElement>? staging = null;
                if (lease is null)
                {
                    lease = cache.Create<TElement, TSchema>(preparation.Node, elements.AsSpan(offset, length), run.Device);
                    run.Own(lease);
                    staging = run.Device.CreateBuffer<TElement>(new() { Count = (ulong)length, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
                    run.Own(new MappedBufferOwner<TElement>(staging));
                    await staging.MapAsync(token);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        staging.CopyFrom(elements.AsSpan(offset, length));
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
                    run.OnImmutableCompletion(lease.Publish);
                }
                else
                {
                    run.Own(lease);
                }

                chunks.Add((lease.Buffer, staging, (ulong)offset));
            }

            IGraphicsBuffer<TElement> destination;
            if (chunks.Count == 1)
            {
                destination = chunks[0].Buffer;
            }
            else
            {
                // Contiguous ABI assembly is fresh per invocation. Immutable chunks,
                // not the mutable run/target/record plan, are the retained unit.
                destination = run.Device.CreateBuffer<TElement>(new() { Count = (ulong)elements.Length, Usage = BufferUsage.CopyDestination | BufferUsage.CopySource | BufferUsage.ShaderRead });
                run.Own(destination);
            }

            var output = new DeviceValue(run, TSchema.Descriptor, extent);
            output.SetBuffer(destination, immutable: true);
            output.Encode();
            run.Encode(preparation.Node, record =>
            {
                foreach ((IGraphicsBuffer<TElement> buffer, IGraphicsBuffer<TElement>? staging, ulong offset) in chunks)
                {
                    if (staging is not null)
                    {
                        record.Commands.Barrier(new BufferBarrierDesc<TElement> { Buffer = staging.SliceElements(0, staging.Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
                        record.Commands.CopyBuffer(staging.SliceElements(0, staging.Count), buffer.SliceElements(0, buffer.Count));
                    }
                }

                record.Commands.Barrier(new MemoryBarrierDesc
                {
                    Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite),
                    After = new(PipelineStage.Copy, ResourceAccess.CopyRead),
                });
                if (chunks.Count > 1)
                {
                    foreach ((IGraphicsBuffer<TElement> buffer, _, ulong offset) in chunks)
                    {
                        record.Commands.CopyBuffer(buffer.SliceElements(0, buffer.Count), destination.SliceElements(offset, buffer.Count));
                    }
                }

                record.Commands.Barrier(new MemoryBarrierDesc
                {
                    Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite),
                    After = new(PipelineStage.AllCommands, ResourceAccess.ShaderRead | ResourceAccess.CopyRead),
                });
            });
            return output;
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
                context.Commands.Barrier(new BufferBarrierDesc<TElement> { Buffer = staging.SliceElements(0, staging.Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
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
