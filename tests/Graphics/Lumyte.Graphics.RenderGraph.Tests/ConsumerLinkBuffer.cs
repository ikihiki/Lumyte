using System.Diagnostics.CodeAnalysis;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class ConsumerLinkBuffer(ShaderArtifact artifact, ulong count = 1, MemoryPreference memory = MemoryPreference.Automatic) : IGraphicsShaderDataBuffer<ConsumerLink>, IShaderDataSource
{
    private readonly IShaderReferenceMetadata?[] _values = new IShaderReferenceMetadata?[checked((int)count)];
    private bool _disposed;

    public ulong Count => count;

    public ulong SizeInBytes => count * 16;

    public ulong ShaderElementStrideInBytes => 16;

    public MemoryPreference Memory => memory;

    public bool IsMapped => false;

    public IShaderDataLayout Layout => ShaderDataLayout.Root(artifact.GetTarget(ShaderTarget.SpirV), "arguments");

    public object ShaderHandle => this;

    internal bool ValidationEnabled { get; init; } = true;

    internal bool ThrowOnDependencyQuery { get; init; }

    internal int DependencyQueries { get; private set; }

    internal IShaderReferenceMetadata Dependencies
        => _values.Count(value => value != null) == 1
            ? _values.First(value => value != null)!
            : new ShaderDependencyMetadata(_values.OfType<IShaderReferenceMetadata>().SelectMany(value => value.References));

    public void CopyFrom(ReadOnlySpan<ConsumerLink> source, ulong elementOffset = 0) => throw new NotSupportedException();

    public ValueTask MapAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public void Unmap() => throw new NotSupportedException();

    public ShaderDataSlice<ConsumerLink> SliceElements(ulong offset, ulong count) => new(this, offset, count);

    public bool TryGetDependencies([NotNullWhen(true)] out IShaderReferenceMetadata? dependencies)
    {
        DependencyQueries++;
        if (ThrowOnDependencyQuery)
        {
            throw new InvalidOperationException("Unexpected dependency query.");
        }

        ValidateAlive();
        dependencies = ValidationEnabled ? Dependencies : null;
        return dependencies != null;
    }

    public void ValidateAlive() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose() => _disposed = true;

    internal IShaderReferenceMetadata ElementDependencies(ulong index)
    {
        ValidateAlive();
        return _values[checked((int)index)] ?? new ShaderDependencyMetadata([]);
    }

    internal void SetDependencies(ulong index, IShaderReferenceMetadata value) => _values[checked((int)index)] = value;
}
