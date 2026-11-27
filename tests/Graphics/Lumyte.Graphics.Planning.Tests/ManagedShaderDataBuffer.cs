using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

internal sealed class ManagedShaderDataBuffer<T>(ulong count, MemoryPreference memory, ManagedDevice device) : IGraphicsShaderDataBuffer<T>
    where T : struct, IShaderArguments
{
    private bool _disposed;

    public ulong Count => count;

    public ulong SizeInBytes => checked(count * ShaderElementStrideInBytes);

    public ulong ShaderElementStrideInBytes => checked((ulong)T.TransferSize);

    public MemoryPreference Memory => memory;

    public bool IsMapped { get; private set; }

    internal List<ManagedShaderDataValue> EncodedValues { get; } = [];

    public void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0) => CopyFrom<T>(source, elementOffset);

    public void CopyFrom<TSource>(ReadOnlySpan<TSource> source, ulong elementOffset = 0)
        where TSource : struct, IShaderArguments
    {
        ShaderTransfer.ValidateCompatibility<T, TSource>();
        if (_disposed || !IsMapped || Memory != MemoryPreference.Upload || elementOffset > Count || (ulong)source.Length > Count - elementOffset)
        {
            throw new InvalidOperationException("Typed upload requires mapped, live staging and bounded source elements.");
        }

        if (device.ShaderDataWriteFailures > 0)
        {
            device.ShaderDataWriteFailures--;
            throw new InvalidOperationException("Injected typed staging transfer failure.");
        }

        foreach (TSource value in source)
        {
            var writer = new ManagedDataWriter(TSource.TransferSize);
            value.Write(ref writer);
            EncodedValues.Add(writer.Complete(TSource.ReferenceCount));
        }

        device.Events.Add("write-shaderdata");
    }

    public ValueTask MapAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || Memory != MemoryPreference.Upload || IsMapped)
        {
            throw new InvalidOperationException("Only idle upload staging may be mapped.");
        }

        device.Events.Add("map-shaderdata");
        return new(MapCoreAsync());
    }

    public void Unmap()
    {
        if (_disposed || !IsMapped)
        {
            throw new InvalidOperationException("Unmap requires live mapped upload staging.");
        }

        IsMapped = false;
        device.Events.Add("unmap-shaderdata");
    }

    public ShaderDataSlice<T> SliceElements(ulong offset, ulong count) => new(this, offset, count);

    public void Dispose()
    {
        if (_disposed)
        {
            throw new InvalidOperationException("A native allocation was returned twice.");
        }

        if (IsMapped)
        {
            throw new InvalidOperationException("Mapped staging cannot retire.");
        }

        _disposed = true;
        device.Events.Add("dispose-shaderdata");
    }

    private async Task MapCoreAsync()
    {
        if (device.ShaderDataMapGate is { } gate)
        {
            device.ShaderDataMapStarted.TrySetResult();
            await gate.Task.ConfigureAwait(false);
        }

        IsMapped = true;
        device.Events.Add("mapped-shaderdata");
    }
}
