using System.Security.Cryptography;
using Lumyte.Maps.Mvt;
using Lumyte.Memory;
using Lumyte.Resources;
using Xunit;

namespace Lumyte.Maps.Tests;

/// <summary>Exercises bounded portable input, private snapshots and the actual manager pipeline.</summary>
public sealed class InMemoryMvtTests
{
    /// <summary>Caller mutation cannot change the bytes or observed content revision.</summary>
    /// <returns>Completion of the snapshot and pipeline checks.</returns>
    [Fact]
    public async Task SnapshotAndDecoderShareImmutableRevisionAsync()
    {
        byte[] bytes = MvtDecoderTests.Tile("roads", 4096, [9, 0, 0, 10, 2, 2]);
        string revision = Convert.ToHexString(SHA256.HashData(bytes));
        var id = new MapTileId(0, 0, 0);
        var inputs = new Dictionary<MapTileId, ReadOnlyMemory<byte>> { [id] = bytes };
        var registration = new InMemoryMvtRegistration("fixture", "1", new("memory"), inputs);
        var source = new InMemoryMvtSource(registration);
        inputs.Clear();
        bytes.AsSpan().Clear();
        await using ResourceSourceSnapshot snapshot = await source.OpenSnapshotAsync(registration.GetKey(id).Address.Id);
        Assert.Equal(revision, snapshot.Revision);
        Assert.Equal(revision, await source.GetRevisionAsync(snapshot.Id));
        await using Stream stream = await snapshot.OpenReadAsync();
        Assert.False(stream.CanWrite);
        Assert.False(((MemoryStream)stream).TryGetBuffer(out _));
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(revision, Convert.ToHexString(SHA256.HashData(copy.ToArray())));
        await Assert.ThrowsAsync<NotSupportedException>(async () => await snapshot.OpenReadAsync(1));
        await using MemoryManager memory = new();
        await using ResourceManager manager = new("memory-test", source, [new MvtResourceStage(registration)], memory, new InlineRuntime());
        using ResourceReference<MapTileContent> reference = await manager.RetainAsync(registration.GetKey(id));
        using ResourceLease<MapTileContent> lease = reference.Pin();
        Assert.Equal(revision, lease.Value.SourceRevision);
        Assert.Equal("roads", Assert.Single(lease.Value.Layers).Name);
        var other = new InMemoryMvtRegistration("fixture", "1", new("memory"), new Dictionary<MapTileId, ReadOnlyMemory<byte>> { [id] = copy.ToArray() });
        await using ResourceManager mismatch = new("memory-mismatch", source, [new MvtResourceStage(other)], memory, new InlineRuntime());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await mismatch.RetainAsync(other.GetKey(id)));
    }

    /// <summary>Input and reservation reject registration before copying, including allocation overhead.</summary>
    [Fact]
    public void RejectsInputReservationAndDuplicateNormalizedIds()
    {
        var id = new MapTileId(0, 0, 0);
        var tiles = new Dictionary<MapTileId, ReadOnlyMemory<byte>> { [id] = new byte[16] };
        Assert.Throws<InvalidDataException>(() => new InMemoryMvtRegistration("f", "1", new("tiny", maxInputBytes: 15), tiles));
        Assert.Throws<InvalidDataException>(() => new InMemoryMvtRegistration("f", "1", new("tiny", maxReservationBytesPerTile: 551), tiles));
        _ = new InMemoryMvtRegistration("f", "1", new("exact", maxReservationBytesPerTile: 552), tiles);
        tiles.Add(new(0, -1, 0), new byte[16]);
        Assert.Throws<ArgumentException>(() => new InMemoryMvtRegistration("f", "1", new("tiny"), tiles));
    }

    /// <summary>Even an earlier valid tile is not copied before all input sizes pass admission.</summary>
    [Fact]
    public void PreflightRejectsAllInputsBeforeReadingAnyBuffer()
    {
        using var unreadable = new UnreadableMemory();
        var tiles = new Dictionary<MapTileId, ReadOnlyMemory<byte>>
        {
            [new(1, 0, 0)] = unreadable.Memory,
            [new(1, 1, 0)] = new byte[17],
        };
        Assert.Throws<InvalidDataException>(() => new InMemoryMvtRegistration("f", "1", new("tiny", maxInputBytes: 16), tiles));
    }

    /// <summary>Missing tiles and cancelled observations cannot silently produce content.</summary>
    /// <returns>Completion of failure checks.</returns>
    [Fact]
    public async Task MissingAndCancelledReadsAreExplicitAsync()
    {
        var registration = new InMemoryMvtRegistration("f", "1", new("test"), new Dictionary<MapTileId, ReadOnlyMemory<byte>>());
        var source = new InMemoryMvtSource(registration);
        ResourceId id = registration.GetKey(new(0, 0, 0)).Address.Id;
        Assert.Null(await source.GetRevisionAsync(id));
        await Assert.ThrowsAsync<FileNotFoundException>(async () => await source.OpenSnapshotAsync(id));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await source.OpenSnapshotAsync(id, cancelled.Token));
    }

    private sealed class UnreadableMemory : System.Buffers.MemoryManager<byte>
    {
        public override Memory<byte> Memory => CreateMemory(16);

        public override Span<byte> GetSpan() => throw new InvalidOperationException("Payload must not be read before admission.");

        public override System.Buffers.MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }

    private sealed class InlineRuntime : IResourceRuntime, IResourceExecutor
    {
        public IResourceExecutor Executor => this;

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => context.ExecuteAsync(cancellationToken);

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource) => stage.ReleaseAsync(resource, runtime);
    }
}
