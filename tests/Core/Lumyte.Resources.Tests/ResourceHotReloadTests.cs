using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace Lumyte.Resources.Tests;

/// <summary>Exercises automatic reload batching and worker lifetime through public resource contracts.</summary>
public sealed class ResourceHotReloadTests
{
    private static readonly ResourceId _asset = new("asset");
    private static readonly ResourceKey<Value> _key = new(_asset);
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    /// <summary>A burst of repeated hints produces one reload while its original lease remains valid.</summary>
    /// <returns>Completion of the coalesced reload.</returns>
    [Fact]
    public async Task DuplicateHintsAreDebouncedAsync()
    {
        await using Fixture fixture = new(new()
        {
            DebounceDelay = TimeSpan.FromMilliseconds(200),
        });
        using ResourceLease<Value> original = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        await fixture.InitialCheck.Task.WaitAsync(_timeout);
        int results = 0;
        fixture.Manager.Reloaded += (_, _) => Interlocked.Increment(ref results);
        await fixture.Changes.Started.Task.WaitAsync(_timeout);
        fixture.Source.Revision = "second";
        for (int i = 0; i < 20; i++)
        {
            await fixture.Changes.SendAsync(new(_asset, null));
        }

        fixture.Changes.Complete();
        await fixture.Manager.HotReloadCompletion.WaitAsync(_timeout);
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
        await using Fixture fixture = new(new()
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaximumBatchDelay = TimeSpan.FromMilliseconds(150),
        });
        using ResourceLease<Value> original = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        await fixture.InitialCheck.Task.WaitAsync(_timeout);
        TaskCompletionSource published = NewSignal();
        fixture.Manager.Reloaded += (_, args) =>
        {
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
        fixture.Source.Revision = "second";
        using CancellationTokenSource cancellation = new(_timeout);
        while (!published.Task.IsCompleted)
        {
            await fixture.Changes.SendAsync(new(_asset, null));
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token);
        }

        Assert.False(fixture.Manager.HotReloadCompletion.IsCompleted);
        using ResourceLease<Value> current = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        Assert.Equal("second", current.Value.Text);
    }

    /// <summary>A source producing synchronously forever cannot prevent the reload worker from starting.</summary>
    /// <returns>Completion of publication and cancellation of the synchronous source.</returns>
    [Fact]
    public async Task SynchronousContinuousSourceDoesNotStarveReloadAsync()
    {
        await using Fixture fixture = new(
            new()
            {
                DebounceDelay = TimeSpan.FromMilliseconds(20),
                MaximumBatchDelay = TimeSpan.FromMilliseconds(30),
            },
            new SynchronousChanges());
        using ResourceLease<Value> original = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        await fixture.InitialCheck.Task.WaitAsync(_timeout);
        TaskCompletionSource published = NewSignal();
        fixture.Manager.Reloaded += (_, args) =>
        {
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
        fixture.Source.Revision = "second";
        await published.Task.WaitAsync(_timeout);
        original.Dispose();
        await fixture.Manager.DisposeAsync().AsTask().WaitAsync(_timeout);
        Assert.True(fixture.Manager.HotReloadCompletion.IsCompletedSuccessfully);
    }

    /// <summary>Initial publications can request input checks while source notifications continuously wake the same consumer.</summary>
    /// <returns>Completion of concurrent publication, a subsequent reload and clean shutdown.</returns>
    [Fact]
    public async Task InitialPublicationAndSourceHintsShareOneNotificationSignalAsync()
    {
        SynchronousChanges changes = new();
        FakeSource source = new() { Changes = changes };
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        await using ResourceManager manager = new("concurrent notifications", source, [new Pipeline()], memory, execution.Resources, reloadOptions: new()
        {
            DebounceDelay = TimeSpan.FromMilliseconds(20),
            MaximumBatchDelay = TimeSpan.FromMilliseconds(30),
        });
        ResourceManager service = manager;
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

        original.Dispose();
        await manager.DisposeAsync().AsTask().WaitAsync(timeout.Token);
        Assert.True(service.HotReloadCompletion.IsCompletedSuccessfully);
        Assert.Null(service.LastReloadError);
    }

    /// <summary>A hint arriving during a blocked build survives the current batch and its conflict.</summary>
    /// <returns>Completion of the subsequent graph publication.</returns>
    [Fact]
    public async Task NotificationDuringReloadIsRetainedAsync()
    {
        await using Fixture fixture = new(ImmediateOptions());
        using ResourceLease<Value> original = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        await fixture.InitialCheck.Task.WaitAsync(_timeout);
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
        ConcurrentQueue<ResourceReloadStatus> statuses = [];
        TaskCompletionSource published = NewSignal();
        fixture.Manager.Reloaded += (_, args) =>
        {
            statuses.Enqueue(args.Result.Status);
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
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
        await using Fixture fixture = new(ImmediateOptions() with { MaximumRetryCount = 1 });
        using ResourceReference<Value> reference = await fixture.Manager.AcquireAsync(_key);
        await fixture.InitialCheck.Task.WaitAsync(_timeout);
        int attempts = 0;
        fixture.Pipeline.AfterSnapshot = (_, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new IOException("The editor is still writing the file.");
            }

            return Task.CompletedTask;
        };
        TaskCompletionSource published = NewSignal();
        int failures = 0;
        fixture.Manager.ReloadFailed += (_, args) =>
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
        fixture.Manager.Reloaded += (_, _) => published.TrySetResult();
        fixture.Source.Revision = "second";
        await fixture.Changes.SendAsync(new(_asset, null));
        await published.Task.WaitAsync(_timeout);

        Assert.Equal(1, failures);
        Assert.Equal(2, attempts);
        Assert.Null(fixture.Manager.LastReloadError);
        Assert.Equal(ResourceReloadStatus.Published, fixture.Manager.LastReloadResult!.Status);
    }

    /// <summary>Exhausting a batch's retries does not stop subsequent change notifications.</summary>
    /// <returns>Completion of publication triggered by a later hint.</returns>
    [Fact]
    public async Task ExhaustedRetryAllowsFutureNotificationAsync()
    {
        await using Fixture fixture = new(ImmediateOptions() with { MaximumRetryCount = 1 });
        using ResourceLease<Value> original = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        await fixture.InitialCheck.Task.WaitAsync(_timeout);
        bool failing = true;
        fixture.Pipeline.AfterSnapshot = (_, _) => failing
            ? Task.FromException(new IOException("Temporarily unreadable"))
            : Task.CompletedTask;
        TaskCompletionSource exhausted = NewSignal();
        TaskCompletionSource published = NewSignal();
        int failures = 0;
        fixture.Manager.ReloadFailed += (_, args) =>
        {
            failures++;
            if (!args.WillRetry)
            {
                exhausted.TrySetResult();
            }
        };
        fixture.Manager.Reloaded += (_, _) => published.TrySetResult();
        fixture.Source.Revision = "second";
        await fixture.Changes.SendAsync(new(_asset, null));
        await exhausted.Task.WaitAsync(_timeout);
        Assert.Equal(2, failures);
        Assert.IsType<IOException>(fixture.Manager.LastReloadError);
        Assert.False(fixture.Manager.HotReloadCompletion.IsCompleted);

        failing = false;
        await fixture.Changes.SendAsync(new(_asset, null));
        await published.Task.WaitAsync(_timeout);
        Assert.Equal(ResourceReloadStatus.Published, fixture.Manager.LastReloadResult!.Status);
    }

    /// <summary>A conflicting source revision is retried even without another notification.</summary>
    /// <returns>Completion of the successful retry.</returns>
    [Fact]
    public async Task ConflictRetriesLatestRevisionAsync()
    {
        await using Fixture fixture = new(ImmediateOptions() with { MaximumRetryCount = 1 });
        using ResourceLease<Value> original = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        await fixture.InitialCheck.Task.WaitAsync(_timeout);
        fixture.Pipeline.AfterSnapshot = (snapshot, _) =>
        {
            if (snapshot.Revision == "second")
            {
                fixture.Source.Revision = "third";
            }

            return Task.CompletedTask;
        };
        ConcurrentQueue<ResourceReloadStatus> statuses = [];
        TaskCompletionSource published = NewSignal();
        fixture.Manager.Reloaded += (_, args) =>
        {
            statuses.Enqueue(args.Result.Status);
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
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
        await using Fixture fixture = new(new()
        {
            MaximumPendingChanges = 1,
        });
        using ResourceLease<Value> original = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        await fixture.InitialCheck.Task.WaitAsync(_timeout);
        TaskCompletionSource published = NewSignal();
        fixture.Manager.Reloaded += (_, _) => published.TrySetResult();
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
        Assert.Equal(ResourceReloadStatus.Published, fixture.Manager.LastReloadResult!.Status);
        using ResourceLease<Value> current = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        Assert.Equal("second", current.Value.Text);
    }

    /// <summary>Watcher failures remain observable while ordinary resource loading remains available.</summary>
    /// <returns>Completion of the faulted service and continued manager use.</returns>
    [Fact]
    public async Task WatcherFailureFaultsCompletionAsync()
    {
        await using Fixture fixture = new(ImmediateOptions());
        await fixture.Changes.Started.Task.WaitAsync(_timeout);
        fixture.Changes.Complete(new IOException("Watcher failed"));
        await Assert.ThrowsAsync<IOException>(() => fixture.Manager.HotReloadCompletion.WaitAsync(_timeout));
        Assert.IsType<IOException>(fixture.Manager.LastReloadError);
        using ResourceLease<Value> lease = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        Assert.Equal("first", lease.Value.Text);
    }

    /// <summary>Observer exceptions stop both workers and remain visible through completion.</summary>
    /// <returns>Completion of the worker failure and joined watcher.</returns>
    [Fact]
    public async Task ObserverFailureStopsWatcherAsync()
    {
        await using Fixture fixture = new(ImmediateOptions());
        using ResourceLease<Value> original = await fixture.Manager.AcquireLeaseForTestAsync(_key);
        await fixture.InitialCheck.Task.WaitAsync(_timeout);
        fixture.Manager.Reloaded += (_, _) => throw new InvalidOperationException("Observer failed");
        fixture.Source.Revision = "second";
        await fixture.Changes.SendAsync(new(_asset, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Manager.HotReloadCompletion.WaitAsync(_timeout));
        Assert.True(fixture.Changes.Stopped.Task.IsCompleted);
        original.Dispose();
        await fixture.Manager.DisposeAsync();
    }

    /// <summary>Throwing cancellation callbacks cannot bypass joining the watcher during disposal.</summary>
    /// <returns>Completion of manager disposal and observation of the cancellation failure.</returns>
    [Fact]
    public async Task CancellationCallbackFailureStillJoinsWorkersAsync()
    {
        await using Fixture fixture = new(ImmediateOptions(), throwOnCancellation: true);
        await fixture.Changes.Started.Task.WaitAsync(_timeout);
        Task first = fixture.Manager.DisposeAsync().AsTask();
        Task second = fixture.Manager.DisposeAsync().AsTask();
        await Task.WhenAll(first, second).WaitAsync(_timeout);
        Assert.True(fixture.Changes.Stopped.Task.IsCompleted);
        Assert.IsType<AggregateException>(fixture.Manager.LastReloadError);
        Assert.True(fixture.Manager.HotReloadCompletion.IsCompleted);
    }

    /// <summary>Initial publication checks each new input without rescanning previously loaded inputs.</summary>
    /// <returns>Completion of independent initial publications and their bounded revision checks.</returns>
    [Fact]
    public async Task InitialPublicationChecksOnlyNewInputsAsync()
    {
        await using Fixture fixture = new(ImmediateOptions());
        var checks = Channel.CreateUnbounded<bool>();
        fixture.Manager.Reloaded += (_, _) => checks.Writer.TryWrite(true);
        List<ResourceReference<Value>> references = [];
        try
        {
            for (int i = 0; i < 20; i++)
            {
                references.Add(await fixture.Manager.AcquireAsync(new ResourceKey<Value>($"input-{i}")));
                await checks.Reader.ReadAsync().AsTask().WaitAsync(_timeout);
            }

            Assert.Equal(40, fixture.Source.RevisionReads.Count);
            Assert.All(fixture.Source.RevisionReads.GroupBy(static id => id), reads => Assert.Equal(2, reads.Count()));
        }
        finally
        {
            foreach (ResourceReference<Value> reference in references)
            {
                reference.Dispose();
            }
        }
    }

    /// <summary>A change consumed before the first publication is rechecked after its input provenance becomes visible.</summary>
    /// <returns>Completion of publication of the changed input without another external notification.</returns>
    [Fact]
    public async Task InitialPublicationRechecksAnAlreadyConsumedChangeAsync()
    {
        await using Fixture fixture = new(ImmediateOptions());
        TaskCompletionSource entered = NewSignal();
        TaskCompletionSource resume = NewSignal();
        TaskCompletionSource published = NewSignal();
        fixture.Source.AfterRevision = async (_, token) =>
        {
            fixture.Source.AfterRevision = null;
            entered.TrySetResult();
            await resume.Task.WaitAsync(token);
        };
        fixture.Manager.Reloaded += (_, args) =>
        {
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
        Task<ResourceReference<Value>> pending = fixture.Manager.AcquireAsync(_key).AsTask();
        try
        {
            await entered.Task.WaitAsync(_timeout);
            fixture.Source.Revision = "second";
            await fixture.Changes.SendAsync(new(_asset, null));
            await fixture.InitialCheck.Task.WaitAsync(_timeout);
            Assert.Equal(ResourceReloadStatus.Unchanged, fixture.Manager.LastReloadResult!.Status);
        }
        finally
        {
            resume.TrySetResult();
        }

        using ResourceReference<Value> reference = await pending.WaitAsync(_timeout);
        await published.Task.WaitAsync(_timeout);
        using ResourceLease<Value> lease = reference.Acquire();
        Assert.Equal("second", lease.Value.Text);
    }

    /// <summary>Newly published inputs cannot continually extend the deadline of a pending change batch.</summary>
    /// <returns>Completion of a changed input's reload while new resources continue loading.</returns>
    [Fact]
    public async Task InitialPublicationsPreservePendingBatchDeadlineAsync()
    {
        await using Fixture fixture = new(new()
        {
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            MaximumBatchDelay = TimeSpan.FromMilliseconds(150),
            MaximumPendingChanges = 4096,
        });
        using ResourceReference<Value> original = await fixture.Manager.AcquireAsync(_key);
        await fixture.InitialCheck.Task.WaitAsync(_timeout);
        TaskCompletionSource published = NewSignal();
        fixture.Manager.Reloaded += (_, args) =>
        {
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
        fixture.Source.Revision = "second";
        List<ResourceReference<Value>> references = [];
        using CancellationTokenSource timeout = new(_timeout);
        try
        {
            while (!published.Task.IsCompleted)
            {
                await fixture.Changes.SendAsync(new(_asset, null));
                references.Add(await fixture.Manager.AcquireAsync(new ResourceKey<Value>($"input-{references.Count}"), timeout.Token));
                await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
            }

            using ResourceLease<Value> current = original.Acquire();
            Assert.Equal("second", current.Value.Text);
            Assert.False(fixture.Manager.HotReloadCompletion.IsCompleted);
        }
        finally
        {
            foreach (ResourceReference<Value> reference in references)
            {
                reference.Dispose();
            }
        }
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
        private readonly MemoryManager _memory = new();
        private readonly ResourceExecutionHost _execution = new();

        internal Fixture(ResourceReloadOptions? options = null, IResourceChangeSource? changes = null, bool throwOnCancellation = false)
        {
            Changes.ThrowOnCancellation = throwOnCancellation;
            Source = new() { Changes = changes ?? Changes };
            Manager = new("reload", Source, [Pipeline], _memory, _execution.Resources, reloadOptions: options);
            Manager.Reloaded += (_, _) => InitialCheck.TrySetResult();
        }

        internal TaskCompletionSource InitialCheck { get; } = NewSignal();

        internal ResourceManager Manager { get; }

        internal FakeSource Source { get; }

        internal FakeChanges Changes { get; } = new();

        internal Pipeline Pipeline { get; } = new();

        public async ValueTask DisposeAsync()
        {
            await Manager.DisposeAsync();
            await _execution.DisposeAsync();
            await _memory.DisposeAsync();
        }
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

        internal ConcurrentQueue<ResourceId> RevisionReads { get; } = new();

        internal Func<string, CancellationToken, Task>? AfterRevision { get; set; }

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

        public async ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RevisionReads.Enqueue(id);
            string revision = Revision;
            if (AfterRevision is { } after)
            {
                await after(revision, cancellationToken);
            }

            return revision;
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
