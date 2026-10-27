using System.Globalization;
using System.Runtime.CompilerServices;
using Lumyte.Memory;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Checks transient ownership and publication guarantees while reclamation is batched.</summary>
public sealed class ResourcePerformanceLifetimeTests
{
    /// <summary>Completed build work no longer retains its temporary payload through a live output.</summary>
    /// <param name="scenario">The source, dependency, callback or disposable ownership path.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CompletedBuildWorkReleasesManagedPayload(int scenario)
    {
        using RetentionProbe probe = CreateRetentionProbe(scenario);
        Assert.Equal(scenario == 1 ? 1 : 0, probe.TrimmedInputs);
        Assert.Equal(scenario < 2 ? 1 : 0, probe.State.SnapshotDisposals);
        Assert.Equal(scenario >= 2 ? 1 : 0, probe.State.CleanupCalls);
        Assert.Equal(0, probe.State.RootReleases);
        Assert.False(IsAliveAfterCollection(probe.State.Payload!));
        Assert.True(probe.Reference.IsAvailable);
        GC.KeepAlive(probe);
    }

    /// <summary>Disposable cleanup preserves reverse ownership order and retries only incomplete releases.</summary>
    /// <returns>Completion of build cleanup and failed and successful resident cleanup.</returns>
    [Fact]
    public async Task DisposableCleanupPreservesOrderAndRetryAsync()
    {
        await using MemoryManager memory = new();
        List<string> calls = [];
        Stage stage = new(
            context =>
            {
                context.RegisterCleanup(new LoggedDisposable(calls, "resident-first"), ResourceDependencyLifetime.Resident);
                context.RegisterCleanup(new LoggedDisposable(calls, "resident-retry", 1), ResourceDependencyLifetime.Resident);
                context.RegisterCleanup(new LoggedDisposable(calls, "resident-last"), ResourceDependencyLifetime.Resident);
                context.RegisterCleanup(new LoggedDisposable(calls, "build-first"));
                context.RegisterCleanup(new LoggedDisposable(calls, "build-last"));
                return new Output("root");
            },
            _ =>
            {
                calls.Add("output");
                return ValueTask.CompletedTask;
            });
        await using ResourceManager manager = new("disposable-cleanup", new EmptySource(), [stage], memory, new InlineRuntime());
        ResourceReference<Output> reference = await manager.RetainAsync(new ResourceKey<Output>("asset"));
        Assert.Equal(["build-last", "build-first"], calls);

        reference.Dispose();
        await Assert.ThrowsAsync<AggregateException>(async () => await manager.TrimAsync());
        Assert.Equal(["build-last", "build-first", "output", "resident-last", "resident-retry"], calls);
        Assert.Equal(1, await manager.TrimAsync());
        Assert.Equal(["build-last", "build-first", "output", "resident-last", "resident-retry", "resident-retry", "resident-first"], calls);
        Assert.Equal(0, await manager.TrimAsync());
    }

    /// <summary>Independent reclaimable outputs leave one publication while older snapshots keep their values pinned.</summary>
    /// <returns>Completion of batched reclamation and snapshot release.</returns>
    [Fact]
    public async Task BulkTrimPreservesCapturedPublicationAsync()
    {
        await using MemoryManager memory = new();
        List<Output> outputs = [];
        Stage stage = new(context =>
        {
            Output output = new(context.Address.Subresource);
            outputs.Add(output);
            return output;
        });
        await using ResourceManager manager = new("bulk-trim", new EmptySource(), [stage], memory, new InlineRuntime());
        ResourceReference<Output> original = await manager.RetainAsync(new ResourceKey<Output>("asset", "original"));
        using ResourceSnapshot captured = manager.CaptureSnapshot();
        ulong capturedVersion = captured.PublicationVersion;
        original.Dispose();
        for (int index = 0; index < 32; index++)
        {
            using ResourceReference<Output> added = await manager.RetainAsync(new ResourceKey<Output>("asset", index.ToString(CultureInfo.InvariantCulture)));
            Assert.False(added.TryPin(out ResourceLease<Output>? unexpected, captured));
            Assert.Null(unexpected);
        }

        ulong beforeTrim = manager.PublicationVersion;
        Assert.Equal(32, await manager.TrimAsync());
        Assert.Equal(beforeTrim + 1, manager.PublicationVersion);
        Assert.Equal(capturedVersion, captured.PublicationVersion);
        Assert.Equal(0, outputs[0].ReleaseCount);
        Assert.All(outputs.Skip(1), output => Assert.Equal(1, output.ReleaseCount));

        captured.Dispose();
        Assert.Equal(1, await manager.TrimAsync());
        Assert.All(outputs, output => Assert.Equal(1, output.ReleaseCount));
    }

    /// <summary>Cancellation of a reserved reclamation wave leaves unstarted entries available for later cleanup.</summary>
    /// <returns>Completion of cancellation and resumed reclamation.</returns>
    [Fact]
    public async Task CanceledTrimCanResumeWithoutRepeatingCompletedCleanupAsync()
    {
        await using MemoryManager memory = new();
        List<Output> outputs = [];
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int releaseStarts = 0;
        Stage stage = new(
            context =>
            {
                Output output = new(context.Address.Subresource);
                outputs.Add(output);
                return output;
            },
            async _ =>
            {
                if (Interlocked.Increment(ref releaseStarts) == 1)
                {
                    entered.SetResult();
                    await resume.Task;
                }
            });
        await using ResourceManager manager = new("cancel-trim", new EmptySource(), [stage], memory, new InlineRuntime());
        for (int index = 0; index < 16; index++)
        {
            using ResourceReference<Output> reference = await manager.RetainAsync(new ResourceKey<Output>("asset", index.ToString(CultureInfo.InvariantCulture)));
        }

        using CancellationTokenSource cancellation = new();
        Task<int> trimming = manager.TrimAsync(cancellation.Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
        }
        finally
        {
            resume.TrySetResult();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await trimming.WaitAsync(TimeSpan.FromSeconds(10)));
        await manager.TrimAsync();
        Assert.Equal(16, releaseStarts);
        Assert.All(outputs, output => Assert.Equal(1, output.ReleaseCount));
        Assert.Equal(0, await manager.TrimAsync());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RetentionProbe CreateRetentionProbe(int scenario)
    {
        RetentionState state = new();
        MemoryManager memory = new();
        IResourceStage[] stages = scenario == 1
            ? [new RetentionRootStage(scenario, state), new RetentionInputStage()]
            : [new RetentionRootStage(scenario, state)];
        ResourceManager manager = new("retention", new PayloadSource(state), stages, memory, new InlineRuntime());
        ResourceReference<Root> reference = manager.RetainAsync(new ResourceKey<Root>("asset")).GetAwaiter().GetResult();
        int trimmed = manager.TrimAsync().GetAwaiter().GetResult();
        return new(manager, memory, reference, state, trimmed);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAliveAfterCollection(WeakReference<byte[]> reference)
    {
        for (int index = 0; index < 3; index++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
        }

        return reference.TryGetTarget(out _);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterPayloadCleanup(ResourceBuildContext context, RetentionState state, bool disposable)
    {
        byte[] payload = state.CreatePayload();
        if (disposable)
        {
            context.RegisterCleanup(new PayloadDisposable(payload, state));
        }
        else
        {
            context.RegisterCleanup(() =>
            {
                state.CleanupCalls++;
                GC.KeepAlive(payload);
                return ValueTask.CompletedTask;
            });
        }
    }

    private sealed class Root;

    private sealed class Input;

    private sealed class RetentionState
    {
        internal WeakReference<byte[]>? Payload { get; private set; }

        internal int SnapshotDisposals { get; set; }

        internal int CleanupCalls { get; set; }

        internal int RootReleases { get; set; }

        internal byte[] CreatePayload()
        {
            byte[] payload = new byte[1024 * 1024];
            payload[0] = 1;
            Payload = new(payload);
            return payload;
        }
    }

    private sealed class RetentionProbe(ResourceManager manager, MemoryManager memory, ResourceReference<Root> reference, RetentionState state, int trimmedInputs) : IDisposable
    {
        internal ResourceReference<Root> Reference { get; } = reference;

        internal RetentionState State { get; } = state;

        internal int TrimmedInputs { get; } = trimmedInputs;

        public void Dispose()
        {
            Reference.Dispose();
            manager.DisposeAsync().GetAwaiter().GetResult();
            memory.DisposeAsync().GetAwaiter().GetResult();
        }
    }

    private sealed class RetentionRootStage(int scenario, RetentionState state) : IResourceSourceStage<Root>
    {
        public async ValueTask<Root> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            if (scenario == 0)
            {
                await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
            }
            else if (scenario == 1)
            {
                await context.RequireAsync(new ResourceKey<Input>(context.Address), ResourceDependencyLifetime.Build, cancellationToken);
            }
            else
            {
                RegisterPayloadCleanup(context, state, scenario == 3);
            }

            return new Root();
        }

        public ValueTask ReleaseAsync(Root resource)
        {
            state.RootReleases++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RetentionInputStage : IResourceSourceStage<Input>
    {
        public async ValueTask<Input> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
            return new Input();
        }

        public ValueTask ReleaseAsync(Input resource) => ValueTask.CompletedTask;
    }

    private sealed class PayloadSource(RetentionState state) : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ResourceSourceSnapshot>(new PayloadSnapshot(id, state.CreatePayload(), state));

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>("1");
    }

    private sealed class PayloadSnapshot(ResourceId id, byte[] payload, RetentionState state) : ResourceSourceSnapshot
    {
        public override ResourceId Id => id;

        public override string Revision => "1";

        public override long? Length => payload.Length;

        public override bool CanReadRange => false;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public override ValueTask DisposeAsync()
        {
            state.SnapshotDisposals++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PayloadDisposable(byte[] payload, RetentionState state) : IDisposable
    {
        public void Dispose()
        {
            state.CleanupCalls++;
            GC.KeepAlive(payload);
        }
    }

    private sealed class LoggedDisposable(List<string> calls, string name, int failures = 0) : IDisposable
    {
        public void Dispose()
        {
            calls.Add(name);
            if (failures > 0)
            {
                failures--;
                throw new IOException("Dispose must be retried.");
            }
        }
    }

    private sealed class InlineRuntime : IResourceRuntime, IResourceExecutor
    {
        public IResourceExecutor Executor => this;

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => context.ExecuteAsync(cancellationToken);

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource)
            => stage.ReleaseAsync(resource, runtime);
    }

    private sealed class Output(string name)
    {
        internal string Name { get; } = name;

        internal int ReleaseCount { get; set; }
    }

    private sealed class Stage(Func<ResourceBuildContext, Output> build, Func<Output, ValueTask>? release = null) : IResourceSourceStage<Output>
    {
        public ValueTask<Output> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(build(context));

        public async ValueTask ReleaseAsync(Output resource)
        {
            if (release is not null)
            {
                await release(resource);
            }

            resource.ReleaseCount++;
        }
    }

    private sealed class EmptySource : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(null);
    }
}
