using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

internal sealed class DeviceStorage<TElement>(BufferSlice<TElement> slice) : IDeviceStorage
    where TElement : unmanaged
{
    private readonly IGraphicsBuffer<TElement> _buffer = slice.Buffer;
    private readonly BufferSlice<TElement> _slice = slice;

    public void Validate(IGraphicDevice device, ResourceAccess access)
    {
        if (_buffer is not IGraphicsNativeOrigin origin || !ReferenceEquals(origin.Device, device))
        {
            throw new ArgumentException("The buffer belongs to another actual native device.");
        }

        origin.ValidateOrigin();
        var required = (BufferUsage)0;
        if ((access & ResourceAccess.ShaderRead) != 0)
        {
            required |= BufferUsage.ShaderRead;
        }

        if ((access & ResourceAccess.ShaderWrite) != 0)
        {
            required |= BufferUsage.ShaderWrite;
        }

        if ((access & ResourceAccess.CopyRead) != 0)
        {
            required |= BufferUsage.CopySource;
        }

        if ((access & ResourceAccess.CopyWrite) != 0)
        {
            required |= BufferUsage.CopyDestination;
        }

        if ((_buffer.Usage & required) != required || _buffer.IsMapped)
        {
            throw new ArgumentException("The _buffer usage or mapping state conflicts with its declared device access.");
        }
    }

    public async ValueTask<ValueLease<Host<TSchema>>> DownloadAsync<TSchema>(GraphicsStagePreparationContext context, SchemaExtent extent, CancellationToken cancellationToken)
        where TSchema : ISchema
    {
        GraphicsRun run = context.Run;
        long bytes = checked(extent.ElementCount * TSchema.Descriptor.ElementStride);
        run.ReserveReadback(bytes);
        run.ReserveScratch(bytes);
        IGraphicsBuffer<TElement> readback = run.Device.CreateBuffer<TElement>(new BufferDesc<TElement>
        {
            Count = _slice.Count,
            Usage = BufferUsage.CopyDestination,
            Memory = MemoryPreference.Readback,
        });
        run.Own(new MappedBufferOwner<TElement>(readback));
        var completion = new TaskCompletionSource<SchemaData<TSchema>>(TaskCreationOptions.RunContinuationsAsynchronously);
        SchemaData<TSchema>? staged = null;
        run.Encode(context.Node, record =>
        {
            record.Commands.Barrier(new MemoryBarrierDesc
            {
                Before = new BarrierScope(PipelineStage.AllCommands, ResourceAccess.ShaderWrite | ResourceAccess.CopyWrite),
                After = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead),
            });
            record.Commands.CopyBuffer(_slice, readback.SliceElements(0, readback.Count));
            record.Commands.Barrier(new BufferBarrierDesc<TElement>
            {
                Buffer = readback.SliceElements(0, readback.Count),
                Before = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite),
                After = new BarrierScope(PipelineStage.Host, ResourceAccess.HostRead),
            });
        });
        run.RegisterReadback(
            context.Node,
            async () =>
            {
                await readback.MapAsync();
                byte[] copy;
                try
                {
                    var values = new TElement[checked((int)extent.ElementCount)];
                    readback.CopyTo(values);
                    copy = MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
                }
                catch (Exception primary)
                {
                    try
                    {
                        readback.Unmap();
                    }
                    catch (Exception cleanup)
                    {
                        throw new AggregateException(primary, cleanup);
                    }

                    throw;
                }

                readback.Unmap();
                staged = new SchemaData<TSchema>(copy, extent, context.Scope, new CanonicalStorage<TElement>(copy));
            },
            error => completion.TrySetException(error),
            () => completion.TrySetResult(staged ?? throw new InvalidOperationException("Readback publication requires a successful complete copy.")));
        using CancellationTokenRegistration cancellation = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        SchemaData<TSchema> data = await completion.Task;
        return context.Scope.PublishPlatform(new Host<TSchema>(data));
    }
}
