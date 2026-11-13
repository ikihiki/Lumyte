using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
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
            canvas.FillRect(new(i, 0, 1, 1), new SRgba<float>(1, 0, 0, 0.5f));
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
        canvas.FillPath(path.Build(), new SRgba<float>(1, 1, 1, 1));
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

    /// <summary>Checks failure after earlier chunks returns the frame slot even when their cleanup fails.</summary>
    /// <param name="cancel">Whether the later preparation is canceled instead of faulted.</param>
    /// <returns>The asynchronous test completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedLaterChunkPreservesFailureAndRetriesAllEarlierAssetsAsync(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        Exception preparationFailure = cancel
            ? new OperationCanceledException(cancellation.Token)
            : new InvalidOperationException("Injected later chunk preparation failure.");
        var cleanupFailure = new InvalidOperationException("Injected earlier asset cleanup failure.");
        var first = new CleanupFaultBatch(cleanupFailure, 1);
        var second = new CleanupFaultBatch(null, 0);
        int preparations = 0;
        var strategies = new DispatchRenderers();
        var faulting = new DelegateFillPath((_, _, _) =>
        {
            preparations++;
            if (preparations == 3)
            {
                if (cancel)
                {
                    cancellation.Cancel();
                }

                throw preparationFailure;
            }

            return preparations == 1 ? first : second;
        });
        using var renderer = new Renderer2D(new DispatchDevice(), strategies.CreateTable(faulting), retentionOptions: new() { MaxFramesInFlight = 1 });
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var scene = new DrawingScene2D();
        for (int i = 0; i < 3; i++)
        {
            scene.Insert(scene.Root, i, PathContent(1));
        }

        using (DrawingSceneReadLease2D source = scene.Borrow())
        {
            AggregateException error = await Assert.ThrowsAsync<AggregateException>(async () => await renderer.PrepareAsync(source, new() { View = view }, cancellation.Token));
            Assert.Contains(preparationFailure, error.Flatten().InnerExceptions);
            Assert.Contains(cleanupFailure, error.Flatten().InnerExceptions);
        }

        Assert.Equal(3, preparations);
        Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
        Assert.Equal(1, first.DisposeAttempts);
        Assert.Equal(1, second.DisposeAttempts);

        using var emptyScene = new DrawingScene2D();
        using DrawingSceneReadLease2D retrySource = emptyScene.Borrow();
        using PreparedDrawingFrame2D retry = await renderer.PrepareAsync(retrySource, new() { View = view });
        Assert.Equal(2, first.DisposeAttempts);
        Assert.Equal(1, first.SuccessfulDisposals);
        Assert.Equal(1, second.DisposeAttempts);
        Assert.Equal(1, renderer.RetentionStatistics.ActiveFrames);
        retry.Dispose();
        Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
    }

    /// <summary>Checks partial runs attempt every batch and retain only failed cleanup for renderer disposal.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task FailedCommandWithinChunkAttemptsEveryBatchAndRetriesOnlyFailuresAsync()
    {
        var preparationFailure = new InvalidOperationException("Injected fourth command preparation failure.");
        var cleanupFailure = new InvalidOperationException("Injected middle batch cleanup failure.");
        CleanupFaultBatch[] batches =
        [
            new CleanupFaultBatch(null, 0),
            new CleanupFaultBatch(cleanupFailure, 1),
            new CleanupFaultBatch(null, 0),
        ];
        int preparations = 0;
        var strategies = new DispatchRenderers();
        var faulting = new DelegateFillPath((_, _, _) =>
        {
            if (preparations == batches.Length)
            {
                throw preparationFailure;
            }

            return batches[preparations++];
        });
        using var renderer = new Renderer2D(new DispatchDevice(), strategies.CreateTable(faulting), retentionOptions: new() { MaxFramesInFlight = 1 });
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, PathContent(4));
        using DrawingSceneReadLease2D source = scene.Borrow();
        AggregateException error = await Assert.ThrowsAsync<AggregateException>(async () => await renderer.PrepareAsync(source, new() { View = view }));
        Assert.Contains(preparationFailure, error.Flatten().InnerExceptions);
        Assert.Contains(cleanupFailure, error.Flatten().InnerExceptions);
        Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
        Assert.All(batches, batch => Assert.Equal(1, batch.DisposeAttempts));

        renderer.Dispose();
        Assert.Equal(1, batches[0].DisposeAttempts);
        Assert.Equal(2, batches[1].DisposeAttempts);
        Assert.Equal(1, batches[2].DisposeAttempts);
        Assert.All(batches, batch => Assert.Equal(1, batch.SuccessfulDisposals));
        renderer.Dispose();
        Assert.Equal(2, batches[1].DisposeAttempts);
    }

    /// <summary>Checks persistent retiring cleanup blocks allocation without leaking or reporting a busy frame.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task PersistentFailedPreparationCleanupDoesNotOccupyAFrameSlotAsync()
    {
        var preparationFailure = new InvalidOperationException("Injected later preparation failure.");
        var cleanupFailure = new InvalidOperationException("Injected persistent cleanup failure.");
        var batch = new CleanupFaultBatch(cleanupFailure, int.MaxValue);
        int preparations = 0;
        var strategies = new DispatchRenderers();
        var faulting = new DelegateFillPath((_, _, _) => ++preparations == 1 ? batch : throw preparationFailure);
        using var renderer = new Renderer2D(new DispatchDevice(), strategies.CreateTable(faulting), retentionOptions: new() { MaxFramesInFlight = 1 });
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, PathContent(1));
        scene.Insert(scene.Root, 1, PathContent(1));
        using (DrawingSceneReadLease2D source = scene.Borrow())
        {
            AggregateException error = await Assert.ThrowsAsync<AggregateException>(async () => await renderer.PrepareAsync(source, new() { View = view }));
            Assert.Contains(preparationFailure, error.Flatten().InnerExceptions);
            Assert.Contains(cleanupFailure, error.Flatten().InnerExceptions);
        }

        Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
        using var emptyScene = new DrawingScene2D();
        using DrawingSceneReadLease2D retrySource = emptyScene.Borrow();
        Exception? retryError = await Record.ExceptionAsync(async () => await renderer.PrepareAsync(retrySource, new() { View = view }));
        Assert.NotNull(retryError);
        if (retryError is AggregateException aggregate)
        {
            Assert.Contains(cleanupFailure, aggregate.Flatten().InnerExceptions);
        }
        else
        {
            Assert.Same(cleanupFailure, retryError);
        }

        Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
        Assert.Equal(2, batch.DisposeAttempts);
        Assert.Equal(2, preparations);

        batch.FailuresRemaining = 0;
        using PreparedDrawingFrame2D retry = await renderer.PrepareAsync(retrySource, new() { View = view });
        Assert.Equal(3, batch.DisposeAttempts);
        Assert.Equal(1, batch.SuccessfulDisposals);
        retry.Dispose();
        Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
    }

    /// <summary>Checks confirmed device retirement requires disposed commands and never publishes residency.</summary>
    /// <param name="attachSubmission">Whether a pending submission is attached before retirement.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeviceRetirementReleaseRequiresDisposedCommandsAndNeverPublishes(bool attachSubmission)
    {
        var publications = new List<bool>();
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var frame = new PreparedDrawingFrame2D(new DispatchDevice(), new() { View = view }, [], publications.Add);
        var commands = new DispatchCommands();
        frame.Record(commands);
        if (attachSubmission)
        {
            commands.Submit();
            frame.AttachSubmission(new Submission { Status = SubmissionStatus.Pending });
        }

        Assert.Throws<InvalidOperationException>(frame.ReleaseAfterDeviceRetirement);
        Assert.Empty(publications);
        commands.Dispose();
        frame.ReleaseAfterDeviceRetirement();
        frame.ReleaseAfterDeviceRetirement();
        frame.Dispose();
        Assert.Equal(new[] { false }, publications);
        Assert.Throws<ObjectDisposedException>(() => frame.Record(new DispatchCommands()));
    }

    /// <summary>Checks failed retirement cleanup retains the release callback and retries without publication.</summary>
    [Fact]
    public void DeviceRetirementReleaseRetriesFailedCleanup()
    {
        int attempts = 0;
        var publications = new List<bool>();
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var frame = new PreparedDrawingFrame2D(new DispatchDevice(), new() { View = view }, [], completed =>
        {
            if (++attempts == 1)
            {
                throw new ArithmeticException("retirement cleanup");
            }

            publications.Add(completed);
        });
        var commands = new DispatchCommands();
        frame.Record(commands);
        commands.Dispose();
        Assert.Throws<ArithmeticException>(frame.ReleaseAfterDeviceRetirement);
        Assert.Empty(publications);
        frame.ReleaseAfterDeviceRetirement();
        frame.Dispose();
        Assert.Equal(2, attempts);
        Assert.Equal(new[] { false }, publications);
    }

    private static DrawList2D PathContent(int batches)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        var path = new PathBuilder2D();
        path.MoveTo(Vector2.Zero);
        path.LineTo(new(4, 0));
        path.LineTo(new(0, 4));
        path.Close();
        Path2D shape = path.Build();
        for (int i = 0; i < batches; i++)
        {
            // Each distinct clip stack forces a separate prepared batch in the same small chunk.
            canvas.Save();
            canvas.ClipPath(shape);
            canvas.FillPath(shape, new SRgba<float>(1, 1, 1, 1));
            canvas.Restore();
        }

        return canvas.End();
    }

    private sealed class DelegateFillPath(Func<RenderPreparationContext2D, ReadOnlyMemory<FillPathCommand2D>, CancellationToken, IPreparedCommandBatch2D> prepare) : IFillPathRenderer2D
    {
        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken)
            => ValueTask.FromResult(prepare(context, commands, cancellationToken));
    }

    private sealed class CleanupFaultBatch(Exception? failure, int failuresRemaining) : IPreparedCommandBatch2D
    {
        internal int DisposeAttempts { get; private set; }

        internal int SuccessfulDisposals { get; private set; }

        internal int FailuresRemaining { get; set; } = failuresRemaining;

        public void DeclareResources(IDrawingResourceDeclarations2D resources)
        {
        }

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
        {
        }

        public void Dispose()
        {
            DisposeAttempts++;
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw failure!;
            }

            SuccessfulDisposals++;
        }
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
