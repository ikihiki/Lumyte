using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

// Fake command-buffer metadata only; never a production transfer tracker.
internal sealed class TestShaderDataOverlay
{
    private readonly Dictionary<IShaderDataSource, IShaderReferenceMetadata> _buffers = new(ReferenceEqualityComparer.Instance);

    internal IShaderReferenceMetadata? ReadDependencies(IShaderDataSource source)
    {
        source.ValidateAlive();
        return _buffers.TryGetValue(source, out IShaderReferenceMetadata? metadata)
            ? metadata
            : source.TryGetDependencies(out IShaderReferenceMetadata? dependencies) ? dependencies : null;
    }

    internal void Record(IShaderDataSource source, ulong sourceOffset, IShaderDataSource destination, ulong destinationOffset, ulong count, ulong stride, Action record)
    {
        IShaderReferenceMetadata? copied = ReadDependencies(source);
        IShaderReferenceMetadata? previous = ReadDependencies(destination);
        record();
        if (copied == null || previous == null)
        {
            _buffers.Remove(destination);
            return;
        }

        // Whole-buffer union deliberately retains dependencies outside copied ranges.
        _buffers[destination] = destinationOffset == 0 && count * stride == destination.SizeInBytes
            ? copied
            : new ShaderDependencyMetadata(previous.References.Concat(copied.References));
    }
}
