using System.Reflection;
using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Validation.Tests;

/// <summary>Managed regressions for acquisition readiness, wait visibility and incremental recording.</summary>
public sealed class StateReplayReviewTests
{
    private static readonly BarrierScope _copyWrite = new(PipelineStage.Copy, ResourceAccess.CopyWrite);
    private static readonly BarrierScope _copyRead = new(PipelineStage.Copy, ResourceAccess.CopyRead);
    private static readonly TextureSubresourceRange _range = new() { MipLevelCount = 1, ArrayLayerCount = 1 };

    /// <summary>Checks WaitVisibilityUnionsProducerStages.</summary>
    /// <param name="texture">Uses buffers or texture subresources.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WaitVisibilityUnionsProducerStages(bool texture)
    {
        foreach (bool reverse in new[] { false, true })
        {
            var validation = new GraphicsValidation();
            using var producer = new PresentationCommand();
            using var consumer = new PresentationCommand();
            using var image = new PresentationTexture();
            using var first = new PresentationSemaphore();
            using var second = new PresentationSemaphore();
            object resource = texture ? image : new object();
            if (texture)
            {
                validation.SetTextureState(image, TextureState.CopyDestination);
                validation.TextureAccess(producer, image, _range, TextureState.CopyDestination, _copyWrite);
                validation.TextureAccess(consumer, image, _range, TextureState.CopyDestination, _copyWrite);
            }
            else
            {
                validation.Access(producer, resource, _copyWrite);
                validation.Access(consumer, resource, _copyRead);
            }

            validation.CommitSubmission(validation.PrepareSubmission(new[] { producer }));
            validation.SubmittedSignal(producer, first);
            validation.SubmittedSignal(producer, second);
            SemaphoreWaitDesc[] waits = [new() { Semaphore = first, Stages = PipelineStage.FragmentShader }, new() { Semaphore = second, Stages = PipelineStage.Copy }];
            validation.PrepareSubmission(new QueueSubmitDesc { CommandBuffers = [consumer], WaitSemaphores = reverse ? waits.Reverse().ToArray() : waits });
        }
    }

    /// <summary>Checks AcquisitionReadinessSurvivesConsumedBinaryWait.</summary>
    /// <param name="chained">Uses a producer signal rather than queue order alone.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcquisitionReadinessSurvivesConsumedBinaryWait(bool chained)
    {
        var validation = new GraphicsValidation();
        using var image = new PresentationTexture();
        using var acquire = new PresentationSemaphore();
        using var signal = new PresentationSemaphore();
        using var first = new PresentationCommand();
        using var second = new PresentationCommand();
        object acquisition = new();
        validation.SetTextureState(image, TextureState.CopySource);
        validation.PresentationSignaled(acquisition, acquire);
        validation.AcquireTexture(acquisition, image, acquire);
        validation.TextureAccess(first, image, _range, TextureState.CopySource, _copyRead);
        validation.TextureAccess(second, image, _range, TextureState.CopySource, _copyRead);
        var firstDesc = new QueueSubmitDesc { CommandBuffers = [first], WaitSemaphores = [new() { Semaphore = acquire, Stages = PipelineStage.Copy }], SignalSemaphores = [signal] };
        GraphicsValidation.SubmissionState prepared = validation.PrepareSubmission(firstDesc);
        Assert.Throws<InvalidOperationException>(() => validation.PrepareSubmission(new[] { second }));
        validation.CommitSubmission(prepared);
        validation.SubmittedSemaphores(first, firstDesc);
        validation.PrepareSubmission(new QueueSubmitDesc { CommandBuffers = [second], WaitSemaphores = chained ? [new() { Semaphore = signal, Stages = PipelineStage.Copy }] : [] });
        Assert.Throws<InvalidOperationException>(() => validation.Release(acquire));
    }

    /// <summary>Checks UndefinedDiscardsLayoutButNotSourceDependency.</summary>
    /// <param name="synchronized">Includes the required source scope.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UndefinedDiscardsLayoutButNotSourceDependency(bool synchronized)
    {
        var validation = new GraphicsValidation();
        using var image = new PresentationTexture();
        using var producer = new PresentationCommand();
        using var consumer = new PresentationCommand();
        validation.SetTextureState(image, TextureState.CopyDestination);
        validation.TextureAccess(producer, image, _range, TextureState.CopyDestination, _copyWrite);
        validation.CommitSubmission(validation.PrepareSubmission(new[] { producer }));
        validation.TextureBarrier(consumer, new() { Texture = image, Range = _range, BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = synchronized ? _copyWrite : default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
        if (synchronized)
        {
            validation.PrepareSubmission(new[] { consumer });
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => validation.PrepareSubmission(new[] { consumer }));
        }
    }

    /// <summary>Checks FailedRecordingRestoresIncrementalHistoryAndUses.</summary>
    [Fact]
    public void FailedRecordingRestoresIncrementalHistoryAndUses()
    {
        var validation = new GraphicsValidation();
        object command = new(), resource = new(), temporary = new();
        validation.Access(command, resource, _copyWrite);
        using (validation.BeginRecording(command))
        {
            validation.Barrier(command, resource, _copyWrite, _copyRead);
            validation.Access(command, temporary, _copyRead);
        }

        validation.Release(temporary);
        Assert.Throws<InvalidOperationException>(() => validation.Access(command, resource, _copyRead));
        validation.Barrier(command, resource, _copyWrite, _copyRead);
        validation.Access(command, resource, _copyRead);
    }

    /// <summary>Checks RecordingDoesNotEnumerateThePrecedingTape.</summary>
    [Fact]
    public void RecordingDoesNotEnumerateThePrecedingTape()
    {
        var validation = new GraphicsValidation();
        object command = new();
        validation.Access(command, new object(), _copyRead);
        var commands = (System.Collections.IDictionary)typeof(GraphicsValidation).GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(validation)!;
        var tape = (System.Collections.IList)commands[command]!;

        // Poison a historical entry: incremental recording must never inspect it.
        tape[0] = null;
        using GraphicsValidation.RecordingTransaction transaction = validation.BeginRecording(command);
        validation.Access(command, new object(), _copyRead);
        transaction.Commit();
        Assert.Equal(2, tape.Count);
    }

    /// <summary>Checks ReleasedWrapperIsCollectibleWhileDeviceRemainsAlive.</summary>
    [Fact]
    public void ReleasedWrapperIsCollectibleWhileDeviceRemainsAlive()
    {
        using var device = new ValidationDevice(new RawBackend());
        WeakReference weak = CreateReleasedWrapper(device);
        for (int attempt = 0; attempt < 3 && weak.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(weak.IsAlive);
        GC.KeepAlive(device);
    }

    /// <summary>Reads initial metadata once while recording, and once again during submission replay.</summary>
    [Fact]
    public void MetadataRecordingIsIncrementalAndSubmissionStillReplays()
    {
        var validation = new GraphicsValidation();
        var source = new RecordingMetadataSource();
        using var command = new PresentationCommand();
        for (int index = 0; index < 256; index++)
        {
            using GraphicsValidation.RecordingTransaction transaction = validation.BeginRecording(command);
            validation.Metadata(command, source, source.Value, false);
            transaction.Commit();
        }

        Assert.Equal(1, source.Reads);
        validation.PrepareSubmission(new[] { command });
        Assert.Equal(2, source.Reads);
        source.Value = new ShaderDependencyMetadata([]);
        Assert.Throws<InvalidOperationException>(() => validation.PrepareSubmission(new[] { command }));
    }

    /// <summary>Restores metadata publications and texture buckets after failed native recording.</summary>
    [Fact]
    public void RollbackRestoresMetadataAndTextureHistory()
    {
        var validation = new GraphicsValidation();
        var source = new RecordingMetadataSource();
        IShaderReferenceMetadata original = source.Value;
        using var image = new PresentationTexture();
        using var command = new PresentationCommand();
        validation.Metadata(command, source, original, false);
        validation.TextureAccess(command, image, _range, TextureState.CopyDestination, _copyWrite);
        using (validation.BeginRecording(command))
        {
            validation.Metadata(command, source, new ShaderDependencyMetadata([]), true);
            validation.TextureBarrier(command, new() { Texture = image, Range = _range, BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = _copyWrite, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
        }

        validation.Metadata(command, source, original, false);
        Assert.Throws<InvalidOperationException>(() => validation.TextureAccess(command, image, _range, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite)));
        validation.TextureBarrier(command, new() { Texture = image, Range = _range, BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = _copyWrite, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
    }

    /// <summary>Failed preflight and partial native issuance never publish usable acquisition readiness.</summary>
    [Fact]
    public void FailedSubmissionDoesNotGrantAcquisitionReadiness()
    {
        var validation = new GraphicsValidation();
        using var image = new PresentationTexture();
        using var acquire = new PresentationSemaphore();
        using var command = new PresentationCommand();
        object acquisition = new();
        validation.SetTextureState(image, TextureState.CopySource);
        validation.PresentationSignaled(acquisition, acquire);
        validation.AcquireTexture(acquisition, image, acquire);
        validation.TextureAccess(command, image, _range, TextureState.CopySource, _copyRead);
        var wrongStage = new QueueSubmitDesc { CommandBuffers = [command], WaitSemaphores = [new() { Semaphore = acquire, Stages = PipelineStage.FragmentShader }] };
        Assert.Throws<InvalidOperationException>(() => validation.PrepareSubmission(wrongStage));
        Assert.Throws<InvalidOperationException>(() => validation.PrepareSubmission(new[] { command }));
        var valid = new QueueSubmitDesc { CommandBuffers = [command], WaitSemaphores = [new() { Semaphore = acquire, Stages = PipelineStage.Copy }] };
        validation.PrepareSubmission(valid);
        validation.MarkPartialSubmissionFailure();
        Assert.Throws<InvalidOperationException>(() => validation.PrepareSubmission(valid));
        Assert.Throws<InvalidOperationException>(() => validation.Release(acquire));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateReleasedWrapper(ValidationDevice device)
    {
        IGraphicsBuffer<int> buffer = device.CreateBuffer(new BufferDesc<int> { Count = 4, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        var weak = new WeakReference(buffer);
        buffer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => buffer.SliceElements(0, 1));
        return weak;
    }
}
