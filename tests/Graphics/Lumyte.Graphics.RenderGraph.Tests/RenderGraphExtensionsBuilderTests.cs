using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Verifies deferred preparation and ownership when extension configurations are built.</summary>
public sealed class RenderGraphExtensionsBuilderTests
{
    /// <summary>Registration reserves types without loading and building preserves preparation order.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task BuildPreparesOnceInOrderAndTransfersOwnersAsync()
    {
        var events = new List<string>();
        var sharedOwner = new Owner("shared", events);
        using RenderGraphExtensionsBuilder builder = new RenderGraphExtensionsBuilder()
            .Add(_ => new FirstState(), sharedOwner)
            .AddAsync<SecondState>(_ =>
            {
                events.Add("prepare-second");
                return ValueTask.FromResult(new RenderGraphExtensionFactory<SecondState>(static _ => new(), sharedOwner));
            })
            .AddAsync<ThirdState>(_ =>
            {
                events.Add("prepare-third");
                return ValueTask.FromResult(new RenderGraphExtensionFactory<ThirdState>(static _ => new(), new Owner("third", events)));
            });
        Assert.Empty(events);
        using RenderGraphExtensions extensions = await builder.BuildAsync();
        Assert.Equal(["prepare-second", "prepare-third"], events);
        Assert.Throws<InvalidOperationException>(() => builder.Add(_ => new object()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync().AsTask());
        builder.Dispose();
        Assert.Equal(["prepare-second", "prepare-third"], events);
        using var graph = new RenderGraph(extensions: extensions);
        Assert.IsType<FirstState>(graph.GetExtension<FirstState>());
        Assert.IsType<SecondState>(graph.GetExtension<SecondState>());
        Assert.IsType<ThirdState>(graph.GetExtension<ThirdState>());
        extensions.Dispose();
        Assert.Equal(["prepare-second", "prepare-third", "third", "shared"], events);
    }

    /// <summary>Duplicate state registration fails before calling an asynchronous preparation or taking its owner.</summary>
    [Fact]
    public void DuplicateRegistrationsDoNotLoadOrTransferRejectedOwner()
    {
        var releases = new List<string>();
        var rejected = new Owner("rejected", releases);
        using RenderGraphExtensionsBuilder builder = new RenderGraphExtensionsBuilder()
            .AddAsync<FirstState>(_ => throw new InvalidOperationException("Must remain deferred."));
        Assert.Throws<ArgumentException>(() => builder.Add(_ => new FirstState(), rejected));
        Assert.Throws<ArgumentException>(() => builder.AddAsync<FirstState>(_ => throw new InvalidOperationException()));
        builder.Dispose();
        Assert.Empty(releases);
        rejected.Dispose();
        Assert.Equal(["rejected"], releases);
    }

    /// <summary>An abandoned builder releases explicit owners even when no preparation has run.</summary>
    [Fact]
    public void AbandonedBuilderReleasesRegisteredOwners()
    {
        var releases = new List<string>();
        using RenderGraphExtensionsBuilder builder = new RenderGraphExtensionsBuilder()
            .Add(_ => new FirstState(), new Owner("first", releases))
            .Add(_ => new SecondState(), new Owner("second", releases))
            .AddAsync<ThirdState>(_ => throw new InvalidOperationException("Must remain deferred."));
        builder.Dispose();
        Assert.Equal(["second", "first"], releases);
        Assert.Throws<ObjectDisposedException>(() => builder.Add(_ => new object()));
    }

    /// <summary>Failed preparation releases acquired owners in reverse order without starting later work.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PreparationFailureCleansUpAndConsumesBuilderAsync()
    {
        var releases = new List<string>();
        var failure = new InvalidOperationException("Loading failed.");
        using RenderGraphExtensionsBuilder builder = new RenderGraphExtensionsBuilder()
            .Add(_ => new FirstState(), new Owner("first", releases))
            .AddAsync<SecondState>(_ => ValueTask.FromResult(new RenderGraphExtensionFactory<SecondState>(static _ => new(), new Owner("second", releases))))
            .AddAsync<ThirdState>(_ => ValueTask.FromException<RenderGraphExtensionFactory<ThirdState>>(failure))
            .AddAsync<object>(_ => throw new InvalidOperationException("Must not start."));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync().AsTask()));
        Assert.Equal(["second", "first"], releases);
        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync().AsTask());
        builder.Dispose();
        Assert.Equal(["second", "first"], releases);
    }

    /// <summary>A returned owner is retained before observing cancellation, including asynchronously returned owners.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CancellationAfterPreparationReturnsStillReleasesOwnerAsync()
    {
        var releases = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var prepared = new TaskCompletionSource<RenderGraphExtensionFactory<SecondState>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using RenderGraphExtensionsBuilder builder = new RenderGraphExtensionsBuilder()
            .Add(_ => new FirstState(), new Owner("first", releases))
            .AddAsync<SecondState>(token =>
            {
                Assert.Equal(cancellation.Token, token);
                return new(prepared.Task);
            })
            .AddAsync<ThirdState>(_ => throw new InvalidOperationException("Must not start."));
        Task<RenderGraphExtensions> build = builder.BuildAsync(cancellation.Token).AsTask();
        Assert.False(build.IsCompleted);
        cancellation.Cancel();
        prepared.SetResult(new(static _ => new(), new Owner("second", releases)));
        OperationCanceledException failure = await Assert.ThrowsAsync<OperationCanceledException>(() => build);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(["second", "first"], releases);
    }

    /// <summary>A token already canceled before building leaves the builder configurable and its owners untouched.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PreCanceledBuildDoesNotConsumeBuilderAsync()
    {
        var releases = new List<string>();
        using RenderGraphExtensionsBuilder builder = new RenderGraphExtensionsBuilder().Add(_ => new FirstState(), new Owner("first", releases));
        await Assert.ThrowsAsync<OperationCanceledException>(() => builder.BuildAsync(new(canceled: true)).AsTask());
        Assert.Empty(releases);
        builder.Add(_ => new SecondState());
        using RenderGraphExtensions extensions = await builder.BuildAsync();
    }

    /// <summary>Preparation and cleanup failures remain observable and the builder retains the remaining owners for retry.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FailedCleanupPreservesBothErrorsAndRemainingOwnersAsync()
    {
        var releases = new List<string>();
        var preparationFailure = new InvalidOperationException("Loading failed.");
        var cleanupFailure = new InvalidOperationException("Release failed.");
        using RenderGraphExtensionsBuilder builder = new RenderGraphExtensionsBuilder()
            .Add(_ => new FirstState(), new Owner("dependency", releases))
            .AddAsync<SecondState>(_ => ValueTask.FromResult(new RenderGraphExtensionFactory<SecondState>(static _ => new(), new Owner("failing", releases, cleanupFailure))))
            .AddAsync<ThirdState>(_ => ValueTask.FromResult(new RenderGraphExtensionFactory<ThirdState>(static _ => new(), new Owner("released", releases))))
            .AddAsync<object>(_ => ValueTask.FromException<RenderGraphExtensionFactory<object>>(preparationFailure));
        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(() => builder.BuildAsync().AsTask());
        Assert.Equal([preparationFailure, cleanupFailure], failure.InnerExceptions);
        Assert.Equal(["released", "failing"], releases);
        builder.Dispose();
        Assert.Equal(["released", "failing", "failing", "dependency"], releases);
        builder.Dispose();
        Assert.Equal(["released", "failing", "failing", "dependency"], releases);
    }

    /// <summary>A suspended preparation excludes further registration, a second build, and premature disposal.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PendingBuildRejectsMutationDisposalAndReentrantBuildAsync()
    {
        var prepared = new TaskCompletionSource<RenderGraphExtensionFactory<FirstState>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using RenderGraphExtensionsBuilder builder = new RenderGraphExtensionsBuilder().AddAsync<FirstState>(_ => new(prepared.Task));
        Task<RenderGraphExtensions> build = builder.BuildAsync().AsTask();
        Assert.False(build.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => builder.Add(_ => new SecondState()));
        Assert.Throws<InvalidOperationException>(() => builder.AddAsync<SecondState>(_ => throw new InvalidOperationException()));
        Assert.Throws<InvalidOperationException>(() => builder.Dispose());
        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync().AsTask());
        prepared.SetResult(new(static _ => new()));
        using RenderGraphExtensions extensions = await build;
        using var graph = new RenderGraph(extensions: extensions);
        Assert.IsType<FirstState>(graph.GetExtension<FirstState>());
    }

    private sealed class FirstState;

    private sealed class SecondState;

    private sealed class ThirdState;

    private sealed class Owner(string name, List<string> releases, Exception? failOnce = null) : IDisposable
    {
        private bool _failed;

        public void Dispose()
        {
            releases.Add(name);
            if (failOnce is { } failure && !_failed)
            {
                _failed = true;
                throw failure;
            }
        }
    }
}
