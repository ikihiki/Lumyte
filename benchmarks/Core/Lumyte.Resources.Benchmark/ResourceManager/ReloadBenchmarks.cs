using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using Lumyte.Memory;

namespace Lumyte.Resources.Benchmark;

/// <summary>Publishes source changes through the public automatic reload path.</summary>
[BenchmarkCategory("Core", "Resources", "Reload")]
public class ReloadBenchmarks
{
    /// <summary>Rebuilds one changed source while retaining exactly one old generation lease.</summary>
    /// <returns>The newly published checksum.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-RES-06")]
    public Task<int> ChangeAndPublishAsync() => ReloadAsync(fail: false);

    /// <summary>Fails a replacement build and verifies the old publication remains available.</summary>
    /// <returns>The preserved old checksum.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-RES-07")]
    public Task<int> FailedReloadPreservesPublishedAsync() => ReloadAsync(fail: true);

    private static async Task<int> ReloadAsync(bool fail)
    {
        await using var memory = new MemoryManager();
        var source = new Source();
        var stage = new ReloadStage();
        await using ResourceManager manager = new("reload", source, [stage], memory, new ResourcesBenchmarks.InlineRuntime(), reloadOptions: new ResourceReloadOptions { DebounceDelay = TimeSpan.Zero, MaximumRetryCount = 0 });
        using ResourceReference<int> reference = await manager.RetainAsync(new ResourceKey<int>("asset"));
        using ResourceLease<int> old = reference.Pin();
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.Reloaded += (_, _) => published.TrySetResult();
        manager.ReloadFailed += (_, _) =>
        {
            if (fail)
            {
                published.TrySetResult();
            }
            else
            {
                published.TrySetException(new InvalidOperationException("Controlled reload failed."));
            }
        };
        stage.Fail = fail;
        source.Version = 2;
        source.ChangesChannel.Writer.TryWrite(new ResourceSourceChange(new ResourceId("asset"), "2"));
        await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using ResourceLease<int> current = reference.Pin();
        if (old.Value != 1)
        {
            throw new InvalidOperationException("Old lease changed after publication.");
        }

        return current.Value;
    }

    private sealed class ReloadStage : IResourceSourceStage<int>
    {
        private readonly IntegrationBenchmarks.ChecksumStage _inner = new();

        internal bool Fail { get; set; }

        public ValueTask<int> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => Fail ? throw new IOException("Controlled replacement failure.") : _inner.BuildAsync(context, cancellationToken);

        public ValueTask ReleaseAsync(int resource) => ValueTask.CompletedTask;
    }

    private sealed class Source : IResourceSource, IResourceChangeSource
    {
        public IResourceChangeSource Changes => this;

        internal Channel<ResourceSourceChange> ChangesChannel { get; } = Channel.CreateUnbounded<ResourceSourceChange>();

        internal int Version { get; set; } = 1;

        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ResourceSourceSnapshot>(new Snapshot(id, Version));

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(Version.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public async IAsyncEnumerable<ResourceSourceChange> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (ResourceSourceChange change in ChangesChannel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return change;
            }
        }
    }

    private sealed class Snapshot(ResourceId id, int version) : ResourceSourceSnapshot
    {
        public override ResourceId Id => id;

        public override string Revision => version.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public override long? Length => 1;

        public override bool CanReadRange => true;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream>(new MemoryStream([(byte)version], writable: false));

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
