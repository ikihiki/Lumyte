using System.Text;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Memory;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Resources.Graphics.Tests;

/// <summary>Verifies shader loading, device recovery and graph-owned resource generations.</summary>
public sealed class ShaderResourceTests
{
    /// <summary>Direct loading leaves streams open and uses the same binary format as managed loading.</summary>
    /// <returns>Completion of the direct load and cancellation checks.</returns>
    [Fact]
    public async Task DirectLoadingSupportsSyncAsyncAndCancellationAsync()
    {
        byte[] binary = Artifact().GetBinary();
        using var input = new MemoryStream(binary);
        Assert.Equal(binary, ShaderArtifact.Load(input).GetBinary());
        input.Position = 0;
        Assert.Equal(binary, (await ShaderArtifact.LoadAsync(input)).GetBinary());
        Assert.True(input.CanRead);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ShaderArtifact.LoadAsync(input, cancellation.Token).AsTask());
    }

    /// <summary>Plans pin old shaders through recording and GPU completion; recovery reuses CPU artifacts.</summary>
    /// <returns>Completion of generation, retirement and ownership checks.</returns>
    [Fact]
    public async Task GraphRetainsShaderUntilCompletionAndRecoveryReusesArtifactAsync()
    {
        var state = new GraphicsTestState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddMemoryManagement();
        services.AddGraphicsDevice<TestFactory>(ServiceLifetime.Singleton);
        services.AddResources(builder => builder.UseSource<ShaderSource>().AddShaderPipeline());
        services.AddResourceGraphics();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        IGraphicsDeviceService devices = provider.GetRequiredService<IGraphicsDeviceService>();
        await devices.InitializeAsync();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        var key = new ResourceKey<GraphicsShaderResource>("shader");
        using ResourceReference<GraphicsShaderResource> reference = await manager.RetainAsync(key);
        using var graph = new RenderGraphBuilder(state.Devices[0]);
        ResourceRenderGraphImports imports = graph.ImportResources(manager);
        using ResourceSnapshot snapshot = manager.CaptureSnapshot();
        GraphicsShaderResource old = imports.Acquire(snapshot, reference.Handle);
        snapshot.Dispose();
        TestShader shader = Assert.IsType<TestShader>(old.Shader);
        ShaderArtifact artifact = shader.Artifact;
        using CompiledRenderGraph plan = graph.Compile();
        graph.Dispose();
        reference.Dispose();
        Assert.Equal(0, await manager.TrimAsync());
        using var commands = new TestGraphCommands();
        plan.Record(commands);
        commands.Finish();
        commands.Submit();
        Assert.Throws<InvalidOperationException>(() => plan.Dispose());
        Assert.False(shader.Disposed);
        commands.Complete();
        state.Devices[0].Lose();
        await devices.RecoverAsync();
        await manager.RebuildRuntimeAsync([provider.GetRequiredService<GraphicsResourceRuntime>().Id]);
        using ResourceReference<GraphicsShaderResource> currentReference = await manager.RetainAsync(key);
        using ResourceLease<GraphicsShaderResource> current = currentReference.Pin();
        Assert.Same(artifact, current.Value.Shader.Artifact);
        Assert.NotSame(old.Device, current.Value.Device);
        plan.Dispose();
        await manager.TrimAsync();
        Assert.True(shader.Disposed);
        current.Dispose();
        currentReference.Dispose();
        await manager.TrimAsync();
    }

    /// <summary>A lost device invalidates retained generations before graph recording.</summary>
    /// <returns>Completion of stale-input rejection and cleanup.</returns>
    [Fact]
    public async Task InvalidatedGraphInputIsRejectedBeforeRecordingAsync()
    {
        var state = new GraphicsTestState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddMemoryManagement();
        services.AddGraphicsDevice<TestFactory>(ServiceLifetime.Singleton);
        services.AddResources(builder => builder.UseSource<ShaderSource>().AddShaderPipeline());
        services.AddResourceGraphics();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        IGraphicsDeviceService devices = provider.GetRequiredService<IGraphicsDeviceService>();
        await devices.InitializeAsync();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        using var graph = new RenderGraphBuilder();
        ResourceRenderGraphImports imports = graph.ImportResources(manager);
        _ = await imports.AcquireAsync(new ResourceKey<GraphicsShaderResource>("shader"));
        using CompiledRenderGraph plan = graph.Compile();
        state.Devices[0].Lose();
        using var commands = new TestGraphCommands();
        Assert.Throws<ResourceUnavailableException>(() => plan.Record(commands));
        plan.Dispose();
        await manager.TrimAsync();
    }

    /// <summary>Texture imports keep the manager-owned allocation alive until the compiled plan is released.</summary>
    /// <returns>Completion of typed texture import and ownership checks.</returns>
    [Fact]
    public async Task TextureImportTransfersItsLeaseToTheCompiledPlanAsync()
    {
        var services = new ServiceCollection();
        services.AddMemoryManagement();
        services.AddResources(builder => builder.UseSource<ShaderSource>().AddSourcePipeline<IGraphicsTexture, TextureStage>());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        using var graph = new RenderGraphBuilder();
        ResourceRenderGraphImports imports = graph.ImportResources(manager);
        RenderGraphTexture texture = await imports.ImportTextureAsync(new("texture"), TextureState.Sampled, default);
        graph.MarkOutput(texture);
        using CompiledRenderGraph plan = graph.Compile();
        graph.Dispose();
        Assert.Equal(0, await manager.TrimAsync());
        using var commands = new TestGraphCommands();
        plan.Record(commands);
        Texture value = Assert.IsType<Texture>(plan.GetTexture(texture));
        Assert.False(value.Disposed);
        commands.Complete();
        plan.Dispose();
        Assert.Equal(1, await manager.TrimAsync());
        Assert.True(value.Disposed);
    }

    /// <summary>A loaded shader commits its resident bytes and reports them when reclaimed.</summary>
    /// <param name="lengthKnown">Whether the source exposes its byte length before reading.</param>
    /// <returns>Completion of successful allocation and reclamation accounting.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShaderMemoryIsCommittedAndReclaimedAsync(bool lengthKnown)
    {
        byte[] binary = Artifact().GetBinary();
        var services = new ServiceCollection();
        services.AddMemoryManagement();
        services.AddSingleton(new MemorySource(binary, lengthKnown));
        services.AddResources(builder => builder.UseSource<MemorySource>().AddSourcePipeline<ShaderArtifact, ShaderArtifactStage>());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        IMemoryManager memory = provider.GetRequiredService<IMemoryManager>();
        using (ResourceReference<ShaderArtifact> reference = await manager.RetainAsync(new ResourceKey<ShaderArtifact>("shader")))
        using (ResourceLease<ShaderArtifact> lease = reference.Pin())
        {
            Assert.Equal(binary.Length, lease.Value.BinaryLength);
            MemorySnapshot loaded = memory.GetSnapshot(new("cpu"));
            Assert.Equal((ulong)binary.Length, loaded.CommittedBytes);
            Assert.Equal(0UL, loaded.ReservedBytes);
        }

        Assert.Equal((ulong)binary.Length, await memory.ReclaimAsync(new("cpu"), (ulong)binary.Length));
        MemorySnapshot reclaimed = memory.GetSnapshot(new("cpu"));
        Assert.Equal(0UL, reclaimed.CommittedBytes);
        Assert.Equal(0UL, reclaimed.ReservedBytes);
    }

    /// <summary>A malformed binary returns both build buffers and the reserved resident budget.</summary>
    /// <param name="lengthKnown">Whether the source exposes its byte length before reading.</param>
    /// <returns>Completion of failed shader creation cleanup.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidShaderReturnsAllMemoryReservationsAsync(bool lengthKnown)
    {
        var services = new ServiceCollection();
        services.AddMemoryManagement();
        services.AddSingleton(new MemorySource([1, 2, 3], lengthKnown));
        services.AddResources(builder => builder.UseSource<MemorySource>().AddSourcePipeline<ShaderArtifact, ShaderArtifactStage>());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        await Assert.ThrowsAsync<ArgumentException>(() => manager.RetainAsync(new ResourceKey<ShaderArtifact>("broken")).AsTask());
        MemorySnapshot released = provider.GetRequiredService<IMemoryManager>().GetSnapshot(new("cpu"));
        Assert.Equal(0UL, released.CommittedBytes);
        Assert.Equal(0UL, released.ReservedBytes);
    }

    private static ShaderArtifact Artifact() => ShaderArtifact.PackTarget(
        ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", Encoding.UTF8.GetBytes("@compute @workgroup_size(1) fn main() {}"), "{}");

    private sealed class TextureStage : IResourceSourceStage<IGraphicsTexture>
    {
        public ValueTask<IGraphicsTexture> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => new(new Texture());

        public ValueTask ReleaseAsync(IGraphicsTexture resource)
        {
            resource.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Texture : IGraphicsTexture
    {
        public uint Width => 4;

        public uint Height => 4;

        public uint MipLevels => 1;

        public uint ArrayLayers => 1;

        public TextureFormat Format => TextureFormat.Rgba8Unorm;

        public TextureUsage Usage => TextureUsage.Sampled;

        internal bool Disposed { get; private set; }

        public (uint Width, uint Height) GetMipSize(uint mipLevel) => (4, 4);

        public IGraphicsTextureView CreateView(TextureViewDesc? desc = null) => throw new NotSupportedException();

        public void Dispose() => Disposed = true;
    }

    private sealed class MemorySource(byte[] binary, bool lengthKnown) : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => new(new Snapshot(id, binary, lengthKnown));

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => new("1");
    }

    private sealed class ShaderSource : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => new(new Snapshot(id, Artifact().GetBinary()));

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => new("1");
    }

    private sealed class Snapshot(ResourceId id, byte[] binary, bool lengthKnown = true) : ResourceSourceSnapshot
    {
        public override ResourceId Id => id;

        public override string Revision => "1";

        public override long? Length => lengthKnown ? binary.Length : null;

        public override bool CanReadRange => false;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default) => new(new MemoryStream(binary, writable: false));

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
