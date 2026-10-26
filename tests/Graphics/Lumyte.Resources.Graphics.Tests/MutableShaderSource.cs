using System.Text;
using System.Threading.Channels;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics.Tests;

internal sealed class MutableShaderSource : IResourceSource, IResourceChangeSource
{
    private readonly Channel<ResourceSourceChange> _changes = Channel.CreateUnbounded<ResourceSourceChange>();
    private int _version = 1;
    private int _reads;

    public IResourceChangeSource Changes => this;

    internal int Reads => Volatile.Read(ref _reads);

    public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _reads);
        int revision = Volatile.Read(ref _version);
        var artifact = ShaderArtifact.PackTarget(ShaderTarget.Wgsl, id.Value == "vertex" ? ShaderStage.Vertex : ShaderStage.Fragment, "main", "test", Encoding.UTF8.GetBytes($"revision-{revision}"), "{}");
        return new(new Snapshot(id, revision.ToString(System.Globalization.CultureInfo.InvariantCulture), artifact.GetBinary()));
    }

    public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
        => new(Volatile.Read(ref _version).ToString(System.Globalization.CultureInfo.InvariantCulture));

    public IAsyncEnumerable<ResourceSourceChange> WatchAsync(CancellationToken cancellationToken = default) => _changes.Reader.ReadAllAsync(cancellationToken);

    internal void Change()
    {
        Interlocked.Increment(ref _version);
        _changes.Writer.TryWrite(ResourceSourceChange.Rescan);
    }

    private sealed class Snapshot(ResourceId id, string revision, byte[] bytes) : ResourceSourceSnapshot
    {
        public override ResourceId Id => id;

        public override string Revision => revision;

        public override long? Length => bytes.Length;

        public override bool CanReadRange => false;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default) => new(new MemoryStream(bytes, writable: false));

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
