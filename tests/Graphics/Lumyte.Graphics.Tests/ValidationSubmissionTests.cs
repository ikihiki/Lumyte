using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Tests transactional queue diagnostics against managed command and resource identities.</summary>
public sealed class ValidationSubmissionTests
{
    private static readonly BarrierScope _write = new(PipelineStage.Copy, ResourceAccess.CopyWrite);
    private static readonly BarrierScope _read = new(PipelineStage.Copy, ResourceAccess.CopyRead);

    /// <summary>Checks asynchronous native failure never exposes a guessed queue state for reuse.</summary>
    /// <param name="enabled">Whether diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedExecutionRequiresDeviceRecreation(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var command = new FakeCommand();
        validation?.Complete(command, false);
        CheckInvalid(enabled, () => validation?.PrepareSubmission([command]));
    }

    /// <summary>Checks composite disposal preflight leaves the wrapper usable when its backing is busy.</summary>
    [Fact]
    public void CompositeDisposalPreflightDoesNotReleaseWrapper()
    {
        var validation = new GraphicsValidation();
        object wrapper = new(), storage = new(), device = new(), command = new();
        validation.Register(wrapper, device);
        validation.Register(storage, device);
        validation.Use(command, storage);
        Assert.Throws<InvalidOperationException>(() => validation.ValidateRelease(storage));
        validation.Use(command, wrapper);
        validation.Complete(command);
        validation.Release(wrapper);
        validation.Release(storage);
        validation.Release(device);
    }

    /// <summary>Checks adjacent buffer ranges do not introduce a dependency across commands.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisjointBufferRangesCanBeSubmittedTogether(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var first = new FakeCommand();
        using var second = new FakeCommand();
        object buffer = new();
        validation?.Access(first, buffer, _write, 0, 16);
        validation?.Access(second, buffer, _read, 16, 16);
        Commit(validation, [first, second]);
    }

    /// <summary>Checks a partial barrier leaves the remaining overlapping bytes hazardous.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialBufferBarrierDoesNotCoverAnOverlappingRead(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var write = new FakeCommand();
        using var partial = new FakeCommand();
        using var read = new FakeCommand();
        using var remaining = new FakeCommand();
        object buffer = new();
        validation?.Access(write, buffer, _write, 0, 32);
        validation?.Barrier(partial, buffer, _write, _read, 0, 16);
        validation?.Access(read, buffer, _read, 8, 16);
        CheckInvalid(enabled, () => validation?.PrepareSubmission([write, partial, read]));
        validation?.Barrier(remaining, buffer, _write, _read, 16, 16);
        Commit(validation, [write, partial, remaining, read]);
    }

    /// <summary>Checks committed texture states persist across command and submission boundaries.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextureTransitionsUseCommittedQueueState(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var texture = new FakeTexture();
        using var initialize = new FakeCommand();
        using var stale = new FakeCommand();
        using var next = new FakeCommand();
        validation?.SetTextureState(texture, TextureState.Undefined);
        validation?.TextureBarrier(initialize, Transition(texture, TextureState.Undefined, TextureState.CopyDestination, default, _write));
        validation?.TextureAccess(initialize, texture, new(0, 1, 0, 1), TextureState.CopyDestination, _write);
        Commit(validation, [initialize]);
        validation?.Complete(initialize);

        // Recording starts with local state; only preflight can compare another command's Before state.
        validation?.TextureBarrier(stale, Transition(texture, TextureState.Undefined, TextureState.CopySource, default, _read));
        CheckInvalid(enabled, () => validation?.PrepareSubmission([stale]));
        validation?.TextureBarrier(next, Transition(texture, TextureState.CopyDestination, TextureState.CopySource, _write, _read));
        validation?.TextureAccess(next, texture, new(0, 1, 0, 1), TextureState.CopySource, _read);
        Commit(validation, [next]);
    }

    /// <summary>Checks failed preflight publishes neither earlier texture transitions nor semaphore changes.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedPreflightLeavesQueueAndSemaphoresUnchanged(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var texture = new FakeTexture();
        using var transition = new FakeCommand();
        using var write = new FakeCommand();
        using var read = new FakeCommand();
        using var available = new FakeSemaphore();
        using var rendered = new FakeSemaphore();
        object buffer = new(), acquisition = new();
        validation?.SetTextureState(texture, TextureState.Undefined);
        validation?.TextureBarrier(transition, Transition(texture, TextureState.Undefined, TextureState.CopyDestination, default, _write));
        validation?.Access(write, buffer, _write, 0, 16);
        validation?.Access(read, buffer, _read, 0, 16);
        validation?.PresentationSignaled(acquisition, available);
        var desc = new QueueSubmitDesc
        {
            CommandBuffers = [transition, write, read],
            WaitSemaphores = [new() { Semaphore = available, Stages = PipelineStage.Copy }],
            SignalSemaphores = [rendered],
        };
        validation?.ValidateSemaphores(desc);
        CheckInvalid(enabled, () => validation?.PrepareSubmission(desc.CommandBuffers));

        // Both calls remain legal only if the failed attempt neither consumed nor signaled anything.
        validation?.ValidateSemaphores(desc);
        validation?.PresentationWaitValidate([available]);
        validation?.PresentationSignalValidate(rendered);
        CheckInvalid(enabled, () => validation?.PresentationWaitValidate([rendered]));

        // Replaying Undefined and the original write also detects partial publication of either global table.
        Commit(validation, [transition, write]);
        validation?.CompleteSemaphores(acquisition);
        validation?.Release(rendered);
    }

    /// <summary>Checks abandoning prepared state after native failure permits an unchanged retry.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparingWithoutCommittingDoesNotPublishNativeSuccess(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var texture = new FakeTexture();
        using var command = new FakeCommand();
        object buffer = new();
        validation?.SetTextureState(texture, TextureState.Undefined);
        validation?.TextureBarrier(command, Transition(texture, TextureState.Undefined, TextureState.CopyDestination, default, _write));
        validation?.Access(command, buffer, _write, 0, 16);
        GraphicsValidation.SubmissionState? abandoned = validation?.PrepareSubmission([command]);
        if (enabled)
        {
            Assert.NotNull(abandoned);
        }

        // The first prepared state deliberately never reaches CommitSubmission or Submit.
        Commit(validation, [command]);
        CheckInvalid(enabled, () => validation?.PrepareSubmission([command]));
    }

    /// <summary>Checks a registration invalidated after capture is revalidated during submission preparation.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaleCapturedReferenceIsRejectedAtPrepare(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var command = new FakeCommand();
        var reference = new FakeReference();
        validation?.Reference(command, reference);
        Assert.Equal(enabled ? 1 : 0, reference.ValidationCount);
        reference.Alive = false;
        CheckInvalid(enabled, () => validation?.PrepareSubmission([command]));
        Assert.Equal(enabled ? 2 : 0, reference.ValidationCount);
        validation?.Discard(command);
        Commit(validation, [command]);
    }

    /// <summary>Checks cancellation and an early completion observation retain command and semaphore uses.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    /// <returns>The cancellation and retirement observations.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnobservedCompletionRetainsInFlightDependenciesAsync(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var command = new FakeCommand();
        using var semaphore = new FakeSemaphore();
        object resource = new(), submission = new();
        validation?.Use(command, resource);
        Commit(validation, [command]);
        validation?.Submit(command);
        validation?.SubmittedSemaphores(submission, new QueueSubmitDesc { CommandBuffers = [command], SignalSemaphores = [semaphore] });
        bool nativeComplete = false;
        int completionCount = 0;
        bool ObserveCompletion()
        {
            if (!nativeComplete)
            {
                return false;
            }

            validation?.Complete(command);
            validation?.CompleteSemaphores(submission);
            completionCount++;
            return true;
        }

        Assert.False(ObserveCompletion());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var wait = new SurfaceFrameLifetime(ObserveCompletion);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait.WaitForReleaseAsync(cancellation.Token).AsTask());
        Assert.Equal(0, completionCount);
        CheckInvalid(enabled, () => validation?.Discard(command));
        CheckInvalid(enabled, () => validation?.Map(resource));
        CheckInvalid(enabled, () => validation?.Release(resource));
        CheckInvalid(enabled, () => validation?.Release(semaphore));

        nativeComplete = true;
        await wait.WaitForReleaseAsync();
        Assert.Equal(1, completionCount);
        validation?.Map(resource);
        validation?.Release(resource);
        validation?.Discard(command);
        validation?.PresentationWaitValidate([semaphore]);
        validation?.PresentationWaited(submission, [semaphore]);
        validation?.CompleteSemaphores(submission);
        validation?.PresentationSignalValidate(semaphore);
        validation?.Release(semaphore);
    }

    /// <summary>Checks a write dependency covers every outstanding shader reader, not only the last reader.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MergedShaderReadersRequireBothBeforeStages(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var vertex = new FakeCommand();
        using var fragment = new FakeCommand();
        using var incomplete = new FakeCommand();
        using var complete = new FakeCommand();
        using var write = new FakeCommand();
        object buffer = new();
        BarrierScope vertexRead = new(PipelineStage.VertexShader, ResourceAccess.ShaderRead);
        BarrierScope fragmentRead = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
        validation?.Access(vertex, buffer, vertexRead, 0, 16);
        validation?.Access(fragment, buffer, fragmentRead, 0, 16);
        validation?.Barrier(incomplete, buffer, fragmentRead, _write, 0, 16);
        validation?.Barrier(complete, buffer, new(PipelineStage.VertexShader | PipelineStage.FragmentShader, ResourceAccess.ShaderRead), _write, 0, 16);
        validation?.Access(write, buffer, _write, 0, 16);
        CheckInvalid(enabled, () => validation?.PrepareSubmission([vertex, fragment, incomplete, write]));
        Commit(validation, [vertex, fragment, complete, write]);
    }

    /// <summary>Checks the first read does not erase the visibility scope needed by later shader readers.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VertexReadDoesNotGrantFragmentVisibility(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var write = new FakeCommand();
        using var narrow = new FakeCommand();
        using var broad = new FakeCommand();
        using var vertex = new FakeCommand();
        using var fragment = new FakeCommand();
        object buffer = new();
        BarrierScope vertexRead = new(PipelineStage.VertexShader, ResourceAccess.ShaderRead);
        validation?.Access(write, buffer, _write, 0, 16);
        validation?.Barrier(narrow, buffer, _write, vertexRead, 0, 16);
        validation?.Barrier(broad, buffer, _write, new(PipelineStage.VertexShader | PipelineStage.FragmentShader, ResourceAccess.ShaderRead), 0, 16);
        validation?.Access(vertex, buffer, vertexRead, 0, 16);
        validation?.Access(fragment, buffer, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead), 0, 16);
        CheckInvalid(enabled, () => validation?.PrepareSubmission([write, narrow, vertex, fragment]));
        Commit(validation, [write, broad, vertex, fragment]);
    }

    /// <summary>Checks an empty destination scope cannot authorize an access.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyBarrierAfterDoesNotGrantAccess(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var write = new FakeCommand();
        using var empty = new FakeCommand();
        using var covered = new FakeCommand();
        using var read = new FakeCommand();
        object buffer = new();
        validation?.Access(write, buffer, _write, 0, 16);
        validation?.Barrier(empty, buffer, _write, default, 0, 16);
        validation?.Barrier(covered, buffer, _write, _read, 0, 16);
        validation?.Access(read, buffer, _read, 0, 16);
        CheckInvalid(enabled, () => validation?.PrepareSubmission([write, empty, read]));
        Commit(validation, [write, covered, read]);
    }

    /// <summary>Checks consecutive dependencies must connect their destination and source scopes.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsecutiveBarriersRequireConnectedScopes(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var write = new FakeCommand();
        using var first = new FakeCommand();
        using var disconnected = new FakeCommand();
        using var connected = new FakeCommand();
        using var read = new FakeCommand();
        object buffer = new();
        BarrierScope vertexRead = new(PipelineStage.VertexShader, ResourceAccess.ShaderRead);
        validation?.Access(write, buffer, _write, 0, 16);
        validation?.Barrier(first, buffer, _write, vertexRead, 0, 16);
        validation?.Barrier(disconnected, buffer, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead), _read, 0, 16);
        validation?.Barrier(connected, buffer, vertexRead, _read, 0, 16);
        validation?.Access(read, buffer, _read, 0, 16);
        CheckInvalid(enabled, () => validation?.PrepareSubmission([write, first, disconnected, read]));
        Commit(validation, [write, first, connected, read]);
    }

    /// <summary>Checks a selective global shader dependency leaves an unrelated copy write hazardous.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectiveMemoryBarrierPreservesUnrelatedCopyHazards(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var writes = new FakeCommand();
        using var barrier = new FakeCommand();
        using var shaderRead = new FakeCommand();
        using var copyRead = new FakeCommand();
        using var copyBarrier = new FakeCommand();
        object shader = new(), copy = new();
        BarrierScope computeWrite = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite);
        BarrierScope computeRead = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead);
        validation?.Access(writes, shader, computeWrite, 0, 16);
        validation?.Access(writes, copy, _write, 0, 16);
        validation?.MemoryBarrier(barrier, computeWrite, computeRead);
        validation?.Access(shaderRead, shader, computeRead, 0, 16);
        validation?.Access(copyRead, copy, _read, 0, 16);
        validation?.Barrier(copyBarrier, copy, _write, _read, 0, 16);
        CheckInvalid(enabled, () => validation?.PrepareSubmission([writes, barrier, shaderRead, copyRead]));
        Commit(validation, [writes, barrier, shaderRead, copyBarrier, copyRead]);
    }

    /// <summary>Checks failure on a second resource rolls back the first use and newly captured references only.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordingFailureRollsBackEarlierAccessAndReferences(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var command = new FakeCommand();
        object first = new(), conflicting = new();
        var retained = new FakeReference();
        var rolledBack = new FakeReference();
        validation?.Access(command, conflicting, _write, 0, 16);
        validation?.Reference(command, retained);
        CheckInvalid(enabled, () =>
        {
            using GraphicsValidation.RecordingTransaction? recording = validation?.BeginRecording(command);
            validation?.Access(command, first, _read, 0, 16);
            validation?.Reference(command, rolledBack);
            validation?.Access(command, conflicting, _read, 0, 16);
            recording?.Commit();
        });

        validation?.Release(first);
        rolledBack.Alive = false;
        Commit(validation, [command]);
        Assert.Equal(enabled ? 1 : 0, rolledBack.ValidationCount);
        Assert.Equal(enabled ? 2 : 0, retained.ValidationCount);
        CheckInvalid(enabled, () => validation?.Release(conflicting));
        retained.Alive = false;
        CheckInvalid(enabled, () => validation?.PrepareSubmission([command]));
        validation?.Discard(command);
        validation?.Release(conflicting);
    }

    /// <summary>Checks same-batch metadata writes invalidate stale reads while matching snapshots remain valid.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameBatchMetadataWriteRejectsStaleRead(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var write = new FakeCommand();
        using var stale = new FakeCommand();
        using var matching = new FakeCommand();
        ShaderDependencyMetadata original = new([]);
        ShaderDependencyMetadata copied = new(original.References);
        var source = new FakeMetadataSource(original);
        validation?.Metadata(write, source, 0, copied, write: true);
        validation?.Metadata(stale, source, 0, original, write: false);

        // Capture a matching read independently, then restore the pre-submit source value.
        source.SetTransferredReferences(0, copied);
        validation?.Metadata(matching, source, 0, copied, write: false);
        source.SetTransferredReferences(0, original);
        CheckInvalid(enabled, () => validation?.PrepareSubmission([write, stale]));
        Assert.Same(original, source.ReadReferences(0));
        Commit(validation, [write, matching]);
        Assert.Same(original, source.ReadReferences(0));
    }

    /// <summary>Checks abandoned native-submit preparation publishes no metadata or resource hazards.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbandonedMetadataPreparationLeavesGlobalsUnchanged(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var write = new FakeCommand();
        using var originalRead = new FakeCommand();
        object buffer = new();
        ShaderDependencyMetadata original = new([]);
        ShaderDependencyMetadata copied = new(original.References);
        var source = new FakeMetadataSource(original);
        validation?.Metadata(write, source, 0, copied, write: true);
        validation?.Access(write, buffer, _write, 0, 16);
        validation?.Metadata(originalRead, source, 0, original, write: false);
        validation?.Access(originalRead, buffer, _read, 0, 16);
        GraphicsValidation.SubmissionState? abandoned = validation?.PrepareSubmission([write]);
        if (enabled)
        {
            Assert.NotNull(abandoned);
        }

        Assert.Same(original, source.ReadReferences(0));
        Commit(validation, [originalRead]);
    }

    /// <summary>Checks completed semaphore signals provide memory visibility only at the requested wait stages.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SemaphoreWaitStagesSupplyCrossSubmissionMemoryDependency(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var producer = new FakeCommand();
        using var consumer = new FakeCommand();
        using var semaphore = new FakeSemaphore();
        object buffer = new(), signalOwner = new(), waitOwner = new();
        validation?.Access(producer, buffer, _write, 0, 16);
        Commit(validation, [producer]);
        validation?.SubmittedSemaphores(signalOwner, new QueueSubmitDesc
        {
            CommandBuffers = [producer],
            SignalSemaphores = [semaphore],
        });
        validation?.Complete(producer);
        validation?.CompleteSemaphores(signalOwner);

        // Completion retires the native use, not the outstanding binary signal or its memory scope.
        validation?.PresentationWaitValidate([semaphore]);
        CheckInvalid(enabled, () => validation?.PresentationSignalValidate(semaphore));
        validation?.Access(consumer, buffer, _read, 0, 16);
        CheckInvalid(enabled, () => validation?.PrepareSubmission([consumer]));
        var wrongStages = new QueueSubmitDesc
        {
            CommandBuffers = [consumer],
            WaitSemaphores = [new() { Semaphore = semaphore, Stages = PipelineStage.FragmentShader }],
        };
        validation?.ValidateSemaphores(wrongStages);
        CheckInvalid(enabled, () => validation?.PrepareSubmission(wrongStages));

        // Failed preparation must neither consume the signal nor publish its narrower visibility.
        validation?.ValidateSemaphores(wrongStages);
        validation?.PresentationWaitValidate([semaphore]);
        QueueSubmitDesc correctStages = wrongStages with
        {
            WaitSemaphores = [new() { Semaphore = semaphore, Stages = PipelineStage.Copy }],
        };
        validation?.ValidateSemaphores(correctStages);
        if (validation != null)
        {
            GraphicsValidation.SubmissionState prepared = validation.PrepareSubmission(correctStages);
            validation.CommitSubmission(prepared);
        }

        validation?.SubmittedSemaphores(waitOwner, correctStages);
        CheckInvalid(enabled, () => validation?.PresentationWaitValidate([semaphore]));
        validation?.Complete(consumer);
        validation?.CompleteSemaphores(waitOwner);
        validation?.PresentationSignalValidate(semaphore);
    }

    /// <summary>Checks partial diagnostic recording and native failure never publish a source snapshot.</summary>
    /// <param name="enabled">Whether managed diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialOperationAndNativeFailureLeaveSourceSnapshotUnchanged(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var command = new FakeCommand();
        using var originalRead = new FakeCommand();
        ShaderDependencyMetadata original = new([]);
        ShaderDependencyMetadata copied = new(original.References);
        var source = new FakeMetadataSource(original);
        object buffer = new();
        validation?.Access(command, buffer, _write, 0, 16);

        CheckInvalid(enabled, () =>
        {
            using GraphicsValidation.RecordingTransaction? recording = validation?.BeginRecording(command);
            validation?.Metadata(command, source, 0, copied, write: true);
            validation?.Access(command, buffer, _read, 0, 16);
            recording?.Commit();
        });
        Assert.Same(original, source.ReadReferences(0));

        // Reading the original in the same command proves the partial metadata operation rolled back.
        validation?.Metadata(command, source, 0, original, write: false);
        validation?.Metadata(originalRead, source, 0, original, write: false);
        using (GraphicsValidation.RecordingTransaction? recording = validation?.BeginRecording(command))
        {
            validation?.Metadata(command, source, 0, copied, write: true);

            // Native recording failed: leave the transaction uncommitted.
        }

        Assert.Same(original, source.ReadReferences(0));
        GraphicsValidation.SubmissionState? abandoned = validation?.PrepareSubmission([command]);
        if (enabled)
        {
            Assert.NotNull(abandoned);
        }

        // Native submission failed: leave the prepared queue state uncommitted as well.
        Assert.Same(original, source.ReadReferences(0));
        Commit(validation, [command, originalRead]);
        Assert.Same(original, source.ReadReferences(0));
    }

    private static void CheckInvalid(bool enabled, Action action)
    {
        if (enabled)
        {
            Assert.Throws<InvalidOperationException>(action);
        }
        else
        {
            action();
        }
    }

    private static void Commit(GraphicsValidation? validation, IReadOnlyList<IGraphicsCommandBuffer> commands)
    {
        if (validation != null)
        {
            GraphicsValidation.SubmissionState prepared = validation.PrepareSubmission(commands);
            validation.CommitSubmission(prepared);
        }
    }

    private static TextureBarrierDesc Transition(IGraphicsTexture texture, TextureState beforeState, TextureState afterState, BarrierScope before, BarrierScope after)
        => new() { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = beforeState, AfterState = afterState, Before = before, After = after };

    private sealed class FakeMetadataSource(IShaderReferenceMetadata value) : IShaderDataSource
    {
        /// <summary>Gets the unused layout for this sequencing-only source.</summary>
        public IShaderDataLayout Layout => throw new NotSupportedException();

        /// <summary>Gets the logical backing size.</summary>
        public ulong SizeInBytes => 1;

        /// <summary>Gets the unused memory preference.</summary>
        public MemoryPreference Memory => default;

        /// <summary>Gets the source identity.</summary>
        public object ShaderHandle => this;

        /// <summary>Reads the single element snapshot.</summary>
        /// <param name="index">The logical element index.</param>
        /// <returns>The currently published snapshot.</returns>
        public IShaderReferenceMetadata ReadReferences(ulong index) => index == 0 ? value : throw new ArgumentOutOfRangeException(nameof(index));

        /// <summary>Publishes the single element snapshot.</summary>
        /// <param name="index">The logical element index.</param>
        /// <param name="snapshot">The transferred snapshot.</param>
        public void SetTransferredReferences(ulong index, IShaderReferenceMetadata snapshot)
        {
            if (index != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            value = snapshot;
        }

        /// <summary>Validates this always-live managed source.</summary>
        public void ValidateAlive()
        {
        }
    }

    private sealed class FakeReference : IValidatedShaderReference
    {
        public bool Alive { get; set; } = true;

        public int ValidationCount { get; private set; }

        /// <summary>Revalidates the captured registration identity.</summary>
        public void ValidateRegistration()
        {
            ValidationCount++;
            if (!Alive)
            {
                throw new InvalidOperationException("Registration was released after capture.");
            }
        }
    }

    private sealed class FakeSemaphore : IGraphicsSemaphore
    {
        /// <summary>Disposes the managed fake.</summary>
        public void Dispose()
        {
        }
    }

    private sealed class FakeTexture : IGraphicsTexture
    {
        public uint Width => 1;

        public uint Height => 1;

        public uint MipLevels => 1;

        public uint ArrayLayers => 1;

        public TextureFormat Format => TextureFormat.Rgba8Unorm;

        public TextureUsage Usage => TextureUsage.CopySource | TextureUsage.CopyDestination;

        /// <summary>Gets the single mip size.</summary>
        /// <param name="mipLevel">The mip index.</param>
        /// <returns>The one-pixel dimensions.</returns>
        public (uint Width, uint Height) GetMipSize(uint mipLevel) => mipLevel == 0 ? (1u, 1u) : throw new ArgumentOutOfRangeException(nameof(mipLevel));

        /// <summary>Rejects unsupported view creation.</summary>
        /// <param name="desc">The view description.</param>
        /// <returns>No view is created.</returns>
        public IGraphicsTextureView CreateView(TextureViewDesc? desc = null) => throw new NotSupportedException();

        /// <summary>Disposes the managed fake.</summary>
        public void Dispose()
        {
        }
    }

    private sealed class FakeCommand : IGraphicsCommandBuffer
    {
        public CommandBufferState State { get; private set; } = CommandBufferState.Executable;

        /// <summary>Rejects native buffer copies.</summary>
        /// <param name="source">The source slice.</param>
        /// <param name="destination">The destination slice.</param>
        public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
            where TSource : unmanaged
            where TDestination : unmanaged => throw new NotSupportedException();

        /// <summary>Rejects native shader-data copies.</summary>
        /// <param name="source">The source slice.</param>
        /// <param name="destination">The destination slice.</param>
        public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
            where T : IShaderData => throw new NotSupportedException();

        /// <summary>Rejects native texture copies.</summary>
        /// <param name="source">The source region.</param>
        /// <param name="destination">The destination region.</param>
        public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination) => throw new NotSupportedException();

        /// <summary>Rejects native buffer-to-texture copies.</summary>
        /// <param name="source">The source layout.</param>
        /// <param name="destination">The destination region.</param>
        public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination) => throw new NotSupportedException();

        /// <summary>Rejects native texture-to-buffer copies.</summary>
        /// <param name="source">The source region.</param>
        /// <param name="destination">The destination layout.</param>
        public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => throw new NotSupportedException();

        /// <summary>Rejects native barriers.</summary>
        /// <param name="barrier">The dependency description.</param>
        public void Barrier(MemoryBarrierDesc barrier) => throw new NotSupportedException();

        /// <summary>Rejects native buffer barriers.</summary>
        /// <param name="barrier">The dependency description.</param>
        public void Barrier<T>(BufferBarrierDesc<T> barrier)
            where T : unmanaged => throw new NotSupportedException();

        /// <summary>Rejects native buffer barriers.</summary>
        /// <param name="barrier">The dependency description.</param>
        public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
            where T : IShaderData => throw new NotSupportedException();

        /// <summary>Rejects native barriers.</summary>
        /// <param name="barrier">The dependency description.</param>
        public void Barrier(TextureBarrierDesc barrier) => throw new NotSupportedException();

        /// <summary>Rejects native render passes.</summary>
        /// <param name="desc">The pass description.</param>
        public IRenderEncoder BeginRenderPass(RenderPassDesc desc) => throw new NotSupportedException();

        /// <summary>Rejects native compute passes.</summary>
        /// <param name="desc">The pass description.</param>
        public IComputeEncoder BeginComputePass(ComputePassDesc desc) => throw new NotSupportedException();

        /// <summary>Marks the fake executable.</summary>
        public void Finish() => State = CommandBufferState.Executable;

        /// <summary>Disposes the managed fake.</summary>
        public void Dispose() => State = CommandBufferState.Disposed;
    }
}
