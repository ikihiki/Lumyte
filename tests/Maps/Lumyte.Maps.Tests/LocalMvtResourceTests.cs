using System.Security.Cryptography;
using Lumyte.Maps.Mvt;
using Lumyte.Memory;
using Lumyte.Resources;
using Xunit;

namespace Lumyte.Maps.Tests;

/// <summary>Exercises the real manager, source snapshot and reusable stage.</summary>
public sealed class LocalMvtResourceTests
{
    /// <summary>Cache hits retain the complete immutable byte charge and observed revision.</summary>
    /// <returns>Completion of the integration fixture.</returns>
    [Fact]
    public async Task SharesCpuContentAndPropagatesRevisionAsync()
    {
        string file = Path.GetTempFileName();
        try
        {
            byte[] bytes = MvtDecoderTests.Tile("roads", 4096, [9, 0, 0, 10, 2, 2]);
            await File.WriteAllBytesAsync(file, bytes);
            var id = new MapTileId(0, 0, 0);
            var registration = new LocalMvtRegistration("fixture", "1", new("bounded"), new Dictionary<MapTileId, string> { [id] = file });
            var source = new LocalMvtSource(registration);
            await using MemoryManager memory = new();
            await using ResourceManager manager = new("maps-test", source, [new MvtResourceStage(registration)], memory, new InlineRuntime());
            using ResourceReference<MapTileContent> first = await manager.RetainAsync(registration.GetKey(id));
            using ResourceLease<MapTileContent> firstLease = first.Pin();
            using ResourceReference<MapTileContent> second = await manager.RetainAsync(registration.GetKey(id));
            using ResourceLease<MapTileContent> secondLease = second.Pin();
            Assert.Same(firstLease.Value, secondLease.Value);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), firstLease.Value.SourceRevision);
            Assert.True(firstLease.Value.RetainedBytes > 0);
            Assert.Equal(firstLease.Value.RetainedBytes, secondLease.Value.RetainedBytes);
            var other = new LocalMvtRegistration("fixture", "1", new("bounded", maxVertices: 200), new Dictionary<MapTileId, string> { [id] = file });
            var same = new LocalMvtRegistration("fixture", "1", new("bounded"), new Dictionary<MapTileId, string> { [id] = file });
            Assert.Equal(registration.GetKey(id), same.GetKey(id));
            await using ResourceManager mismatched = new("mismatch", source, [new MvtResourceStage(same)], memory, new InlineRuntime());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await mismatched.RetainAsync(same.GetKey(id)));
            Assert.NotEqual(registration.GetKey(id), other.GetKey(id));
            Assert.NotEqual(registration.GetKey(id).Address.Variant, other.GetKey(id).Address.Variant);
            Assert.Equal(registration.GetKey(id), registration.GetKey(new(0, -1, 0)));
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>The source rejects oversized bytes before creating its snapshot array.</summary>
    /// <returns>Completion of the input limit check.</returns>
    [Fact]
    public async Task SourceAppliesRegisteredInputLimitAsync()
    {
        string file = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(file, new byte[16]);
            var id = new MapTileId(0, 0, 0);
            var registration = new LocalMvtRegistration("fixture", "1", new("tiny", maxInputBytes: 1), new Dictionary<MapTileId, string> { [id] = file });
            var source = new LocalMvtSource(registration);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await source.OpenSnapshotAsync(registration.GetKey(id).Address.Id));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private sealed class InlineRuntime : IResourceRuntime, IResourceExecutor
    {
        public IResourceExecutor Executor => this;

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => context.ExecuteAsync(cancellationToken);

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource) => stage.ReleaseAsync(resource, runtime);
    }
}
