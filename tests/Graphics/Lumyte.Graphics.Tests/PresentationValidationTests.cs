using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Tests optional presentation diagnostics without unsafe native misuse.</summary>
public sealed class PresentationValidationTests
{
    /// <summary>Checks the surface, swapchain, frame and borrowed-image ownership chain.</summary>
    /// <param name="enabled">Whether managed diagnostics are explicitly enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PresentationParentsAndImageUsesAreOptIn(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        object device = new(), surface = new(), swapchain = new(), frame = new(), image = new(), command = new();
        validation?.Register(surface, device);
        validation?.Register(swapchain, surface);
        validation?.Register(frame, swapchain);
        validation?.Register(image, frame);
        validation?.Use(command, image);
        if (enabled)
        {
            Assert.Throws<InvalidOperationException>(() => validation!.Release(surface));
            Assert.Throws<InvalidOperationException>(() => validation!.Release(swapchain));
            Assert.Throws<InvalidOperationException>(() => validation!.Release(frame));
            Assert.Throws<InvalidOperationException>(() => validation!.Release(image));
        }

        validation?.Complete(command);
        validation?.Release(image);
        validation?.Release(frame);
        validation?.Release(swapchain);
        validation?.Release(surface);
        validation?.Release(device);
    }

    /// <summary>Checks acquire signals and present waits share queue binary-semaphore state.</summary>
    /// <param name="enabled">Whether managed diagnostics are explicitly enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcquireAndPresentShareBinarySemaphoreState(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var available = new FakeSemaphore();
        using var rendered = new FakeSemaphore();
        object acquire = new(), submission = new(), present = new();
        validation?.PresentationSignalValidate(available);
        validation?.PresentationSignaled(acquire, available);
        var submit = new QueueSubmitDesc
        {
            CommandBuffers = [],
            WaitSemaphores = [new() { Semaphore = available, Stages = PipelineStage.ColorOutput }],
            SignalSemaphores = [rendered],
        };
        validation?.ValidateSemaphores(submit);
        validation?.SubmittedSemaphores(submission, submit);
        validation?.PresentationWaitValidate([rendered]);
        validation?.PresentationWaited(present, [rendered]);
        if (enabled)
        {
            Assert.Throws<InvalidOperationException>(() => validation!.PresentationWaitValidate([rendered]));
            Assert.Throws<InvalidOperationException>(() => validation!.PresentationSignalValidate(available));
            Assert.Throws<InvalidOperationException>(() => validation!.Release(rendered));
        }

        validation?.CompleteSemaphores(acquire);
        validation?.CompleteSemaphores(submission);
        validation?.CompleteSemaphores(present);
        validation?.PresentationSignalValidate(available);
        validation?.PresentationSignalValidate(rendered);
        validation?.Release(available);
        validation?.Release(rendered);
    }

    /// <summary>Checks validation and cancelled waits cannot retire native semaphore uses.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CancelledReleaseWaitRetainsPresentationUsesAsync()
    {
        var validation = new GraphicsValidation();
        using var semaphore = new FakeSemaphore();
        object operation = new();
        validation.PresentationSignalValidate(semaphore);
        validation.PresentationSignaled(operation, semaphore);
        bool complete = false;
        var lifetime = new SurfaceFrameLifetime(() =>
        {
            if (complete)
            {
                validation.CompleteSemaphores(operation);
            }

            return complete;
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lifetime.WaitForReleaseAsync(cancellation.Token).AsTask());
        Assert.Throws<InvalidOperationException>(() => validation.Release(semaphore));
        complete = true;
        await lifetime.WaitForReleaseAsync();
        validation.PresentationWaitValidate([semaphore]);
        validation.PresentationWaited(operation, [semaphore]);
        validation.CompleteSemaphores(operation);
        validation.Release(semaphore);
    }

    /// <summary>Checks failed native operations leave binary state unchanged.</summary>
    [Fact]
    public void PreflightDoesNotPublishNativeSuccess()
    {
        var validation = new GraphicsValidation();
        using var semaphore = new FakeSemaphore();
        validation.PresentationSignalValidate(semaphore);
        validation.PresentationSignalValidate(semaphore);
        Assert.Throws<InvalidOperationException>(() => validation.PresentationWaitValidate([semaphore]));
        object acquire = new();
        validation.PresentationSignaled(acquire, semaphore);
        validation.PresentationWaitValidate([semaphore]);
        validation.PresentationWaitValidate([semaphore]);
        Assert.Throws<InvalidOperationException>(() => validation.PresentationWaitValidate([semaphore, semaphore]));
        validation.CompleteSemaphores(acquire);
        Assert.Throws<InvalidOperationException>(() => validation.PresentationSignalValidate(semaphore));
    }

    /// <summary>Checks synchronous acquisition completion preserves a consumable binary signal.</summary>
    [Fact]
    public void SynchronousAcquireCompletionDoesNotConsumeItsSignal()
    {
        var validation = new GraphicsValidation();
        using var available = new FakeSemaphore();
        object acquire = new(), submission = new();
        validation.PresentationSignalValidate(available);
        validation.PresentationSignaled(acquire, available);
        validation.CompleteSemaphores(acquire);
        Assert.Throws<InvalidOperationException>(() => validation.PresentationSignalValidate(available));

        var submit = new QueueSubmitDesc
        {
            CommandBuffers = [],
            WaitSemaphores = [new() { Semaphore = available, Stages = PipelineStage.ColorOutput }],
        };
        validation.ValidateSemaphores(submit);
        validation.SubmittedSemaphores(submission, submit);
        Assert.Throws<InvalidOperationException>(() => validation.Release(available));
        validation.CompleteSemaphores(submission);
        validation.PresentationSignalValidate(available);
        validation.Release(available);
    }

    /// <summary>Checks an asynchronous acquisition requires its own semaphore at first GPU use.</summary>
    [Fact]
    public void AcquiredImageRequiresItsMatchedWaitBeforeCommit()
    {
        var validation = new GraphicsValidation();
        using var texture = new FakeTexture();
        using var available = new FakeSemaphore();
        using var unrelated = new FakeSemaphore();
        IGraphicsCommandBuffer command = CreateCommandIdentity();
        object acquire = new(), unrelatedAcquire = new(), submission = new();
        validation.SetTextureState(texture, TextureState.Undefined);
        validation.AcquireTexture(acquire, texture, available);
        validation.PresentationSignaled(acquire, available);
        validation.PresentationSignaled(unrelatedAcquire, unrelated);
        RecordPresentTransition(validation, command, texture);
        var missing = new QueueSubmitDesc { CommandBuffers = [command] };
        Assert.Throws<InvalidOperationException>(() => validation.PrepareSubmission(missing));
        QueueSubmitDesc wrong = missing with
        {
            WaitSemaphores = [new() { Semaphore = unrelated, Stages = PipelineStage.ColorOutput }],
        };
        Assert.Throws<InvalidOperationException>(() => validation.PrepareSubmission(wrong));
        QueueSubmitDesc correct = missing with
        {
            WaitSemaphores = [new() { Semaphore = available, Stages = PipelineStage.ColorOutput }],
        };
        validation.ValidateSemaphores(correct);
        GraphicsValidation.SubmissionState prepared = validation.PrepareSubmission(correct);

        // Native submission may still fail: preparation must not retire readiness or consume signals.
        Assert.Throws<InvalidOperationException>(() => validation.PrepareSubmission(missing));
        validation.PresentationWaitValidate([available]);
        validation.CommitSubmission(prepared);
        validation.Submit(command);
        validation.SubmittedSemaphores(submission, correct);
        Assert.Throws<InvalidOperationException>(() => validation.PresentationWaitValidate([available]));
        validation.Complete(command);
        validation.CompleteSemaphores(submission);
        validation.CompleteSemaphores(acquire);
        validation.CompleteSemaphores(unrelatedAcquire);
    }

    /// <summary>Checks cancellation cannot make an asynchronous, semaphore-free image GPU-ready.</summary>
    /// <returns>The release observation.</returns>
    [Fact]
    public async Task CancelledAcquireCompletionRetainsImageReadinessAsync()
    {
        var validation = new GraphicsValidation();
        using var texture = new FakeTexture();
        IGraphicsCommandBuffer command = CreateCommandIdentity();
        object acquire = new();
        validation.SetTextureState(texture, TextureState.Undefined);
        validation.AcquireTexture(acquire, texture, null);
        RecordPresentTransition(validation, command, texture);
        var submit = new QueueSubmitDesc { CommandBuffers = [command] };
        bool nativeComplete = false;
        var lifetime = new SurfaceFrameLifetime(() =>
        {
            if (nativeComplete)
            {
                validation.CompleteSemaphores(acquire);
            }

            return nativeComplete;
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lifetime.WaitForReleaseAsync(cancellation.Token).AsTask());
        Assert.Throws<InvalidOperationException>(() => validation.PrepareSubmission(submit));
        nativeComplete = true;
        await lifetime.WaitForReleaseAsync();
        validation.CommitSubmission(validation.PrepareSubmission(submit));
    }

    /// <summary>Checks synchronous readiness retirement does not require a semaphore wait.</summary>
    /// <param name="signal">Whether acquisition supplies a logical binary signal.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedAcquireAllowsImageUseWithoutWait(bool signal)
    {
        var validation = new GraphicsValidation();
        using var texture = new FakeTexture();
        using var available = new FakeSemaphore();
        IGraphicsCommandBuffer command = CreateCommandIdentity();
        object acquire = new();
        validation.SetTextureState(texture, TextureState.Undefined);
        validation.AcquireTexture(acquire, texture, signal ? available : null);
        if (signal)
        {
            validation.PresentationSignaled(acquire, available);
        }

        validation.CompleteSemaphores(acquire);
        RecordPresentTransition(validation, command, texture);
        validation.CommitSubmission(validation.PrepareSubmission(new QueueSubmitDesc { CommandBuffers = [command] }));
        if (signal)
        {
            validation.PresentationWaitValidate([available]);
        }
    }

    /// <summary>Checks an unrelated signaled semaphore cannot authorize presenting an in-flight image.</summary>
    [Fact]
    public void PresentRequiresSemaphoreProducedByImageSubmission()
    {
        var validation = new GraphicsValidation();
        using var texture = new FakeTexture();
        using var rendered = new FakeSemaphore();
        using var unrelated = new FakeSemaphore();
        IGraphicsCommandBuffer command = CreateCommandIdentity();
        object submission = new(), unrelatedSubmission = new();
        validation.SetTextureState(texture, TextureState.Undefined);
        RecordPresentTransition(validation, command, texture);
        var submit = new QueueSubmitDesc { CommandBuffers = [command], SignalSemaphores = [rendered] };
        validation.CommitSubmission(validation.PrepareSubmission(submit));
        validation.Submit(command);
        validation.SubmittedSemaphores(submission, submit);
        validation.PresentationSignaled(unrelatedSubmission, unrelated);
        validation.PresentationWaitValidate([unrelated]);
        Assert.Throws<InvalidOperationException>(() => validation.ValidatePresentTexture(texture));
        Assert.Throws<InvalidOperationException>(() => validation.ValidatePresentTexture(texture, [unrelated]));
        validation.ValidatePresentTexture(texture, [rendered]);
        validation.Complete(command);
        validation.CompleteSemaphores(submission);
        validation.ValidatePresentTexture(texture);
    }

    private static void RecordPresentTransition(GraphicsValidation validation, IGraphicsCommandBuffer command, IGraphicsTexture texture)
        => validation.TextureBarrier(command, new()
        {
            Texture = texture,
            Range = new(0, 1, 0, 1),
            BeforeState = TextureState.Undefined,
            AfterState = TextureState.Present,
            Before = default,
            After = new(PipelineStage.ColorOutput, ResourceAccess.None),
        });

    private static IGraphicsCommandBuffer CreateCommandIdentity()
    {
        // Tracker-only identity: never invoke native methods or Dispose on this uninitialized wrapper.
        Type type = typeof(Lumyte.Graphics.Vulkan.VulkanDevice).Assembly.GetType("Lumyte.Graphics.Vulkan.VulkanCommandBuffer", throwOnError: true)!;
        return (IGraphicsCommandBuffer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
    }

    private sealed class FakeTexture : IGraphicsTexture
    {
        public uint Width => 1;

        public uint Height => 1;

        public uint MipLevels => 1;

        public uint ArrayLayers => 1;

        public TextureFormat Format => TextureFormat.Rgba8Unorm;

        public TextureUsage Usage => TextureUsage.RenderAttachment;

        public (uint Width, uint Height) GetMipSize(uint mipLevel) => mipLevel == 0 ? (1u, 1u) : throw new ArgumentOutOfRangeException(nameof(mipLevel));

        public IGraphicsTextureView CreateView(TextureViewDesc? desc = null) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class FakeSemaphore : IGraphicsSemaphore
    {
        public void Dispose()
        {
        }
    }
}
