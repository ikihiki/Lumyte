using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Lumyte.Memory;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Exercises automatic reload batching and worker lifetime through public resource contracts.</summary>
public sealed class ResourceReloadServiceTests
{
    private static readonly ResourceId _asset = new("asset");
    private static readonly ResourceKey<Value> _key = new(_asset);
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    /// <summary>A burst of repeated hints produces one reload while its original lease remains valid.</summary>
    /// <returns>Completion of the coalesced reload.</returns>
    [Fact]
    public async Task DuplicateHintsAreDebouncedAsync()
    {
        await using Fixture fixture = new();
        using ResourceLease<Value> original = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        await using ResourceReloadService service = new(fixture.Manager, fixture.Changes, new()
        {
            DebounceDelay = TimeSpan.FromMilliseconds(200),
        });
        int results = 0;
        service.Reloaded += (_, _) => Interlocked.Increment(ref results);
        service.Start();
        service.Start();
        await fixture.Changes.Started.Task.WaitAsync(_timeout);
        fixture.Source.Revision = "second";
        for (int i = 0; i < 20; i++)
        {
            await fixture.Changes.SendAsync(new(_asset, null));
        }

        fixture.Changes.Complete();
        await service.Completion.WaitAsync(_timeout);
        Assert.Equal(1, results);
        Assert.Equal(2, fixture.Pipeline.Builds);
        Assert.Equal("first", original.Value.Text);
        using ResourceLease<Value> current = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        Assert.Equal("second", current.Value.Text);
    }

    /// <summary>Continuous notifications cannot postpone a reload beyond the maximum aggregation period.</summary>
    /// <returns>Completion of a reload while the source is still sending hints.</returns>
    [Fact]
    public async Task ContinuousHintsReachMaximumBatchDelayAsync()
    {
        await using Fixture fixture = new();
        using ResourceLease<Value> original = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        await using ResourceReloadService service = new(fixture.Manager, fixture.Changes, new()
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaximumBatchDelay = TimeSpan.FromMilliseconds(150),
        });
        TaskCompletionSource published = NewSignal();
        service.Reloaded += (_, args) =>
        {
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
        service.Start();
        fixture.Source.Revision = "second";
        using CancellationTokenSource cancellation = new(_timeout);
        while (!published.Task.IsCompleted)
        {
            await fixture.Changes.SendAsync(new(_asset, null));
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token);
        }

        Assert.False(service.Completion.IsCompleted);
        using ResourceLease<Value> current = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        Assert.Equal("second", current.Value.Text);
    }

    /// <summary>A source producing synchronously forever cannot prevent the reload worker from starting.</summary>
    /// <returns>Completion of publication and cancellation of the synchronous source.</returns>
    [Fact]
    public async Task SynchronousContinuousSourceDoesNotStarveReloadAsync()
    {
        await using Fixture fixture = new();
        using ResourceLease<Value> original = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        await using ResourceReloadService service = new(fixture.Manager, new SynchronousChanges(), new()
        {
            DebounceDelay = TimeSpan.FromMilliseconds(20),
            MaximumBatchDelay = TimeSpan.FromMilliseconds(30),
        });
        TaskCompletionSource published = NewSignal();
        service.Reloaded += (_, args) =>
        {
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
        fixture.Source.Revision = "second";
        service.Start();
        await published.Task.WaitAsync(_timeout);
        await service.StopAsync().WaitAsync(_timeout);
        Assert.True(service.Completion.IsCompletedSuccessfully);
    }

    /// <summary>Initial publications can request rescans while source notifications continuously wake the same consumer.</summary>
    /// <returns>Completion of concurrent publication, a subsequent reload and clean shutdown.</returns>
    [Fact]
    public async Task InitialPublicationAndSourceHintsShareOneNotificationSignalAsync()
    {
        SynchronousChanges changes = new();
        FakeSource source = new() { Changes = changes };
        await using MemoryManager memory = new();
        await using ResourceManager manager = new("concurrent notifications", source, [new Pipeline()], memory, reloadOptions: new()
        {
            DebounceDelay = TimeSpan.FromMilliseconds(20),
            MaximumBatchDelay = TimeSpan.FromMilliseconds(30),
        });
        ResourceReloadService service = manager.HotReload!;
        TaskCompletionSource published = NewSignal();
        service.Reloaded += (_, args) =>
        {
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await changes.Started.Task.WaitAsync(timeout.Token);
        using ResourceReference<Value> original = await manager.AcquireAsync(_key, timeout.Token);
        for (int i = 0; i < 4096; i++)
        {
            using (ResourceReference<Value> reference = await manager.AcquireAsync(new ResourceKey<Value>($"asset-{i}"), timeout.Token))
            {
                using ResourceLease<Value> lease = reference.Acquire();
                Assert.Equal("first", lease.Value.Text);
            }

            if (i % 32 == 31)
            {
                await manager.TrimAsync(timeout.Token);
            }
        }

        source.Revision = "second";
        await published.Task.WaitAsync(timeout.Token);
        using (ResourceLease<Value> current = original.Acquire())
        {
            Assert.Equal("second", current.Value.Text);
        }

        await service.StopAsync().WaitAsync(timeout.Token);
        Assert.True(service.Completion.IsCompletedSuccessfully);
        Assert.Null(service.LastError);
    }

    /// <summary>A hint arriving during a blocked build survives the current batch and its conflict.</summary>
    /// <returns>Completion of the subsequent graph publication.</returns>
    [Fact]
    public async Task NotificationDuringReloadIsRetainedAsync()
    {
        await using Fixture fixture = new();
        using ResourceLease<Value> original = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        TaskCompletionSource entered = NewSignal();
        TaskCompletionSource resume = NewSignal();
        fixture.Pipeline.AfterSnapshot = async (snapshot, token) =>
        {
            if (snapshot.Revision == "second")
            {
                entered.TrySetResult();
                await resume.Task.WaitAsync(token);
            }
        };
        await using ResourceReloadService service = new(fixture.Manager, fixture.Changes, ImmediateOptions());
        ConcurrentQueue<ResourceReloadStatus> statuses = [];
        TaskCompletionSource published = NewSignal();
        service.Reloaded += (_, args) =>
        {
            statuses.Enqueue(args.Result.Status);
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
        service.Start();
        fixture.Source.Revision = "second";
        await fixture.Changes.SendAsync(new(_asset, null));
        await entered.Task.WaitAsync(_timeout);
        fixture.Source.Revision = "third";
        await fixture.Changes.SendAsync(new(_asset, null));
        resume.SetResult();
        await published.Task.WaitAsync(_timeout);

        Assert.Equal([ResourceReloadStatus.Conflict, ResourceReloadStatus.Published], statuses.ToArray());
        using ResourceLease<Value> current = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        Assert.Equal("third", current.Value.Text);
        Assert.Equal("first", original.Value.Text);
    }

    /// <summary>Transient build failures retry without publishing a partial graph.</summary>
    /// <returns>Completion of the failed and successful attempts.</returns>
    [Fact]
    public async Task FailedReloadRetriesAndKeepsPreviousGraphAsync()
    {
        await using Fixture fixture = new();
        using ResourceReference<Value> reference = await fixture.Manager.AcquireAsync(_key);
        int attempts = 0;
        fixture.Pipeline.AfterSnapshot = (_, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new IOException("The editor is still writing the file.");
            }

            return Task.CompletedTask;
        };
        await using ResourceReloadService service = new(fixture.Manager, fixture.Changes, ImmediateOptions() with { MaximumRetryCount = 1 });
        TaskCompletionSource published = NewSignal();
        int failures = 0;
        service.ReloadFailed += (_, args) =>
        {
            Assert.IsType<IOException>(args.Error);
            Assert.Equal(1, args.Attempt);
            Assert.True(args.WillRetry);
            Assert.True(fixture.Manager.TryAcquireLeaseForTest(reference.Handle, out ResourceLease<Value>? lease));
            using (lease)
            {
                Assert.Equal("first", lease!.Value.Text);
            }

            failures++;
        };
        service.Reloaded += (_, _) => published.TrySetResult();
        service.Start();
        fixture.Source.Revision = "second";
        await fixture.Changes.SendAsync(new(_asset, null));
        await published.Task.WaitAsync(_timeout);

        Assert.Equal(1, failures);
        Assert.Equal(2, attempts);
        Assert.Null(service.LastError);
        Assert.Equal(ResourceReloadStatus.Published, service.LastResult!.Status);
    }

    /// <summary>Exhausting a batch's retries does not stop subsequent change notifications.</summary>
    /// <returns>Completion of publication triggered by a later hint.</returns>
    [Fact]
    public async Task ExhaustedRetryAllowsFutureNotificationAsync()
    {
        await using Fixture fixture = new();
        using ResourceLease<Value> original = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        bool failing = true;
        fixture.Pipeline.AfterSnapshot = (_, _) => failing
            ? Task.FromException(new IOException("Temporarily unreadable"))
            : Task.CompletedTask;
        await using ResourceReloadService service = new(fixture.Manager, fixture.Changes, ImmediateOptions() with { MaximumRetryCount = 1 });
        TaskCompletionSource exhausted = NewSignal();
        TaskCompletionSource published = NewSignal();
        int failures = 0;
        service.ReloadFailed += (_, args) =>
        {
            failures++;
            if (!args.WillRetry)
            {
                exhausted.TrySetResult();
            }
        };
        service.Reloaded += (_, _) => published.TrySetResult();
        service.Start();
        fixture.Source.Revision = "second";
        await fixture.Changes.SendAsync(new(_asset, null));
        await exhausted.Task.WaitAsync(_timeout);
        Assert.Equal(2, failures);
        Assert.IsType<IOException>(service.LastError);
        Assert.False(service.Completion.IsCompleted);

        failing = false;
        await fixture.Changes.SendAsync(new(_asset, null));
        await published.Task.WaitAsync(_timeout);
        Assert.Equal(ResourceReloadStatus.Published, service.LastResult!.Status);
    }

    /// <summary>A conflicting source revision is retried even without another notification.</summary>
    /// <returns>Completion of the successful retry.</returns>
    [Fact]
    public async Task ConflictRetriesLatestRevisionAsync()
    {
        await using Fixture fixture = new();
        using ResourceLease<Value> original = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        fixture.Pipeline.AfterSnapshot = (snapshot, _) =>
        {
            if (snapshot.Revision == "second")
            {
                fixture.Source.Revision = "third";
            }

            return Task.CompletedTask;
        };
        await using ResourceReloadService service = new(fixture.Manager, fixture.Changes, ImmediateOptions() with { MaximumRetryCount = 1 });
        ConcurrentQueue<ResourceReloadStatus> statuses = [];
        TaskCompletionSource published = NewSignal();
        service.Reloaded += (_, args) =>
        {
            statuses.Enqueue(args.Result.Status);
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
        service.Start();
        fixture.Source.Revision = "second";
        await fixture.Changes.SendAsync(new(_asset, null));
        await published.Task.WaitAsync(_timeout);
        Assert.Equal([ResourceReloadStatus.Conflict, ResourceReloadStatus.Published], statuses.ToArray());
        using ResourceLease<Value> current = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        Assert.Equal("third", current.Value.Text);
    }

    /// <summary>Explicit rescans and bounded-queue overflow both revalidate all tracked input identifiers.</summary>
    /// <param name="overflow">Whether to trigger a rescan by overflowing the identifier limit.</param>
    /// <returns>Completion of the tracked source's publication.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RescanRevalidatesTrackedSourcesAsync(bool overflow)
    {
        await using Fixture fixture = new();
        using ResourceLease<Value> original = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        await using ResourceReloadService service = new(fixture.Manager, fixture.Changes, new()
        {
            MaximumPendingChanges = 1,
        });
        TaskCompletionSource published = NewSignal();
        service.Reloaded += (_, _) => published.TrySetResult();
        service.Start();
        fixture.Source.Revision = "second";
        if (overflow)
        {
            await fixture.Changes.SendAsync(new(new("untracked-a"), null));
            await fixture.Changes.SendAsync(new(new("untracked-b"), null));
        }
        else
        {
            await fixture.Changes.SendAsync(ResourceSourceChange.Rescan);
        }

        await published.Task.WaitAsync(_timeout);
        Assert.Equal(ResourceReloadStatus.Published, service.LastResult!.Status);
        using ResourceLease<Value> current = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        Assert.Equal("second", current.Value.Text);
    }

    /// <summary>Watcher failures remain observable and disposal does not take ownership of the manager.</summary>
    /// <returns>Completion of the faulted service and continued manager use.</returns>
    [Fact]
    public async Task WatcherFailureFaultsCompletionAsync()
    {
        await using Fixture fixture = new();
        ResourceReloadService service = new(fixture.Manager, fixture.Changes);
        service.Start();
        await fixture.Changes.Started.Task.WaitAsync(_timeout);
        fixture.Changes.Complete(new IOException("Watcher failed"));
        await Assert.ThrowsAsync<IOException>(() => service.Completion.WaitAsync(_timeout));
        Assert.IsType<IOException>(service.LastError);
        await Assert.ThrowsAsync<IOException>(() => service.StopAsync());
        await service.DisposeAsync();
        await service.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(service.Start);
        using ResourceLease<Value> lease = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        Assert.Equal("first", lease.Value.Text);
    }

    /// <summary>Observer exceptions stop both workers and remain visible through completion.</summary>
    /// <returns>Completion of the worker failure and joined watcher.</returns>
    [Fact]
    public async Task ObserverFailureStopsWatcherAsync()
    {
        await using Fixture fixture = new();
        using ResourceLease<Value> original = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        ResourceReloadService service = new(fixture.Manager, fixture.Changes, ImmediateOptions());
        service.Reloaded += (_, _) => throw new InvalidOperationException("Observer failed");
        service.Start();
        fixture.Source.Revision = "second";
        await fixture.Changes.SendAsync(new(_asset, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Completion.WaitAsync(_timeout));
        Assert.True(fixture.Changes.Stopped.Task.IsCompleted);
        await service.DisposeAsync();
    }

    /// <summary>Throwing cancellation callbacks cannot bypass joining the watcher during disposal.</summary>
    /// <returns>Completion of disposal and continued manager ownership.</returns>
    [Fact]
    public async Task CancellationCallbackFailureStillJoinsWorkersAsync()
    {
        await using Fixture fixture = new();
        fixture.Changes.ThrowOnCancellation = true;
        ResourceReloadService service = new(fixture.Manager, fixture.Changes);
        service.Start();
        await fixture.Changes.Started.Task.WaitAsync(_timeout);
        Task first = service.DisposeAsync().AsTask();
        Task second = service.DisposeAsync().AsTask();
        await Task.WhenAll(first, second).WaitAsync(_timeout);
        Assert.True(fixture.Changes.Stopped.Task.IsCompleted);
        Assert.IsType<AggregateException>(service.LastError);
        using ResourceLease<Value> lease = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        Assert.Equal("first", lease.Value.Text);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static ResourceReloadOptions ImmediateOptions() => new()
    {
        DebounceDelay = TimeSpan.Zero,
        MaximumRetryCount = 0,
        RetryDelay = TimeSpan.Zero,
    };

    private sealed class Fixture : IAsyncDisposable
    {
        internal Fixture()
        {
            Manager = new("reload", Source, [Pipeline], new MemoryManager());
        }

        internal ResourceManager Manager { get; }

        internal FakeSource Source { get; } = new();

        internal FakeChanges Changes { get; } = new();

        internal Pipeline Pipeline { get; } = new();

        public ValueTask DisposeAsync() => Manager.DisposeAsync();
    }

    private sealed record Value(string Text);

    private sealed class Pipeline : IResourceSourceStage<Value>
    {
        private int _builds;

        internal int Builds => Volatile.Read(ref _builds);

        internal Func<ResourceSourceSnapshot, CancellationToken, Task>? AfterSnapshot { get; set; }

        public async ValueTask<Value> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _builds);
            ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
            if (AfterSnapshot is not null)
            {
                await AfterSnapshot(snapshot, cancellationToken);
            }

            return new Value(snapshot.Revision);
        }

        public ValueTask ReleaseAsync(Value resource) => ValueTask.CompletedTask;
    }

    private sealed class FakeSource : IResourceSource
    {
        private string _revision = "first";

        public IResourceChangeSource? Changes { get; init; }

        internal string Revision
        {
            get => Volatile.Read(ref _revision);
            set => Volatile.Write(ref _revision, value);
        }

        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ResourceSourceSnapshot>(new Snapshot(id, Revision));
        }

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>(Revision);
        }
    }

    private sealed class Snapshot(ResourceId id, string revision) : ResourceSourceSnapshot
    {
        public override ResourceId Id => id;

        public override string Revision => revision;

        public override long? Length => revision.Length;

        public override bool CanReadRange => true;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(revision)));

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeChanges : IResourceChangeSource
    {
        private readonly Channel<ResourceSourceChange> _changes = Channel.CreateUnbounded<ResourceSourceChange>();
        private readonly Channel<bool> _delivered = Channel.CreateUnbounded<bool>();

        internal TaskCompletionSource Started { get; } = NewSignal();

        internal TaskCompletionSource Stopped { get; } = NewSignal();

        internal bool ThrowOnCancellation { get; set; }

        public async IAsyncEnumerable<ResourceSourceChange> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using CancellationTokenRegistration registration = ThrowOnCancellation
                ? cancellationToken.Register(() => throw new InvalidOperationException("Cancellation callback failed"))
                : default;
            Started.TrySetResult();
            try
            {
                await foreach (ResourceSourceChange change in _changes.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return change;
                    _delivered.Writer.TryWrite(true);
                }
            }
            finally
            {
                Stopped.TrySetResult();
            }
        }

        internal async Task SendAsync(ResourceSourceChange change)
        {
            await _changes.Writer.WriteAsync(change);
            await _delivered.Reader.ReadAsync().AsTask().WaitAsync(_timeout);
        }

        internal void Complete(Exception? error = null) => _changes.Writer.Complete(error);
    }

    private sealed class SynchronousChanges : IResourceChangeSource
    {
        internal TaskCompletionSource Started { get; } = NewSignal();

        public async IAsyncEnumerable<ResourceSourceChange> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            Started.TrySetResult();
            while (true)
            {
                yield return new(_asset, null);
            }
        }
    }
}
