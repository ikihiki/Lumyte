using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class EnforcingNativePreparationCommand(EnforcingNativePreparationBackend backend) : IGraphicsCommandBuffer
{
    private readonly List<Entry> _entries = [];
    private bool _recording;

    public object? Source { get; private set; }

    public object? Destination { get; private set; }

    public ulong SourceOffset { get; private set; }

    public ulong DestinationOffset { get; private set; }

    public ulong CopySize { get; private set; }

    public CommandBufferState State { get; private set; } = CommandBufferState.Recording;

    public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        Check();
        _recording = true;
        Source = source.Buffer;
        Destination = destination.Buffer;
        SourceOffset = source.OffsetInBytes;
        DestinationOffset = destination.OffsetInBytes;
        CopySize = source.SizeInBytes;
    }

    public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
        where T : struct, IShaderArguments => throw new NotSupportedException();

    public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination) => throw new NotSupportedException();

    public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination) => throw new NotSupportedException();

    public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => throw new NotSupportedException();

    public void Barrier(MemoryBarrierDesc barrier) => throw new NotSupportedException();

    public void Barrier<T>(BufferBarrierDesc<T> barrier)
        where T : unmanaged => throw new NotSupportedException();

    public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
        where T : struct, IShaderArguments => throw new NotSupportedException();

    public void Barrier(TextureBarrierDesc barrier)
    {
        Check();
        _recording = true;
    }

    public IRenderEncoder BeginRenderPass(RenderPassDesc desc) => throw new NotSupportedException();

    public IComputeEncoder BeginComputePass(ComputePassDesc desc)
    {
        Check();
        _recording = true;
        return new EnforcingNativePreparationEncoder(this, backend);
    }

    public void PrepareCompute<T>(IGraphicsComputePipeline pipeline, IArgumentTable? table, in T arguments)
        where T : struct, IShaderArguments
    {
        Check();
        if (_recording)
        {
            throw new InvalidOperationException("Preparation must precede recording.");
        }

        object identity = arguments.NativePreparationIdentity ?? throw new ArgumentException("Missing identity.");
        byte[] bytes = new byte[T.TransferSize];
        backend.Allocations++;
        backend.Maps++;
        var writer = new EnforcingNativePreparationWriter(bytes);
        arguments.Write(ref writer);
        writer.Complete();
        backend.Writes++;
        _entries.Add(new(identity, pipeline, table, bytes, bytes.Length));
    }

    public void Finish() => State = CommandBufferState.Executable;

    public void Dispose() => State = CommandBufferState.Disposed;

    internal Entry Find(object identity, object? pipeline, object? table)
    {
        Check();
        return _entries.FirstOrDefault(e => ReferenceEquals(e.Identity, identity) && ReferenceEquals(e.Pipeline, pipeline) && ReferenceEquals(e.Table, table))
            ?? throw new InvalidOperationException("Missing command-local preparation.");
    }

    internal void Check()
    {
        if (State != CommandBufferState.Recording)
        {
            throw new InvalidOperationException("Command expired.");
        }
    }

    internal sealed record Entry(object Identity, object Pipeline, object? Table, byte[] Wire, int WireSize);
}
