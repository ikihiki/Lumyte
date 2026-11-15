using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Drawing;

namespace Lumyte.Resources.Graphics.Benchmark.Pipelines;

/// <summary>Owned byte source containing a documented interleaved triangle and native ushort indices.</summary>
public sealed class MeshSource : IResourceSource
{
    private readonly byte[] _bytes;

    /// <summary>Initializes a new instance of the <see cref="MeshSource"/> class.</summary>
    public MeshSource()
    {
        ModelVertex[] vertices =
        [
            new(new(-1, -1, 0), Vector3.UnitZ, new(1, 0, 0, 1), new(0, 1), Vector4.One),
            new(new(1, -1, 0), Vector3.UnitZ, new(1, 0, 0, 1), new(1, 1), Vector4.One),
            new(new(0, 1, 0), Vector3.UnitZ, new(1, 0, 0, 1), new(0.5f, 0), Vector4.One),
        ];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(vertices.Length);
        writer.Write(MemoryMarshal.AsBytes(vertices.AsSpan()));
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)2);
        _bytes = stream.ToArray();
    }

    /// <summary>Gets the number of actual source opens.</summary>
    public int Opens { get; private set; }

    /// <inheritdoc/>
    public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Opens++;
        return ValueTask.FromResult<ResourceSourceSnapshot>(new Snapshot(id, _bytes));
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>("triangle-v1");

    private sealed class Snapshot(ResourceId id, byte[] bytes) : ResourceSourceSnapshot
    {
        public override ResourceId Id => id;

        public override string Revision => "triangle-v1";

        public override long? Length => bytes.Length;

        public override bool CanReadRange => true;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream>(new MemoryStream(bytes, checked((int)offset), checked((int)(length ?? (bytes.Length - offset))), false));
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
