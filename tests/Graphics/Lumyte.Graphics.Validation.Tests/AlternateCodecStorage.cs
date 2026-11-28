using System.Diagnostics.CodeAnalysis;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class AlternateCodecStorage<T>(AlternateCodecBackend backend, ShaderArtifact artifact, ulong count, MemoryPreference memory) : IGraphicsShaderDataBuffer<T>, IShaderDataSource
    where T : struct, IShaderArguments
{
    public ulong Count => count;

    public ulong SizeInBytes => count * ShaderElementStrideInBytes;

    public ulong ShaderElementStrideInBytes => (ulong)T.TransferSize;

    public MemoryPreference Memory => memory;

    public bool IsMapped { get; private set; }

    public IShaderDataLayout Layout { get; } = ShaderDataLayout.Data<T>(artifact, ShaderTarget.Wgsl);

    public object ShaderHandle => this;

    public void ValidateAlive()
    {
    }

    public bool TryGetDependencies([NotNullWhen(true)] out IShaderReferenceMetadata? dependencies)
    {
        dependencies = new ShaderDependencyMetadata([]);
        return true;
    }

    public void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0)
    {
        backend.Writes++;
        var writer = new TypedNativeWriter(backend.NativeReferences);
        foreach (T value in source)
        {
            value.Write(ref writer);
        }

        if (backend.FailWrite)
        {
            throw new InvalidOperationException("Injected native write failure.");
        }
    }

    public void CopyFrom<TSource>(ReadOnlySpan<TSource> source, ulong elementOffset = 0)
        where TSource : struct, IShaderArguments
    {
        backend.Writes++;
        var writer = new TypedNativeWriter(backend.NativeReferences);
        foreach (TSource value in source)
        {
            value.Write(ref writer);
        }

        if (backend.FailWrite)
        {
            throw new InvalidOperationException("Injected native write failure.");
        }
    }

    public ValueTask MapAsync(CancellationToken cancellationToken = default)
    {
        IsMapped = true;
        return ValueTask.CompletedTask;
    }

    public void Unmap() => IsMapped = false;

    public ShaderDataSlice<T> SliceElements(ulong offset, ulong count) => new(this, offset, count);

    public void Dispose()
    {
    }
}
