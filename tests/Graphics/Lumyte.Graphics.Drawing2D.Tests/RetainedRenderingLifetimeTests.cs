using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks completion-safe retained ownership without accessing a native queue.</summary>
public sealed class RetainedRenderingLifetimeTests
{
    /// <summary>Checks every retirement precondition and one-shot recording.</summary>
    [Fact]
    public void RetirementRequiresSubmissionCompletionAndCommandDisposal()
    {
        bool released = false;
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var frame = new PreparedDrawingFrame2D(new DispatchDevice(), new() { View = view }, [], completed => released = completed);
        var commands = new DispatchCommands();
        frame.Record(commands);
        Assert.Throws<InvalidOperationException>(() => frame.Record(commands));
        Assert.Throws<InvalidOperationException>(frame.Retire);
        Assert.Throws<InvalidOperationException>(frame.Dispose);
        commands.Submit();
        var submission = new Submission();
        frame.AttachSubmission(submission);
        Assert.Throws<InvalidOperationException>(frame.Retire);
        submission.Status = SubmissionStatus.Completed;
        Assert.Throws<InvalidOperationException>(frame.Retire);
        commands.Dispose();
        frame.Retire();
        Assert.True(released);
        frame.Dispose();
    }

    /// <summary>Checks abort is explicit and cannot release live command memory.</summary>
    [Fact]
    public void RecordedAbortRequiresDisposedUnsubmittedCommands()
    {
        bool released = false;
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var frame = new PreparedDrawingFrame2D(new DispatchDevice(), new() { View = view }, [], completed => released = !completed);
        var commands = new DispatchCommands();
        frame.Record(commands);
        Assert.Throws<InvalidOperationException>(frame.AbortUnsubmitted);
        commands.Dispose();
        frame.AbortUnsubmitted();
        Assert.True(released);
    }

    /// <summary>Checks a prepared frame bounds future preparation until released.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task BusySlotsAndDisposedSourcesAreRejectedAsync()
    {
        var strategies = new DispatchRenderers();
        using var renderer = new Renderer2D(new DispatchDevice(), strategies.CreateTable(), retentionOptions: new() { MaxFramesInFlight = 1 });
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var snapshot = new DrawingSnapshot2D(0, []);
        var target = new RenderTarget2D { View = view };
        using PreparedDrawingFrame2D first = await renderer.PrepareAsync(snapshot, target);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await renderer.PrepareAsync(snapshot, target));
        first.Dispose();
        using PreparedDrawingFrame2D second = await renderer.PrepareAsync(snapshot, target);
        second.Dispose();
        snapshot.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await renderer.PrepareAsync(snapshot, target));
    }

    /// <summary>Checks immutable pages coalesce and external batches are freshly prepared each frame.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task CustomRendererUsesDefaultBatchingAndNeverAssumesRepeatabilityAsync()
    {
        var strategies = new DispatchRenderers();
        using var renderer = new Renderer2D(new DispatchDevice(), strategies.CreateTable());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 130; i++)
        {
            canvas.FillRect(new(i, 0, 1, 1), new Color2D(1, 0, 0, 0.5f));
        }

        DrawList2D content = canvas.End();
        using var snapshot = new DrawingSnapshot2D(1, [new(default, content, Matrix3x2.Identity, [], 1, 1)]);
        for (int i = 0; i < 2; i++)
        {
            using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, new() { View = view });
            var commands = new DispatchCommands();
            frame.Record(commands);
            commands.Submit();
            frame.AttachSubmission(new Submission { Status = SubmissionStatus.Completed });
            commands.Dispose();
            frame.Retire();
        }

        Assert.Equal(2, strategies.Batches.Count);
        Assert.Equal(260, strategies.Commands.Count);
        Assert.All(strategies.Batches, batch => Assert.Equal(1, batch.DisposeCount));
    }

    /// <summary>Checks a partially recorded frame cannot publish residency even if later submitted.</summary>
    [Fact]
    public void FailedRecordCannotAttachOrRetire()
    {
        bool released = false;
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var frame = new PreparedDrawingFrame2D(new DispatchDevice(), new() { View = view }, [new ThrowingBatch()], _ => released = true);
        var commands = new DispatchCommands();
        Assert.Throws<InvalidOperationException>(() => frame.Record(commands));
        commands.Submit();
        commands.Dispose();
        Assert.Throws<InvalidOperationException>(() => frame.AttachSubmission(new Submission { Status = SubmissionStatus.Completed }));
        Assert.Throws<InvalidOperationException>(frame.Retire);
        Assert.False(released);

        // This test deliberately confirms no submission took place despite the fake state.
        frame.AbortUnsubmitted();
        Assert.True(released);
    }

    /// <summary>Checks renderer disposal cannot release resources retained by an active prepared frame.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task RendererDisposalKeepsPreparedResourceOwnerUntilRetirementAsync()
    {
        var device = new DispatchDevice();
        var owner = new ResourceOwner();
        var strategies = new DispatchRenderers();
        var retaining = new RetainingFillPath();
        using var renderer = new Renderer2D(device, strategies.CreateTable(retaining), new(device, new Pipeline()), owner);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        var path = new PathBuilder2D();
        path.MoveTo(Vector2.Zero);
        path.LineTo(new(4, 0));
        path.LineTo(new(0, 4));
        path.Close();
        canvas.FillPath(path.Build(), new Color2D(1, 1, 1, 1));
        using var snapshot = new DrawingSnapshot2D(1, [new(default, canvas.End(), Matrix3x2.Identity, [], 1, 1)]);
        using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, new() { View = view });
        renderer.Dispose();
        Assert.Equal(0, owner.Disposals);
        var commands = new DispatchCommands();
        frame.Record(commands);
        commands.Submit();
        frame.AttachSubmission(new Submission { Status = SubmissionStatus.Completed });
        Assert.Equal(0, owner.Disposals);
        commands.Dispose();
        frame.Retire();
        Assert.Equal(1, owner.Disposals);
    }

    private sealed class RetainingFillPath : IFillPathRenderer2D
    {
        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken)
            => ValueTask.FromResult<IPreparedCommandBatch2D>(new RetainingBatch(context, context.RetainResources()));
    }

    private sealed class RetainingBatch(RenderPreparationContext2D context, IDisposable retained) : IPreparedCommandBatch2D
    {
        public void DeclareResources(IDrawingResourceDeclarations2D resources)
        {
        }

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target) => context.ValidateResources();

        public void Dispose() => retained.Dispose();
    }

    private sealed class Pipeline : IGraphicsPipeline
    {
        public GraphicsPipelineDesc Desc { get; } = new() { VertexShader = new Shader(), FragmentShader = new Shader() };

        public void Dispose() => throw new InvalidOperationException("Borrowed pipeline.");
    }

    private sealed class Shader : IGraphicsShader
    {
        public ShaderArtifact Artifact => DrawingShaders2D.Fragment;

        public void Dispose() => throw new InvalidOperationException("Borrowed shader.");
    }

    private sealed class ResourceOwner : IDisposable
    {
        internal int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    private sealed class ThrowingBatch : IPreparedCommandBatch2D
    {
        public void DeclareResources(IDrawingResourceDeclarations2D resources)
        {
        }

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target) => throw new InvalidOperationException("Injected recording failure.");

        public void Dispose()
        {
        }
    }

    private sealed class Submission : IGraphicsSubmission
    {
        public SubmissionStatus Status { get; set; }

        public ValueTask WaitAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Dispose() => Status = SubmissionStatus.Disposed;
    }
}
