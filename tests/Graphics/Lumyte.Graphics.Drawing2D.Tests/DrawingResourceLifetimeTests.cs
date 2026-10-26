using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks one resource owner is shared internally without public program or pipeline leases.</summary>
public sealed class DrawingResourceLifetimeTests
{
    /// <summary>Checks prepared uses keep a single owner after renderer disposal and return it exactly once.</summary>
    [Fact]
    public void PreparedUsesOutliveTheRenderer()
    {
        var device = new DispatchDevice();
        var owner = new Owner();
        var resources = new DrawingResources2D(device, new Pipeline());
        var state = new DrawingResourceState2D(device, resources, owner);
        using IDisposable first = state.Retain();
        using IDisposable second = state.Retain();
        state.Dispose();
        state.Dispose();
        Assert.Equal(0, owner.Disposals);
        Assert.Same(resources, state.Resources);
        Assert.Throws<ObjectDisposedException>(() => state.Retain());
        first.Dispose();
        Assert.Equal(0, owner.Disposals);
        second.Dispose();
        second.Dispose();
        Assert.Equal(1, owner.Disposals);
        Assert.Throws<ObjectDisposedException>(() => state.Validate());
    }

    /// <summary>Checks the existing ResourceLease validator protects preparation and recording after loss.</summary>
    [Fact]
    public void RuntimeValidationDoesNotPreventReturningInvalidatedResources()
    {
        var device = new DispatchDevice();
        var owner = new Owner();
        bool lost = false;
        using var state = new DrawingResourceState2D(
            device,
            new(device, new Pipeline()),
            owner,
            () =>
            {
                if (lost)
                {
                    throw new InvalidOperationException("Lost device epoch.");
                }
            });
        using IDisposable use = state.Retain();
        lost = true;
        Assert.Throws<InvalidOperationException>(() => state.Validate());
        Assert.Throws<InvalidOperationException>(() => state.Retain());
        state.Dispose();
        Assert.Equal(0, owner.Disposals);
        use.Dispose();
        Assert.Equal(1, owner.Disposals);
    }

    /// <summary>Checks the last use retains a failed cleanup so disposal can be retried.</summary>
    [Fact]
    public void FailedOwnerCleanupCanBeRetried()
    {
        var device = new DispatchDevice();
        var owner = new Owner { FailOnce = true };
        using var state = new DrawingResourceState2D(device, new(device, new Pipeline()), owner);
        using IDisposable use = state.Retain();
        state.Dispose();
        Assert.Throws<InvalidOperationException>(() => use.Dispose());
        Assert.Equal(0, owner.Disposals);
        Assert.Throws<ObjectDisposedException>(() => state.Validate());
        use.Dispose();
        Assert.Equal(1, owner.Disposals);
    }

    /// <summary>Checks incompatible physical devices fail before ownership is transferred.</summary>
    [Fact]
    public void ForeignResourcesDoNotTransferTheirOwner()
    {
        var owner = new Owner();
        var resources = new DrawingResources2D(new DispatchDevice(), new Pipeline());
        Assert.Throws<ArgumentException>(() => new Renderer2D(new DispatchDevice(), DrawingCommandRenderers2D.CreateDefault(), resources, owner));
        Assert.Equal(0, owner.Disposals);
        owner.Dispose();
    }

    /// <summary>Checks a batch allocation failure returns its internal use while the renderer retains the owner.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task PreparationFailureReturnsTheBatchUseAsync()
    {
        var device = new DispatchDevice();
        var owner = new Owner();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), new(device, new Pipeline()), owner);
        await Assert.ThrowsAsync<NotSupportedException>(() => PrepareAsync(renderer));
        Assert.Equal(0, owner.Disposals);
        renderer.Dispose();
        Assert.Equal(1, owner.Disposals);
    }

    /// <summary>Checks cancellation after retaining resources releases the batch's protection.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task CancellationAfterRetainingResourcesReturnsTheBatchUseAsync()
    {
        using var cancellation = new CancellationTokenSource();
        var device = new DispatchDevice();
        var owner = new Owner();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), new(device, new Pipeline()), owner, cancellation.Cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PrepareAsync(renderer, cancellation.Token));
        renderer.Dispose();
        Assert.Equal(1, owner.Disposals);
    }

    /// <summary>Checks default shader and pipeline creation failures clean up each successfully created predecessor.</summary>
    /// <param name="pipelineFailure">Whether allocation fails at the pipeline or second module.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultCreationFailureReleasesNativePredecessorsAsync(bool pipelineFailure)
    {
        var created = new List<TrackedShader>();
        var device = new DispatchDevice
        {
            ShaderFactory = artifact =>
            {
                if (!pipelineFailure && created.Count == 1)
                {
                    throw new InvalidOperationException("Fragment allocation failed.");
                }

                var shader = new TrackedShader(artifact);
                created.Add(shader);
                return shader;
            },
            PipelineFactory = _ => throw new InvalidOperationException("Pipeline allocation failed."),
        };
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        await Assert.ThrowsAsync<InvalidOperationException>(() => PrepareAsync(renderer));
        Assert.Equal(pipelineFailure ? 2 : 1, created.Count);
        Assert.All(created, shader => Assert.Equal(1, shader.Disposals));
        renderer.Dispose();
        Assert.All(created, shader => Assert.Equal(1, shader.Disposals));
    }

    private static async Task PrepareAsync(Renderer2D renderer, CancellationToken cancellationToken = default)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 1, 1), new Color2D(1, 0, 0, 1));
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view }, cancellationToken);
    }

    private sealed class Pipeline : IGraphicsPipeline
    {
        public GraphicsPipelineDesc Desc { get; } = new() { VertexShader = new Shader(), FragmentShader = new Shader() };

        public void Dispose() => throw new InvalidOperationException("Borrowed pipelines are returned through their supplied owner.");
    }

    private sealed class Shader : IGraphicsShader
    {
        public ShaderArtifact Artifact => DrawingShaders2D.Fragment;

        public void Dispose() => throw new NotSupportedException();
    }

    private sealed class TrackedShader(ShaderArtifact artifact) : IGraphicsShader
    {
        public ShaderArtifact Artifact { get; } = artifact;

        internal int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    private sealed class Owner : IDisposable
    {
        internal int Disposals { get; private set; }

        internal bool FailOnce { get; init; }

        private bool Failed { get; set; }

        public void Dispose()
        {
            if (FailOnce && !Failed)
            {
                Failed = true;
                throw new InvalidOperationException("Retry cleanup.");
            }

            Disposals++;
        }
    }
}
