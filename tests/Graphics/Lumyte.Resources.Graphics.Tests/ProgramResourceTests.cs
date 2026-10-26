using System.Text;
using System.Threading.Channels;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Resources.Graphics.Tests;

/// <summary>Checks resource-managed program publication, device recovery and exact lease ownership.</summary>
public sealed class ProgramResourceTests
{
    private const string ProgramId = "test.program.v1";
    private static readonly ResourceKey<GraphicsProgramResource> _key = new("program");

    /// <summary>Checks acquisitions perform no loading and a captured frame retains its program after hot reload.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task HotReloadPreservesFrameSnapshotAndIssuedLeasesAsync()
    {
        var state = new GraphicsTestState();
        var source = new MutableSource();
        await using ServiceProvider services = CreateServices(state, source);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(services);
        await services.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = services.GetRequiredService<ResourceManager>();
        using ResourceReference<GraphicsProgramResource> reference = await manager.AcquireAsync(_key);
        using ResourceSnapshot snapshot = manager.CaptureSnapshot();
        ResourceGraphicsProgramProvider programs = Provider(snapshot, reference);
        TestDevice device = Assert.Single(state.Devices);
        using GraphicsProgramLease first = programs.Acquire(device, ProgramId);
        using GraphicsProgramLease same = programs.Acquire(device, ProgramId);
        Assert.Same(first.Resources.Pipeline, same.Resources.Pipeline);
        Assert.Single(device.Programs);
        int reads = source.Reads;
        var reloaded = new TaskCompletionSource<ResourceReloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.Reloaded += (_, args) => reloaded.TrySetResult(args.Result);
        manager.ReloadFailed += (_, args) => reloaded.TrySetException(args.Error);
        source.Change();
        Assert.Equal(ResourceReloadStatus.Published, (await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(10))).Status);
        using GraphicsProgramLease oldFrame = programs.Acquire(device, ProgramId);
        Assert.Same(first.Resources.Pipeline, oldFrame.Resources.Pipeline);
        using ResourceSnapshot currentSnapshot = manager.CaptureSnapshot();
        using GraphicsProgramLease current = Provider(currentSnapshot, reference).Acquire(device, ProgramId);
        Assert.NotSame(first.Resources.Pipeline, current.Resources.Pipeline);
        Assert.NotEqual(first.Resources.FragmentArtifact.GetBinary(), current.Resources.FragmentArtifact.GetBinary());
        Assert.True(source.Reads > reads);
        int afterReload = source.Reads;
        snapshot.Dispose();
        Assert.Same(same.Resources, first.Resources);
        Assert.Equal(afterReload, source.Reads);
        Assert.All(device.Programs, pipeline => Assert.False(pipeline.Disposed));
    }

    /// <summary>Checks loss invalidates existing program leases and rebuild publishes the same handle on a new device.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task DeviceRecoveryReusesArtifactsAndRebuildsSameHandleAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider services = CreateServices(state, new MutableSource());
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(services);
        IGraphicsDeviceService devices = services.GetRequiredService<IGraphicsDeviceService>();
        await devices.InitializeAsync();
        ResourceManager manager = services.GetRequiredService<ResourceManager>();
        using ResourceReference<GraphicsProgramResource> reference = await manager.AcquireAsync(_key);
        ResourceHandle<GraphicsProgramResource> handle = reference.Handle;
        using ResourceSnapshot snapshot = manager.CaptureSnapshot();
        using GraphicsProgramLease old = Provider(snapshot, reference).Acquire(state.Devices[0], ProgramId);
        ShaderArtifact artifact = old.Resources.FragmentArtifact;
        TestProgramPipeline pipeline = Assert.IsType<TestProgramPipeline>(old.Resources.Pipeline);
        state.Devices[0].Lose();
        Assert.Throws<ResourceUnavailableException>(() => old.Resources);
        await devices.RecoverAsync();
        GraphicsResourceRuntime runtime = services.GetRequiredService<GraphicsResourceRuntime>();
        Assert.Equal(ResourceReloadStatus.Published, (await manager.RebuildRuntimeAsync([runtime.Id])).Status);
        using ResourceSnapshot rebuilt = manager.CaptureSnapshot();
        using GraphicsProgramLease current = Provider(rebuilt, reference).Acquire(state.Devices[1], ProgramId);
        Assert.Equal(handle, reference.Handle);
        Assert.Same(artifact, current.Resources.FragmentArtifact);
        Assert.NotSame(pipeline, current.Resources.Pipeline);
        Assert.False(pipeline.Disposed);
        snapshot.Dispose();
        old.Dispose();
        await manager.TrimAsync();
        Assert.True(pipeline.Disposed);
        Assert.True(state.Devices[0].Confirmations > 0);
    }

    /// <summary>Checks mismatched physical devices and unknown ABI requests do not fall back or load sources.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task WrongDeviceAndUnknownProgramAreRejectedWithoutLoadingAsync()
    {
        var state = new GraphicsTestState();
        var source = new MutableSource();
        await using ServiceProvider services = CreateServices(state, source);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(services);
        await services.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = services.GetRequiredService<ResourceManager>();
        using ResourceReference<GraphicsProgramResource> reference = await manager.AcquireAsync(_key);
        using ResourceSnapshot snapshot = manager.CaptureSnapshot();
        ResourceGraphicsProgramProvider programs = Provider(snapshot, reference);
        int reads = source.Reads;
        Assert.Throws<ArgumentException>(() => programs.Acquire(new TestDevice(), ProgramId));
        Assert.Throws<NotSupportedException>(() => programs.Acquire(state.Devices[0], "unknown"));
        Assert.Equal(reads, source.Reads);
        snapshot.Dispose();
        reference.Dispose();
        await manager.TrimAsync();
        Assert.True(Assert.Single(state.Devices[0].Programs).Disposed);
    }

    /// <summary>Checks cleanup retries preserve shader dependencies until pipeline destruction succeeds.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task CleanupFailureRetainsShadersUntilRetryAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider services = CreateServices(state, new MutableSource());
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(services);
        await services.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = services.GetRequiredService<ResourceManager>();
        using ResourceReference<GraphicsProgramResource> reference = await manager.AcquireAsync(_key);
        TestDevice device = Assert.Single(state.Devices);
        TestProgramPipeline pipeline = Assert.Single(device.Programs);
        pipeline.FailOnce = true;
        reference.Dispose();
        await Assert.ThrowsAnyAsync<Exception>(() => manager.TrimAsync().AsTask());
        Assert.False(pipeline.Disposed);
        Assert.All(device.Shaders, shader => Assert.False(shader.Disposed));
        await manager.TrimAsync();
        Assert.True(pipeline.Disposed);
        Assert.All(device.Shaders, shader => Assert.Equal(1, shader.DisposeCount));
    }

    /// <summary>Checks a failed fragment allocation releases the partially created vertex module.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task PartialCreationFailureReleasesNativePredecessorsAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider services = CreateServices(state, new MutableSource());
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(services);
        await services.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        TestDevice device = Assert.Single(state.Devices);
        device.CreateShaderFailOnCall = 2;
        ResourceManager manager = services.GetRequiredService<ResourceManager>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.AcquireAsync(_key).AsTask());
        await manager.TrimAsync();
        Assert.True(Assert.Single(device.Shaders).Disposed);
        Assert.Empty(device.Programs);
    }

    /// <summary>Checks a renderer provider and its batches independently retain the same exact generation.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task OwnedProviderKeepsBatchesAfterRendererAndReferenceDisposalAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider services = CreateServices(state, new MutableSource());
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(services);
        await services.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = services.GetRequiredService<ResourceManager>();
        using ResourceReference<GraphicsProgramResource> reference = await manager.AcquireAsync(_key);
        using var provider = new RetainedGraphicsProgramProvider<GraphicsProgramResource>(reference.Acquire(), static resource => resource, ProgramId);
        TestDevice device = Assert.Single(state.Devices);
        using GraphicsProgramLease batch = provider.Acquire(device, ProgramId);
        using GraphicsProgramLease second = provider.Acquire(device, ProgramId);
        TestProgramPipeline pipeline = Assert.IsType<TestProgramPipeline>(batch.Resources.Pipeline);
        Assert.Throws<NotSupportedException>(() => provider.Acquire(device, "unknown"));
        Assert.Throws<ArgumentException>(() => provider.Acquire(new TestDevice(), ProgramId));
        reference.Dispose();
        provider.Dispose();
        provider.Dispose();
        await manager.TrimAsync();
        Assert.Same(pipeline, batch.Resources.Pipeline);
        Assert.False(pipeline.Disposed);
        Assert.Throws<ObjectDisposedException>(() => provider.Acquire(device, ProgramId));
        batch.Dispose();
        await manager.TrimAsync();
        Assert.False(pipeline.Disposed);
        device.Lose();
        Assert.Throws<ResourceUnavailableException>(() => second.Resources);
        second.Dispose();
        await manager.TrimAsync();
        Assert.True(pipeline.Disposed);
    }

    /// <summary>Checks typed shader inputs are isolated by consumer and use the last registration.</summary>
    [Fact]
    public void ShaderRegistrationIsTypedAndLastWins()
    {
        var services = new ServiceCollection();
        services.UseGraphicsShaders<GraphicsProgramResource>(new("old-v"), new("old-f"));
        services.UseGraphicsShaders<GraphicsProgramResource>(new("new-v"), new("new-f"));
        services.UseGraphicsShaders<ImageEffects.ImageEffectShaderResource>(new("effect-v"), new("effect-f"));
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Equal(new ResourceKey<ShaderArtifact>("new-v"), provider.GetRequiredService<GraphicsShaderPackage<GraphicsProgramResource>>().Vertex);
        Assert.Equal(new ResourceKey<ShaderArtifact>("effect-f"), provider.GetRequiredService<GraphicsShaderPackage<ImageEffects.ImageEffectShaderResource>>().Fragment);
    }

    private static ResourceGraphicsProgramProvider Provider(ResourceSnapshot snapshot, ResourceReference<GraphicsProgramResource> reference)
        => new(snapshot, new Dictionary<string, ResourceHandle<GraphicsProgramResource>> { [ProgramId] = reference.Handle });

    private static ServiceProvider CreateServices(GraphicsTestState state, MutableSource source)
    {
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddSingleton(source);
        services.AddMemoryManagement();
        services.AddGraphicsDevice<TestFactory>(ServiceLifetime.Singleton);
        services.UseGraphicsShaders<GraphicsProgramResource>(new("vertex"), new("fragment"));
        services.AddResources(builder => builder.UseSource<MutableSource>()
            .AddShaderPipeline().AddGraphicsPrograms()
            .ConfigureHotReload(new() { DebounceDelay = TimeSpan.Zero, MaximumBatchDelay = TimeSpan.FromMilliseconds(10) }));
        services.AddResourceGraphics();
        return services.BuildServiceProvider();
    }

    private sealed class MutableSource : IResourceSource, IResourceChangeSource
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
