using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Checks presentation is an explicit export state on an acquired image, not a hidden queue operation.</summary>
public sealed class PresentationExportTests
{
    /// <summary>An acquired image receives its final Present barrier after the last clear.</summary>
    /// <returns>The asynchronous completion.</returns>
    [Fact]
    public async Task AcquiredSurfaceImageCanBeReleasedToPresentAsync()
    {
        await using var host = new PlanningHost();
        var native = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, native);
        await using var frame = new FrameOwner(session.CreateFrame());
        var texture = new ManagedTexture(new() { Width = 4, Height = 4, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment }, native.Events);
        var surface = new SurfaceImage(texture);
        PlanTexture output = frame.Plan.ImportSurfaceTexture(surface, new TestLease(), initialized: false, TextureState.Undefined);
        PlanPass clear = frame.Plan.AddPass("clear-acquired-image");
        output = clear.Write(output, output.Range, PlanWriteMode.Discard, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), TextureState.ColorAttachment);
        clear.Clear(output, default);
        frame.Plan.Export(output, TextureState.Present, new(PipelineStage.AllCommands, ResourceAccess.None));
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        await using var execution = new ExecutionOwner(session.CreateExecution(prepared.Value));
        await execution.Value.RecordAsync();
        Assert.Equal(TextureState.Present, native.Commands!.TextureBarriers[^1].AfterState);
        Assert.False(surface.PresentCalled);
        Assert.DoesNotContain("submit", native.Events);
    }

    /// <summary>Ordinary allocations cannot forge a swapchain release state.</summary>
    /// <returns>The asynchronous completion.</returns>
    [Fact]
    public async Task OrdinaryTextureCannotRequestPresentAsync()
    {
        await using var host = new PlanningHost();
        await using var session = new SessionOwner(host.Module, new ManagedDevice());
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanTexture texture = frame.Plan.CreateTexture(new() { Width = 4, Height = 4, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment });
        Assert.Throws<ArgumentException>(() => frame.Plan.Export(texture, TextureState.Present, new(PipelineStage.AllCommands, ResourceAccess.None)));
    }

    private sealed class SurfaceImage(IGraphicsTexture texture) : IGraphicsSurfaceFrame
    {
        public SurfaceFrameStatus Status => SurfaceFrameStatus.Acquired;

        public IGraphicsTexture Texture => texture;

        internal bool PresentCalled { get; private set; }

        public SurfaceStatus Present(IReadOnlyList<IGraphicsSemaphore>? waitSemaphores = null)
        {
            PresentCalled = true;
            throw new NotSupportedException("Presentation is explicitly host-owned in this test.");
        }

        public ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}
