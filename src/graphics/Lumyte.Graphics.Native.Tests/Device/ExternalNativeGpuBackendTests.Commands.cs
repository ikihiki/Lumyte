namespace Lumyte.Graphics.Native.Tests.Device;

public sealed partial class ExternalNativeGpuBackendTests
{
    [Fact]
    public void ConsumerPassesTransferSubresourcesAndDependenciesToExternalRecording()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        var description = new NativeGpuTextureDescription(
            NativeGpuTextureDimension.TwoD, 64, 32, 1, 4, 6, 1, GpuFormat.Rgba8Unorm,
            NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination);
        NativeGpuMemoryRequirements linearRequirements = backend.GetLinearMemoryRequirements(8192, NativeGpuMemoryKind.GpuOnly);
        NativeGpuMemoryRequirements textureRequirements = backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
        ulong stride = Math.Max(linearRequirements.Size, textureRequirements.Size);
        NativeGpuHeap heap = backend.CreateGpuHeap(stride * 3, textureRequirements.Alignment, NativeGpuMemoryKind.GpuOnly,
            [linearRequirements.Compatibility, textureRequirements.Compatibility]);
        NativeGpuLinearRegion upload = backend.CreateLinearRegion(8192, heap, 0);
        NativeGpuLinearRegion readback = backend.CreateLinearRegion(8192, heap, stride);
        NativeGpuTextureHandle texture = backend.CreateTexture(description, heap, stride * 2);
        var source = new NativeGpuRange(upload, 128, 2048);
        var destination = new NativeGpuRange(readback, 192, 4096);
        var footprint = new NativeGpuTextureCopyFootprint(
            1, NativeGpuTextureAspect.Color, 2, 2, new(3, 4, 0), new(8, 4, 1), 256, 1024);
        var view = new NativeGpuTextureView(texture, NativeGpuTextureViewDimension.TwoDArray,
            GpuFormat.Rgba8Unorm, NativeGpuTextureAspect.Color, 1, 1, 2, 2);

        try
        {
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.TextureTransition(view, GpuTextureLayout.Undefined, GpuTextureLayout.CopyDestination);
            commands.CopyMemoryToTexture(source, texture, footprint);
            commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Copy, GpuAccess.CopyRead);
            commands.TextureTransition(view, GpuTextureLayout.CopyDestination, GpuTextureLayout.CopySource);
            commands.CopyTextureToMemory(texture, destination, footprint);
            commands.CopyMemory(source, destination);
            commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);
            commands.DiscardTexture(view, GpuTextureLayout.CopyDestination);

            Assert.Collection(observed,
                value => Assert.Equal(new TextureLayoutChange(view, GpuTextureLayout.Undefined, GpuTextureLayout.CopyDestination),
                    Assert.IsType<TextureLayoutChange>(value)),
                value => Assert.Equal(new UploadTexture(source, texture, footprint), Assert.IsType<UploadTexture>(value)),
                value => Assert.Equal(new GlobalDependency(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Copy, GpuAccess.CopyRead),
                    Assert.IsType<GlobalDependency>(value)),
                value => Assert.Equal(new TextureLayoutChange(view, GpuTextureLayout.CopyDestination, GpuTextureLayout.CopySource),
                    Assert.IsType<TextureLayoutChange>(value)),
                value => Assert.Equal(new ReadbackTexture(texture, destination, footprint), Assert.IsType<ReadbackTexture>(value)),
                value => Assert.Equal(new MemoryCopy(source, destination), Assert.IsType<MemoryCopy>(value)),
                value => Assert.Equal(new GlobalDependency(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead),
                    Assert.IsType<GlobalDependency>(value)),
                value => Assert.Equal(new TextureDiscard(view, GpuTextureLayout.CopyDestination), Assert.IsType<TextureDiscard>(value)));
        }
        finally
        {
            backend.DestroyTexture(texture);
            backend.DestroyLinearRegion(readback);
            backend.DestroyLinearRegion(upload);
            backend.DestroyGpuHeap(heap);
        }
    }

    [Fact]
    public void ConsumerSubmitsOrderedRecordingsWithFullWidthCompletionValues()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        NativeGpuQueue queue = backend.MainQueue;
        const ulong initialValue = 1ul << 40;
        const ulong completionValue = initialValue + 9;
        using NativeGpuSemaphore semaphore = queue.CreateSemaphore(initialValue);
        using NativeGpuCommandBuffer first = queue.StartCommandRecording();
        using NativeGpuCommandBuffer second = queue.StartCommandRecording();

        queue.Submit([second, first], semaphore, completionValue);
        _ = queue.IsComplete(semaphore, completionValue);
        queue.Wait(semaphore, completionValue);

        Assert.Collection(observed,
            value => Assert.Equal(new SemaphoreCreation(initialValue), Assert.IsType<SemaphoreCreation>(value)),
            value =>
            {
                QueueSubmission submission = Assert.IsType<QueueSubmission>(value);
                Assert.Equal((semaphore, completionValue), (submission.Semaphore, submission.Value));
                Assert.Collection(submission.Commands,
                    command => Assert.Same(second, command),
                    command => Assert.Same(first, command));
            },
            value => Assert.Equal(new CompletionQuery(semaphore, completionValue), Assert.IsType<CompletionQuery>(value)),
            value => Assert.Equal(new CompletionWait(semaphore, completionValue), Assert.IsType<CompletionWait>(value)));
    }

    private sealed record MemoryCopy(NativeGpuRange Source, NativeGpuRange Destination);
    private sealed record UploadTexture(NativeGpuRange Source, NativeGpuTextureHandle Destination, NativeGpuTextureCopyFootprint Footprint);
    private sealed record ReadbackTexture(NativeGpuTextureHandle Source, NativeGpuRange Destination, NativeGpuTextureCopyFootprint Footprint);
    private sealed record GlobalDependency(GpuStage BeforeStages, GpuAccess BeforeAccess, GpuStage AfterStages, GpuAccess AfterAccess);
    private sealed record TextureLayoutChange(NativeGpuTextureView View, GpuTextureLayout Before, GpuTextureLayout After);
    private sealed record TextureDiscard(NativeGpuTextureView View, GpuTextureLayout After);
    private sealed record SemaphoreCreation(ulong InitialValue);
    private sealed record QueueSubmission(NativeGpuCommandBuffer[] Commands, NativeGpuSemaphore Semaphore, ulong Value);
    private sealed record CompletionQuery(NativeGpuSemaphore Semaphore, ulong Value);
    private sealed record CompletionWait(NativeGpuSemaphore Semaphore, ulong Value);

    // This spy verifies the public implementation boundary without modelling GPU execution,
    // one-shot validation, or retirement; those behaviors belong to backend tests.
    private sealed class ExternalQueue(Action<object>? observe) : NativeGpuQueue
    {
        public override NativeGpuCommandBuffer StartCommandRecording() => new ExternalCommands(observe);

        public override void Submit(ReadOnlySpan<NativeGpuCommandBuffer> commands, NativeGpuSemaphore semaphore, ulong value)
            => observe?.Invoke(new QueueSubmission(commands.ToArray(), semaphore, value));

        public override NativeGpuSemaphore CreateSemaphore(ulong initialValue)
        {
            observe?.Invoke(new SemaphoreCreation(initialValue));
            return new ExternalSemaphore();
        }

        public override bool IsComplete(NativeGpuSemaphore semaphore, ulong value)
        {
            observe?.Invoke(new CompletionQuery(semaphore, value));
            return false;
        }

        public override void Wait(NativeGpuSemaphore semaphore, ulong value)
            => observe?.Invoke(new CompletionWait(semaphore, value));
    }

    private sealed class ExternalCommands(Action<object>? observe) : NativeGpuCommandBuffer
    {
        public override void CopyMemory(NativeGpuRange source, NativeGpuRange destination)
            => observe?.Invoke(new MemoryCopy(source, destination));

        public override void CopyMemoryToTexture(NativeGpuRange source, NativeGpuTextureHandle destination, NativeGpuTextureCopyFootprint footprint)
            => observe?.Invoke(new UploadTexture(source, destination, footprint));

        public override void CopyTextureToMemory(NativeGpuTextureHandle source, NativeGpuRange destination, NativeGpuTextureCopyFootprint footprint)
            => observe?.Invoke(new ReadbackTexture(source, destination, footprint));

        public override void Barrier(GpuStage beforeStages, GpuAccess beforeAccess, GpuStage afterStages, GpuAccess afterAccess)
            => observe?.Invoke(new GlobalDependency(beforeStages, beforeAccess, afterStages, afterAccess));

        public override void TextureTransition(NativeGpuTextureView view, GpuTextureLayout beforeLayout, GpuTextureLayout afterLayout)
            => observe?.Invoke(new TextureLayoutChange(view, beforeLayout, afterLayout));

        public override void DiscardTexture(NativeGpuTextureView view, GpuTextureLayout afterLayout)
            => observe?.Invoke(new TextureDiscard(view, afterLayout));

        public override void SetResourceDescriptorHeap(NativeGpuDescriptorHeap heap)
            => observe?.Invoke(new ResourceHeapSelection(heap));

        public override void SetSamplerDescriptorHeap(NativeGpuDescriptorHeap heap)
            => observe?.Invoke(new SamplerHeapSelection(heap));

        public override void Dispose() { }
    }

    private sealed class ExternalSemaphore : NativeGpuSemaphore
    {
        public override void Dispose() { }
    }
}
