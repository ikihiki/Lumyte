using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
public sealed unsafe partial class VulkanNativeCommandsTests
{
    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void AddressCopyUsesRegionRelativeOffsetsAndSourceSize()
    {
        using var resources = new Resources();
        var upload = resources.Linear(256, NativeGpuMemoryKind.CpuVisible);
        var gpu = resources.Linear(256, NativeGpuMemoryKind.GpuOnly);
        var readback = resources.Linear(256, NativeGpuMemoryKind.Readback);
        byte[] expected = Pattern(64, 31);
        expected.CopyTo(Bytes(upload)[16..]);
        Bytes(readback).Fill(0xCB);
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        commands.CopyMemory(new(upload, 16, 64), new(gpu, 32, 128));
        CopyDependency(commands);
        commands.CopyMemory(new(gpu, 32, 64), new(readback, 48, 128));
        HostDependency(commands);

        queue.Submit([commands], completion, 1);
        commands.Dispose();
        queue.Wait(completion, 1);

        byte[] actual = Bytes(readback).ToArray();
        byte[] wholeExpected = Enumerable.Repeat((byte)0xCB, 256).ToArray();
        expected.CopyTo(wholeExpected, 48);
        Assert.Equal(wholeExpected, actual);
    }

    [VulkanNativeTheory]
    [Trait("Category", "VulkanNativeConformance")]
    [InlineData(false)]
    [InlineData(true)]
    public void PrerecordedTextureUsesInitializeOnlyOnce(bool separateSubmissions)
    {
        using var resources = new Resources();
        var upload = resources.Linear(4096, NativeGpuMemoryKind.CpuVisible);
        var readback = resources.Linear(4096, NativeGpuMemoryKind.Readback);
        var texture = resources.Texture(TextureDescription());
        byte[] expected = Pattern(256, 83);
        expected.CopyTo(Bytes(upload));
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var write = queue.StartCommandRecording();
        using var read = queue.StartCommandRecording();
        write.CopyMemoryToTexture(new(upload, 0, 256), texture, Footprint());
        CopyDependency(read);
        read.CopyTextureToMemory(texture, new(readback, 0, 256), Footprint());
        HostDependency(read);

        if (separateSubmissions)
        {
            queue.Submit([write], completion, 1);
            queue.Submit([read], completion, 2);
        }
        else { queue.Submit([write, read], completion, 2); }
        queue.Wait(completion, 2);

        Assert.Equal(expected, Bytes(readback)[..256].ToArray());
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void UnsubmittedDisposalLeavesFreshTextureUsable()
    {
        using var resources = new Resources();
        var upload = resources.Linear(4096, NativeGpuMemoryKind.CpuVisible);
        var readback = resources.Linear(4096, NativeGpuMemoryKind.Readback);
        var texture = resources.Texture(TextureDescription());
        byte[] expected = Pattern(256, 13);
        expected.CopyTo(Bytes(upload));
        var queue = resources.Backend.MainQueue;
        using (var discarded = queue.StartCommandRecording())
        {
            discarded.CopyMemoryToTexture(new(upload, 0, 256), texture, Footprint());
        }
        using var completion = queue.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        commands.CopyMemoryToTexture(new(upload, 0, 256), texture, Footprint());
        CopyDependency(commands);
        commands.CopyTextureToMemory(texture, new(readback, 0, 256), Footprint());
        HostDependency(commands);

        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);

        Assert.Equal(expected, Bytes(readback)[..256].ToArray());
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void RejectedBatchLeavesTextureInitializationForLaterUse()
    {
        using var resources = new Resources();
        var upload = resources.Linear(4096, NativeGpuMemoryKind.CpuVisible);
        var readback = resources.Linear(4096, NativeGpuMemoryKind.Readback);
        var texture = resources.Texture(TextureDescription());
        byte[] expected = Pattern(256, 61);
        expected.CopyTo(Bytes(upload));
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using (var abandoned = queue.StartCommandRecording())
        using (var foreign = new ForeignCommands())
        {
            abandoned.CopyMemoryToTexture(new(upload, 0, 256), texture, Footprint());
            Assert.Throws<ArgumentException>(() => queue.Submit([abandoned, foreign], completion, 1));
        }
        using var retry = queue.StartCommandRecording();
        retry.CopyMemoryToTexture(new(upload, 0, 256), texture, Footprint());
        CopyDependency(retry);
        retry.CopyTextureToMemory(texture, new(readback, 0, 256), Footprint());
        HostDependency(retry);

        queue.Submit([retry], completion, 1);
        queue.Wait(completion, 1);

        Assert.Equal(expected, Bytes(readback)[..256].ToArray());
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void CompletedCallerSemaphoreCanBeDisposedBeforeLaterQueueOperations()
    {
        using var resources = new Resources();
        var queue = resources.Backend.MainQueue;
        using (var completion = queue.CreateSemaphore(0))
        using (var commands = queue.StartCommandRecording())
        {
            queue.Submit([commands], completion, 1);
            queue.Wait(completion, 1);
        }
        using var nextCompletion = queue.CreateSemaphore(7);
        using var next = queue.StartCommandRecording();

        queue.Submit([next], nextCompletion, 8);
        queue.Wait(nextCompletion, 8);

        Assert.True(queue.IsComplete(nextCompletion, 8));
    }

    [VulkanNativeTheory]
    [Trait("Category", "VulkanNativeConformance")]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordingCannotBeSubmittedTwice(bool waitBeforeRetry)
    {
        using var resources = new Resources();
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        queue.Submit([commands], completion, 1);
        if (waitBeforeRetry) { queue.Wait(completion, 1); }

        Exception? error = Record.Exception(() => queue.Submit([commands], completion, 2));
        queue.Wait(completion, 1);

        Assert.IsType<InvalidOperationException>(error);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void EmptySubmissionIsRejected()
    {
        using var resources = new Resources();
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);

        var error = Assert.Throws<ArgumentException>(() => queue.Submit([], completion, 1));

        Assert.Equal("commands", error.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DuplicateRecordingIsRejectedWithoutSubmittingTheBatch()
    {
        using var resources = new Resources();
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();

        var error = Assert.Throws<ArgumentException>(() => queue.Submit([commands, commands], completion, 1));

        Assert.Equal("commands", error.ParamName);
        Assert.False(queue.IsComplete(completion, 1));
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void NewAliasInitializesAfterCallerDependencyAndCanSwitchBack()
    {
        using var resources = new Resources();
        var upload = resources.Linear(4096, NativeGpuMemoryKind.CpuVisible);
        var readback = resources.Linear(4096, NativeGpuMemoryKind.Readback);
        byte[] first = Pattern(256, 47);
        byte[] second = Pattern(256, 167);
        first.CopyTo(Bytes(upload));
        second.CopyTo(Bytes(upload)[256..]);
        var description = TextureDescription();
        var requirements = resources.Backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
        var heap = resources.Heap(requirements, NativeGpuMemoryKind.GpuOnly);
        var a = resources.Texture(description, heap);
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var useA = queue.StartCommandRecording();
        useA.CopyMemoryToTexture(new(upload, 0, 256), a, Footprint());
        queue.Submit([useA], completion, 1);

        // B is placed after A has been submitted and may still be executing.
        var b = resources.Texture(description, heap);
        using var reuse = queue.StartCommandRecording();
        AliasDependency(reuse);
        reuse.DiscardTexture(View(b), GpuTextureLayout.General);
        reuse.CopyMemoryToTexture(new(upload, 256, 256), b, Footprint());
        CopyDependency(reuse);
        reuse.CopyTextureToMemory(b, new(readback, 0, 256), Footprint());
        AliasDependency(reuse);
        reuse.DiscardTexture(View(a), GpuTextureLayout.General);
        reuse.CopyMemoryToTexture(new(upload, 0, 256), a, Footprint());
        CopyDependency(reuse);
        reuse.CopyTextureToMemory(a, new(readback, 256, 256), Footprint());
        HostDependency(reuse);

        queue.Submit([reuse], completion, 2);
        queue.Wait(completion, 2);

        Assert.Equal(second.Concat(first).ToArray(), Bytes(readback)[..512].ToArray());
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DepthAndStencilTransfersPreserveTheOtherAspect()
    {
        using var resources = new Resources();
        var upload = resources.Linear(4096, NativeGpuMemoryKind.CpuVisible);
        var readback = resources.Linear(4096, NativeGpuMemoryKind.Readback);
        var texture = resources.Texture(TextureDescription() with { Format = GpuFormat.Depth24PlusStencil8 });
        // D24 uses the low 24 bits of a 32-bit element; stencil is a separate byte element.
        for (int index = 0; index < 64; index++)
        {
            uint depth = (uint)(index * 65537);
            BitConverter.TryWriteBytes(Bytes(upload).Slice(index * 4, 4), depth);
            Bytes(upload)[512 + index] = (byte)(index + 91);
        }
        var depthFootprint = Footprint() with { Aspect = NativeGpuTextureAspect.Depth };
        var stencilFootprint = Footprint() with { Aspect = NativeGpuTextureAspect.Stencil, RowPitch = 8, ImagePitch = 64 };
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        commands.CopyMemoryToTexture(new(upload, 0, 256), texture, depthFootprint);
        CopyDependency(commands);
        commands.CopyMemoryToTexture(new(upload, 512, 64), texture, stencilFootprint);
        CopyDependency(commands);
        commands.CopyTextureToMemory(texture, new(readback, 0, 256), depthFootprint);
        commands.CopyTextureToMemory(texture, new(readback, 512, 64), stencilFootprint);
        HostDependency(commands);

        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);

        Assert.Equal(Bytes(upload)[512..576].ToArray(), Bytes(readback)[512..576].ToArray());
        Assert.Equal(Enumerable.Range(0, 64).Select(index => (uint)(index * 65537)).ToArray(),
            Enumerable.Range(0, 64).Select(index => BitConverter.ToUInt32(Bytes(readback).Slice(index * 4, 4)) & 0xFFFFFF).ToArray());
    }

    [VulkanNativeTheory]
    [Trait("Category", "VulkanNativeConformance")]
    [InlineData(NativeGpuTextureDimension.TwoD, 1U, 2U)]
    [InlineData(NativeGpuTextureDimension.ThreeD, 2U, 1U)]
    public void TextureCopyHonorsPaddedRowsAndImages(NativeGpuTextureDimension dimension, uint depth, uint layers)
    {
        using var resources = new Resources();
        var upload = resources.Linear(4096, NativeGpuMemoryKind.CpuVisible);
        var readback = resources.Linear(4096, NativeGpuMemoryKind.Readback);
        var texture = resources.Texture(TextureDescription() with { Dimension = dimension, Depth = depth, LayerCount = layers });
        byte[] expected = Pattern(768, 123);
        expected.CopyTo(Bytes(upload)[256..]);
        var footprint = Footprint() with { LayerCount = layers, Extent = new(8, 8, depth), RowPitch = 40, ImagePitch = 400 };
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        commands.CopyMemoryToTexture(new(upload, 256, 768), texture, footprint);
        CopyDependency(commands);
        commands.CopyTextureToMemory(texture, new(readback, 128, 768), footprint);
        HostDependency(commands);

        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);

        byte[] expectedTexels = Enumerable.Range(0, 2).SelectMany(image => Enumerable.Range(0, 8)
            .SelectMany(row => expected.Skip(image * 400 + row * 40).Take(32))).ToArray();
        byte[] actualTexels = Enumerable.Range(0, 2).SelectMany(image => Enumerable.Range(0, 8)
            .SelectMany(row => Bytes(readback).Slice(128 + image * 400 + row * 40, 32).ToArray())).ToArray();
        Assert.Equal(expectedTexels, actualTexels);
    }

    private static void CopyDependency(NativeGpuCommandBuffer commands)
        => commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Copy, GpuAccess.CopyRead | GpuAccess.CopyWrite);
    private static void HostDependency(NativeGpuCommandBuffer commands)
        => commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);
    private static void AliasDependency(NativeGpuCommandBuffer commands)
        => commands.Barrier(GpuStage.All, GpuAccess.CopyWrite | GpuAccess.CopyRead, GpuStage.All, GpuAccess.CopyWrite | GpuAccess.CopyRead);
    private static NativeGpuTextureDescription TextureDescription() => new(NativeGpuTextureDimension.TwoD,
        8, 8, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination);
    private static NativeGpuTextureCopyFootprint Footprint() => new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(8, 8, 1), 32, 256);
    private static NativeGpuTextureView View(NativeGpuTextureHandle texture) => new(texture,
        NativeGpuTextureViewDimension.TwoD, GpuFormat.Rgba8Unorm, NativeGpuTextureAspect.Color, 0, 1, 0, 1);
    private static Span<byte> Bytes(NativeGpuLinearRegion region) => new((void*)region.CpuAddress, checked((int)region.Size));
    private static byte[] Pattern(int count, int seed) => Enumerable.Range(0, count).Select(index => (byte)(index * 37 + seed)).ToArray();

    private sealed class Resources : IDisposable
    {
        private readonly List<NativeGpuHeap> heaps = [];
        private readonly List<NativeGpuLinearRegion> regions = [];
        private readonly List<NativeGpuTextureHandle> textures = [];
        public VulkanBackend Backend { get; } = VulkanBackend.Create();

        public NativeGpuHeap Heap(NativeGpuMemoryRequirements requirements, NativeGpuMemoryKind kind)
        {
            var heap = Backend.CreateGpuHeap(requirements.Size, requirements.Alignment, kind, [requirements.Compatibility]);
            heaps.Add(heap);
            return heap;
        }

        public NativeGpuLinearRegion Linear(ulong size, NativeGpuMemoryKind kind)
        {
            var requirements = Backend.GetLinearMemoryRequirements(size, kind);
            var region = Backend.CreateLinearRegion(size, Heap(requirements, kind), 0);
            regions.Add(region);
            return region;
        }

        public NativeGpuTextureHandle Texture(NativeGpuTextureDescription description, NativeGpuHeap? heap = null)
        {
            heap ??= Heap(Backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly), NativeGpuMemoryKind.GpuOnly);
            var texture = Backend.CreateTexture(description, heap, 0);
            textures.Add(texture);
            return texture;
        }

        public void Dispose()
        {
            foreach (var texture in textures) { Backend.DestroyTexture(texture); }
            foreach (var region in regions) { Backend.DestroyLinearRegion(region); }
            foreach (var heap in heaps) { Backend.DestroyGpuHeap(heap); }
            Backend.Dispose();
        }
    }
}
