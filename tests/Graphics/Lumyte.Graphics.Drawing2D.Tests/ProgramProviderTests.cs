using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks default drawing obtains and returns external program leases on every failure path.</summary>
public sealed class ProgramProviderTests
{
    /// <summary>Checks a returned old device program cannot reach native drawing allocation.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ForeignDeviceProgramIsReturnedAsync()
    {
        var programs = new BorrowedProvider(new DispatchDevice());
        await Assert.ThrowsAsync<ArgumentException>(() => PrepareAsync(new DispatchDevice(), programs));
        Assert.Equal(1, programs.Returned);
    }

    /// <summary>Checks a failure after acquiring a valid program releases the external lease.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task LaterNativeFailureReturnsProgramAsync()
    {
        var device = new DispatchDevice();
        var programs = new BorrowedProvider(device);
        await Assert.ThrowsAsync<NotSupportedException>(() => PrepareAsync(device, programs));
        Assert.Equal(1, programs.Returned);
    }

    /// <summary>Checks cancellation raised during acquisition wins before native batch allocation.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task CancellationDuringAcquisitionReturnsProgramAsync()
    {
        using var cancellation = new CancellationTokenSource();
        var device = new DispatchDevice();
        var programs = new BorrowedProvider(device, cancellation.Cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PrepareAsync(device, programs, cancellation.Token));
        Assert.Equal(1, programs.Returned);
    }

    /// <summary>Checks an unavailable provider is propagated without an embedded fallback.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ProviderFailureDoesNotLoadDefaultProgramAsync()
    {
        var error = new InvalidOperationException("The resource is not resident.");
        var programs = new BorrowedProvider(new DispatchDevice(), () => throw error);
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => PrepareAsync(new DispatchDevice(), programs)));
        Assert.Equal(0, programs.Returned);
    }

    private static async Task PrepareAsync(IGraphicDevice device, IGraphicsProgramProvider programs, CancellationToken cancellationToken = default)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 1, 1), new Color2D(1, 0, 0, 1));
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), programs);
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view }, cancellationToken);
    }

    private sealed class BorrowedProvider(IGraphicDevice owner, Action? acquired = null) : IGraphicsProgramProvider
    {
        private readonly ShaderArtifact _artifact = ShaderArtifact.LoadEmbedded(typeof(DrawingPrograms2D).Assembly, "Lumyte.Drawing2D.drawing-fragment.lshader");

        internal int Returned { get; private set; }

        public GraphicsProgramLease Acquire(IGraphicDevice device, string programId)
        {
            Assert.Equal(DrawingPrograms2D.ProgramId, programId);
            acquired?.Invoke();
            return new(new(owner, new BorrowedPipeline(), _artifact), () => Returned++);
        }
    }

    private sealed class BorrowedPipeline : IGraphicsPipeline
    {
        public GraphicsPipelineDesc Desc => throw new NotSupportedException();

        public void Dispose() => throw new InvalidOperationException("A borrowed pipeline must not be disposed by drawing.");
    }
}
