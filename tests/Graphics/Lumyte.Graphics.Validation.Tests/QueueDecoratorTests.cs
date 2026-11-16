using System.Collections;
using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Validation.Tests;

/// <summary>Exercises actual queue wrappers at a deterministic native boundary.</summary>
public sealed class QueueDecoratorTests
{
    /// <summary>Rejects missing backends and nested validation layers.</summary>
    [Fact]
    public void ConstructorRejectsNullAndNestedLayers()
    {
        Assert.Throws<ArgumentNullException>(() => new ValidationDevice(null!));
        using var layer = new ValidationDevice(new QueueBackend());
        Assert.Throws<ArgumentException>(() => new ValidationDevice(layer));
    }

    /// <summary>Disposal releases subscriptions, never the borrowed device.</summary>
    [Fact]
    public void DeviceIsBorrowedAndSubscriptionIsRemoved()
    {
        var backend = new QueueBackend();
        var layer = new ValidationDevice(backend);
        Assert.True(layer.ValidationEnabled);
        Assert.Equal(1, backend.Subscribers);
        layer.Dispose();
        layer.Dispose();
        Assert.Equal(0, backend.DisposeCount);
        Assert.Equal(0, backend.Subscribers);
        Assert.Throws<ObjectDisposedException>(() => layer.CreateSemaphore());
    }

    /// <summary>Events identify the decorator and late status remains observable.</summary>
    [Fact]
    public void LossEventUsesWrapperIdentity()
    {
        var backend = new QueueBackend();
        var layer = new ValidationDevice(backend);
        object? sender = null;
        GraphicsDeviceLoss? loss = null;
        layer.DeviceLost += (value, args) =>
        {
            sender = value;
            loss = args.Loss;
        };
        backend.Lose();
        Assert.Same(layer, sender);
        Assert.Same(backend.Loss, loss);
        layer.Dispose();
        Assert.Equal(GraphicsDeviceStatus.Lost, layer.Status);
        Assert.Same(loss, layer.Loss);
    }

    /// <summary>Live children prevent disposal and owned children are released once.</summary>
    [Fact]
    public void ChildDisposalIsOwnedAndRegistryIsBounded()
    {
        var raw = new PresentationSemaphore();
        var backend = new QueueBackend { ReusedSemaphore = raw };
        using var layer = new ValidationDevice(backend);
        IGraphicsSemaphore child = layer.CreateSemaphore();
        Assert.Throws<InvalidOperationException>(layer.Dispose);
        child.Dispose();
        child.Dispose();
        Assert.Equal(1, raw.DisposeCount);
        FieldInfo field = Assert.IsType<FieldInfo>(typeof(ValidationDevice).GetField("_objects", BindingFlags.NonPublic | BindingFlags.Instance), exactMatch: false);
        Assert.Empty(Assert.IsAssignableFrom<IDictionary>(field.GetValue(layer)));
    }

    /// <summary>A faulty factory cannot dispose the already wrapped native child.</summary>
    [Fact]
    public void DuplicateFactoryResultDoesNotDestroyExistingChild()
    {
        var raw = new PresentationSemaphore();
        using var layer = new ValidationDevice(new QueueBackend { ReusedSemaphore = raw });
        using IGraphicsSemaphore child = layer.CreateSemaphore();
        Assert.Throws<InvalidOperationException>(() => layer.CreateSemaphore());
        Assert.Equal(0, raw.DisposeCount);
    }

    /// <summary>Reentrant domain disposal cleans the just-created native child.</summary>
    [Fact]
    public void ReentrantFactoryRegistrationFailureDisposesNativeChild()
    {
        var raw = new PresentationSemaphore();
        var backend = new QueueBackend { ReusedSemaphore = raw };
        var layer = new ValidationDevice(backend);
        backend.DuringCreate = layer.Dispose;
        Assert.Throws<ObjectDisposedException>(() => layer.CreateSemaphore());
        Assert.Equal(1, raw.DisposeCount);
        Assert.Equal(0, backend.DisposeCount);
    }

    /// <summary>Preflight rejects recording commands without issuing native work and allows repair.</summary>
    [Fact]
    public void FailedPreflightCanBeRetriedAfterFinish()
    {
        var backend = new QueueBackend();
        using var layer = new ValidationDevice(backend);
        using IGraphicsCommandBuffer command = layer.CreateCommandBuffer(new());
        Assert.Throws<InvalidOperationException>(() => layer.Queue.Submit([command]));
        Assert.Equal(0, backend.NativeQueue.SubmitCount);
        command.Finish();
        using IGraphicsSubmission submission = layer.Queue.Submit([command]);
        Assert.IsType<PresentationCommand>(Assert.Single(backend.NativeQueue.Last!.CommandBuffers));
        backend.NativeQueue.Submission.Complete();
        Assert.Equal(SubmissionStatus.Completed, submission.Status);
    }

    /// <summary>Raw and foreign commands never cross a validation domain.</summary>
    [Fact]
    public void RawAndForeignCommandsAreRejectedBeforeNativeSubmit()
    {
        var backend = new QueueBackend();
        using var layer = new ValidationDevice(backend);
        using var other = new ValidationDevice(new QueueBackend());
        using IGraphicsCommandBuffer foreign = other.CreateCommandBuffer(new());
        using var raw = new PresentationCommand();
        foreign.Finish();
        raw.Finish();
        Assert.Throws<ArgumentException>(() => layer.Queue.Submit([foreign]));
        Assert.Throws<ArgumentException>(() => layer.Queue.Submit([raw]));
        Assert.Equal(0, backend.NativeQueue.SubmitCount);
    }

    /// <summary>Unsignaled waits fail transactionally before native issuance.</summary>
    [Fact]
    public void UnsignaledWaitDoesNotConsumeCommand()
    {
        var backend = new QueueBackend();
        using var layer = new ValidationDevice(backend);
        using IGraphicsSemaphore semaphore = layer.CreateSemaphore();
        using IGraphicsCommandBuffer command = layer.CreateCommandBuffer(new());
        command.Finish();
        Assert.Throws<InvalidOperationException>(() => layer.Queue.Submit(new QueueSubmitDesc { CommandBuffers = [command], WaitSemaphores = [new() { Semaphore = semaphore, Stages = PipelineStage.Copy }] }));
        Assert.Equal(0, backend.NativeQueue.SubmitCount);
        using IGraphicsSubmission submission = layer.Queue.Submit([command]);
        backend.NativeQueue.Submission.Complete();
        Assert.Equal(SubmissionStatus.Completed, submission.Status);
    }

    /// <summary>Cancellation never retires command memory or signal semaphore use.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task CanceledWaitRetainsResourcesUntilActualCompletionAsync()
    {
        var backend = new QueueBackend();
        using var layer = new ValidationDevice(backend);
        using IGraphicsSemaphore semaphore = layer.CreateSemaphore();
        using IGraphicsCommandBuffer command = layer.CreateCommandBuffer(new());
        command.Finish();
        using IGraphicsSubmission submission = layer.Queue.Submit(new QueueSubmitDesc { CommandBuffers = [command], SignalSemaphores = [semaphore] });
        Assert.IsType<PresentationSemaphore>(Assert.Single(backend.NativeQueue.Last!.SignalSemaphores));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => submission.WaitAsync(cancel.Token).AsTask());
        Assert.Equal(SubmissionStatus.Pending, submission.Status);
        Assert.Throws<InvalidOperationException>(submission.Dispose);
        Assert.Throws<InvalidOperationException>(command.Dispose);
        Assert.Throws<InvalidOperationException>(semaphore.Dispose);
        backend.NativeQueue.Submission.Complete();
        await submission.WaitAsync();
        Assert.Equal(SubmissionStatus.Completed, submission.Status);
    }

    /// <summary>Opaque submission failure retains resources until explicit backend loss retirement.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task NativeFailurePreservesExceptionAndQuarantinesUntilRetirementAsync()
    {
        var failure = new InvalidOperationException("native issue");
        var backend = new QueueBackend();
        backend.NativeQueue.Failure = failure;
        using var layer = new ValidationDevice(backend);
        using IGraphicsSemaphore semaphore = layer.CreateSemaphore();
        using IGraphicsCommandBuffer command = layer.CreateCommandBuffer(new());
        command.Finish();
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => layer.Queue.Submit(new QueueSubmitDesc { CommandBuffers = [command], SignalSemaphores = [semaphore] })));
        Assert.Throws<InvalidOperationException>(command.Dispose);
        Assert.Throws<InvalidOperationException>(semaphore.Dispose);
        await layer.WaitForRetirementAsync();
        Assert.Throws<InvalidOperationException>(command.Dispose);
        backend.Lose();
        await layer.WaitForRetirementAsync();
        command.Dispose();
        semaphore.Dispose();
    }
}
