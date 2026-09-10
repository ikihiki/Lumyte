using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
[Trait("Category", "DirectX12Conformance")]
public sealed class DirectX12NativeCommandTests
{
    [Fact]
    public void SubmittedBatchCopiesPlacedRegionRelativeRanges()
    {
        using DirectX12Backend backend = CreateBackend();
        using var upload = new Region(backend, NativeGpuMemoryKind.CpuVisible);
        using var gpu = new Region(backend, NativeGpuMemoryKind.GpuOnly);
        using var readback = new Region(backend, NativeGpuMemoryKind.Readback);
        byte[] expected = Enumerable.Range(0, 73).Select(index => (byte)(index * 17)).ToArray();
        Marshal.Copy(expected, 0, upload.Value.CpuAddress + 21, expected.Length);
        using NativeGpuCommandBuffer first = backend.MainQueue.StartCommandRecording();
        using NativeGpuCommandBuffer second = backend.MainQueue.StartCommandRecording();
        using NativeGpuSemaphore completion = backend.MainQueue.CreateSemaphore(0);
        first.CopyMemory(new(upload.Value, 21, 73), new(gpu.Value, 37, 80));
        second.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Copy, GpuAccess.CopyRead);
        second.CopyMemory(new(gpu.Value, 37, 73), new(readback.Value, 57, 73));
        second.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);

        backend.MainQueue.Submit([first, second], completion, 1);
        first.Dispose();
        second.Dispose();
        backend.MainQueue.Wait(completion, 1);

        Assert.Equal(expected, Read(readback.Value.CpuAddress + 57, expected.Length));
    }

    [Fact]
    public void CopyRejectsADestinationRangeSmallerThanTheSource()
    {
        using DirectX12Backend backend = CreateBackend();
        using var upload = new Region(backend, NativeGpuMemoryKind.CpuVisible);
        using var readback = new Region(backend, NativeGpuMemoryKind.Readback);
        using NativeGpuCommandBuffer recording = backend.MainQueue.StartCommandRecording();

        ArgumentException error = Assert.Throws<ArgumentException>(() => recording.CopyMemory(
            new(upload.Value, 0, 32), new(readback.Value, 0, 16)));

        Assert.Equal("destination", error.ParamName);
    }

    [Fact]
    public void FailedBatchDoesNotExecuteAnEarlierRecording()
    {
        using DirectX12Backend backend = CreateBackend();
        using var upload = new Region(backend, NativeGpuMemoryKind.CpuVisible);
        using var readback = new Region(backend, NativeGpuMemoryKind.Readback);
        using var destroyed = new Region(backend, NativeGpuMemoryKind.GpuOnly);
        byte[] marker = [23, 41, 89, 137];
        Marshal.Copy(marker, 0, readback.Value.CpuAddress, marker.Length);
        Marshal.Copy(new byte[marker.Length], 0, upload.Value.CpuAddress, marker.Length);
        using NativeGpuCommandBuffer first = backend.MainQueue.StartCommandRecording();
        using NativeGpuCommandBuffer second = backend.MainQueue.StartCommandRecording();
        using NativeGpuSemaphore completion = backend.MainQueue.CreateSemaphore(0);
        first.CopyMemory(new(upload.Value, 0, 4), new(readback.Value, 0, 4));
        second.CopyMemory(new(upload.Value, 0, 4), new(destroyed.Value, 0, 4));
        destroyed.Dispose();

        Assert.Throws<ObjectDisposedException>(() => backend.MainQueue.Submit([first, second], completion, 1));
        using NativeGpuCommandBuffer drain = backend.MainQueue.StartCommandRecording();
        backend.MainQueue.Submit([drain], completion, 2);
        backend.MainQueue.Wait(completion, 2);

        Assert.Equal(marker, Read(readback.Value.CpuAddress, marker.Length));
    }

    [Fact]
    public void AcceptedRecordingCannotBeSubmittedAgain()
    {
        using DirectX12Backend backend = CreateBackend();
        using NativeGpuCommandBuffer recording = backend.MainQueue.StartCommandRecording();
        using NativeGpuSemaphore completion = backend.MainQueue.CreateSemaphore(0);
        backend.MainQueue.Submit([recording], completion, 1);
        backend.MainQueue.Wait(completion, 1);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => backend.MainQueue.Submit([recording], completion, 2));

        Assert.Contains("no longer", error.Message);
    }

    [Fact]
    public void DisposingAnUnsubmittedRecordingDoesNotSubmitWork()
    {
        using DirectX12Backend backend = CreateBackend();
        using NativeGpuCommandBuffer recording = backend.MainQueue.StartCommandRecording();
        using NativeGpuSemaphore completion = backend.MainQueue.CreateSemaphore(0);

        recording.Dispose();

        Assert.Throws<InvalidOperationException>(() => backend.MainQueue.Submit([recording], completion, 1));
        Assert.False(backend.MainQueue.IsComplete(completion, 1));
    }

    [Fact]
    public void DuplicateRecordingIsRejectedBeforeSubmission()
    {
        using DirectX12Backend backend = CreateBackend();
        using NativeGpuCommandBuffer recording = backend.MainQueue.StartCommandRecording();
        using NativeGpuSemaphore completion = backend.MainQueue.CreateSemaphore(0);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => backend.MainQueue.Submit([recording, recording], completion, 1));

        Assert.Contains("more than once", error.Message);
    }

    [Theory]
    [InlineData("commands")]
    [InlineData("semaphore")]
    public void ForeignQueueObjectsAreRejected(string parameter)
    {
        using DirectX12Backend backend = CreateBackend();
        using DirectX12Backend other = CreateBackend();
        using NativeGpuCommandBuffer recording = (parameter == "commands" ? other : backend).MainQueue.StartCommandRecording();
        using NativeGpuSemaphore completion = (parameter == "semaphore" ? other : backend).MainQueue.CreateSemaphore(0);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => backend.MainQueue.Submit([recording], completion, 1));

        Assert.Equal(parameter, error.ParamName);
    }

    [Fact]
    public void CallerSemaphoreCanBeDisposedBeforeLaterQueueOperations()
    {
        using DirectX12Backend backend = CreateBackend();
        using NativeGpuCommandBuffer first = backend.MainQueue.StartCommandRecording();
        NativeGpuSemaphore firstCompletion = backend.MainQueue.CreateSemaphore(0);
        backend.MainQueue.Submit([first], firstCompletion, 1);
        backend.MainQueue.Wait(firstCompletion, 1);
        firstCompletion.Dispose();

        using NativeGpuCommandBuffer second = backend.MainQueue.StartCommandRecording();
        using NativeGpuSemaphore secondCompletion = backend.MainQueue.CreateSemaphore(5);
        backend.MainQueue.Submit([second], secondCompletion, 6);
        backend.MainQueue.Wait(secondCompletion, 6);

        Assert.True(backend.MainQueue.IsComplete(secondCompletion, 6));
    }

    [Theory]
    [InlineData(NativeGpuTextureDimension.OneD)]
    [InlineData(NativeGpuTextureDimension.TwoD)]
    [InlineData(NativeGpuTextureDimension.ThreeD)]
    public void TextureCopiesPreserveSubrectanglesAndCallerImagePitch(NativeGpuTextureDimension dimension)
    {
        using DirectX12Backend backend = CreateBackend();
        using var upload = new Region(backend, NativeGpuMemoryKind.CpuVisible);
        using var readback = new Region(backend, NativeGpuMemoryKind.Readback);
        bool volume = dimension == NativeGpuTextureDimension.ThreeD;
        bool oneD = dimension == NativeGpuTextureDimension.OneD;
        var description = new NativeGpuTextureDescription(dimension, 16, oneD ? 1u : 12u,
            volume ? 8u : 1u, 2, volume ? 1u : 3u, 1, GpuFormat.Rgba8Unorm,
            NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination);
        using var texture = new Texture(backend, description);
        var footprint = new NativeGpuTextureCopyFootprint(1, NativeGpuTextureAspect.Color,
            volume ? 0u : 1u, volume ? 1u : 2u, new(1, oneD ? 0u : 1u, volume ? 1u : 0u),
            new(3, oneD ? 1u : 2u, volume ? 2u : 1u), 256, 2048);
        var view = new NativeGpuTextureView(texture.Value, volume ? NativeGpuTextureViewDimension.ThreeD
            : oneD ? NativeGpuTextureViewDimension.OneD : NativeGpuTextureViewDimension.TwoDArray,
            description.Format, NativeGpuTextureAspect.Color, 1, 1, volume ? 0u : 1u, volume ? 1u : 2u);
        byte[] expected = WritePattern(upload.Value.CpuAddress + 512, footprint, 2, 4);
        using NativeGpuCommandBuffer recording = backend.MainQueue.StartCommandRecording();
        using NativeGpuSemaphore completion = backend.MainQueue.CreateSemaphore(0);
        recording.DiscardTexture(view, GpuTextureLayout.CopyDestination);
        recording.CopyMemoryToTexture(new(upload.Value, 512, 4096), texture.Value, footprint);
        recording.TextureTransition(view, GpuTextureLayout.CopyDestination, GpuTextureLayout.CopySource);
        recording.CopyTextureToMemory(texture.Value, new(readback.Value, 512, 4096), footprint);
        recording.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);

        backend.MainQueue.Submit([recording], completion, 1);
        backend.MainQueue.Wait(completion, 1);

        Assert.Equal(expected, ReadPattern(readback.Value.CpuAddress + 512, footprint, 2, 4));
    }

    [Theory]
    [InlineData(GpuFormat.D32Float, NativeGpuTextureAspect.Depth, 4)]
    [InlineData(GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.Depth, 4)]
    [InlineData(GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.Stencil, 1)]
    public void DepthAndStencilPlanesCopyTheirOwnElements(GpuFormat format, NativeGpuTextureAspect aspect, int bytes)
    {
        using DirectX12Backend backend = CreateBackend();
        using var upload = new Region(backend, NativeGpuMemoryKind.CpuVisible);
        using var readback = new Region(backend, NativeGpuMemoryKind.Readback);
        var description = new NativeGpuTextureDescription(NativeGpuTextureDimension.TwoD, 8, 4,
            1, 1, 1, 1, format, NativeGpuTextureUsage.DepthStencilAttachment
                | NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination);
        using var texture = new Texture(backend, description);
        var footprint = new NativeGpuTextureCopyFootprint(0, aspect, 0, 1, default, new(8, 4, 1), 256, 1024);
        var view = new NativeGpuTextureView(texture.Value, NativeGpuTextureViewDimension.TwoD,
            format, aspect, 0, 1, 0, 1);
        byte[] expected = WritePattern(upload.Value.CpuAddress + 512, footprint, 1, bytes);
        using NativeGpuCommandBuffer recording = backend.MainQueue.StartCommandRecording();
        using NativeGpuSemaphore completion = backend.MainQueue.CreateSemaphore(0);
        recording.DiscardTexture(view, GpuTextureLayout.CopyDestination);
        recording.CopyMemoryToTexture(new(upload.Value, 512, 1024), texture.Value, footprint);
        recording.TextureTransition(view, GpuTextureLayout.CopyDestination, GpuTextureLayout.CopySource);
        recording.CopyTextureToMemory(texture.Value, new(readback.Value, 512, 1024), footprint);
        recording.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);

        backend.MainQueue.Submit([recording], completion, 1);
        backend.MainQueue.Wait(completion, 1);

        byte[] actual = ReadPattern(readback.Value.CpuAddress + 512, footprint, 1, bytes);
        if (format == GpuFormat.Depth24PlusStencil8 && aspect == NativeGpuTextureAspect.Depth)
        {
            // The high byte of each native D24 depth element is unused.
            expected = expected.Where((_, index) => index % 4 != 3).ToArray();
            actual = actual.Where((_, index) => index % 4 != 3).ToArray();
        }
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TextureCopyRejectsAnInsufficientLogicalRange()
    {
        using DirectX12Backend backend = CreateBackend();
        using var upload = new Region(backend, NativeGpuMemoryKind.CpuVisible);
        using var texture = new Texture(backend, new(NativeGpuTextureDimension.TwoD,
            8, 4, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.CopyDestination));
        using NativeGpuCommandBuffer recording = backend.MainQueue.StartCommandRecording();
        using NativeGpuSemaphore completion = backend.MainQueue.CreateSemaphore(0);
        recording.CopyMemoryToTexture(new(upload.Value, 512, 16), texture.Value,
            new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(8, 4, 1), 256, 1024));

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => backend.MainQueue.Submit([recording], completion, 1));

        Assert.Equal("memory", error.ParamName);
    }

    [Theory]
    [InlineData(2u, 0u, 1u)]
    [InlineData(0u, 3u, 1u)]
    [InlineData(0u, 2u, 2u)]
    public void CopySubresourceAxesCannotOverflowIntoAnotherLayerOrPlane(uint mip, uint baseLayer, uint layerCount)
    {
        using DirectX12Backend backend = CreateBackend();
        using var upload = new Region(backend, NativeGpuMemoryKind.CpuVisible);
        using var texture = new Texture(backend, new(NativeGpuTextureDimension.TwoD,
            8, 4, 1, 2, 3, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.CopyDestination));
        using NativeGpuCommandBuffer recording = backend.MainQueue.StartCommandRecording();
        using NativeGpuSemaphore completion = backend.MainQueue.CreateSemaphore(0);
        recording.CopyMemoryToTexture(new(upload.Value, 512, 4096), texture.Value,
            new(mip, NativeGpuTextureAspect.Color, baseLayer, layerCount, default, new(1, 1, 1), 256, 1024));

        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => backend.MainQueue.Submit([recording], completion, 1));

        Assert.Equal("copy", error.ParamName);
    }

    [Fact]
    public void AliasedTexturesCanBeReactivatedWithExplicitBarriersAndDiscard()
    {
        using DirectX12Backend backend = CreateBackend();
        using var upload = new Region(backend, NativeGpuMemoryKind.CpuVisible);
        using var readback = new Region(backend, NativeGpuMemoryKind.Readback);
        var description = new NativeGpuTextureDescription(NativeGpuTextureDimension.TwoD, 8, 4,
            1, 1, 1, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination);
        NativeGpuMemoryRequirements requirements = backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
        NativeGpuHeap heap = backend.CreateGpuHeap(requirements.Size * 2, requirements.Alignment,
            NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
        NativeGpuTextureHandle first = backend.CreateTexture(description, heap, requirements.Size);
        NativeGpuTextureHandle second = backend.CreateTexture(description, heap, requirements.Size);
        try
        {
            var footprint = new NativeGpuTextureCopyFootprint(0, NativeGpuTextureAspect.Color, 0, 1,
                default, new(8, 4, 1), 256, 1024);
            byte[][] expected =
            [
                WritePattern(upload.Value.CpuAddress + 512, footprint, 1, 4, 11),
                WritePattern(upload.Value.CpuAddress + 1536, footprint, 1, 4, 53),
                WritePattern(upload.Value.CpuAddress + 2560, footprint, 1, 4, 97),
            ];
            using NativeGpuCommandBuffer recording = backend.MainQueue.StartCommandRecording();
            using NativeGpuSemaphore completion = backend.MainQueue.CreateSemaphore(0);
            NativeGpuTextureHandle[] sequence = [first, second, first];
            for (int index = 0; index < sequence.Length; index++)
            {
                if (index != 0)
                {
                    recording.Barrier(GpuStage.Copy, GpuAccess.CopyRead | GpuAccess.CopyWrite,
                        GpuStage.Copy, GpuAccess.CopyRead | GpuAccess.CopyWrite);
                }
                var view = new NativeGpuTextureView(sequence[index], NativeGpuTextureViewDimension.TwoD,
                    description.Format, NativeGpuTextureAspect.Color, 0, 1, 0, 1);
                ulong offset = checked(512ul + (ulong)index * 1024);
                recording.DiscardTexture(view, GpuTextureLayout.CopyDestination);
                recording.CopyMemoryToTexture(new(upload.Value, offset, 1024), sequence[index], footprint);
                recording.TextureTransition(view, GpuTextureLayout.CopyDestination, GpuTextureLayout.CopySource);
                recording.CopyTextureToMemory(sequence[index], new(readback.Value, offset, 1024), footprint);
            }
            recording.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);

            backend.MainQueue.Submit([recording], completion, 1);
            backend.MainQueue.Wait(completion, 1);

            Assert.Collection(
                new[]
                {
                    ReadPattern(readback.Value.CpuAddress + 512, footprint, 1, 4),
                    ReadPattern(readback.Value.CpuAddress + 1536, footprint, 1, 4),
                    ReadPattern(readback.Value.CpuAddress + 2560, footprint, 1, 4),
                },
                actual => Assert.Equal(expected[0], actual),
                actual => Assert.Equal(expected[1], actual),
                actual => Assert.Equal(expected[2], actual));
        }
        finally
        {
            backend.DestroyTexture(second);
            backend.DestroyTexture(first);
            backend.DestroyGpuHeap(heap);
        }
    }

    // These are hardware conformance tests; the optional Windows Graphics Tools debug layer
    // is absent on the test machine (D3D12GetDebugInterface returns 0x887A002D).
    private static DirectX12Backend CreateBackend() => DirectX12Backend.Create();

    private static byte[] WritePattern(nint address, NativeGpuTextureCopyFootprint footprint, int slices, int elementBytes, int seed = 0)
    {
        var expected = new List<byte>();
        for (int slice = 0; slice < slices; slice++)
        {
            for (int row = 0; row < footprint.Extent.Height; row++)
            {
                byte[] bytes = Enumerable.Range(0, checked((int)footprint.Extent.Width * elementBytes))
                    .Select(column => (byte)(seed + slice * 53 + row * 19 + column * 7)).ToArray();
                Marshal.Copy(bytes, 0, address + checked((nint)((ulong)slice * footprint.ImagePitch + (ulong)row * footprint.RowPitch)), bytes.Length);
                expected.AddRange(bytes);
            }
        }
        return expected.ToArray();
    }

    private static byte[] ReadPattern(nint address, NativeGpuTextureCopyFootprint footprint, int slices, int elementBytes)
    {
        var result = new List<byte>();
        for (int slice = 0; slice < slices; slice++)
        {
            for (int row = 0; row < footprint.Extent.Height; row++)
            {
                result.AddRange(Read(address + checked((nint)((ulong)slice * footprint.ImagePitch + (ulong)row * footprint.RowPitch)),
                    checked((int)footprint.Extent.Width * elementBytes)));
            }
        }
        return result.ToArray();
    }

    private static byte[] Read(nint address, int length)
    {
        var result = new byte[length];
        Marshal.Copy(address, result, 0, length);
        return result;
    }

    private sealed class Region : IDisposable
    {
        private readonly DirectX12Backend backend;
        private readonly NativeGpuHeap heap;
        private bool disposed;
        public NativeGpuLinearRegion Value { get; }
        public Region(DirectX12Backend backend, NativeGpuMemoryKind kind)
        {
            this.backend = backend;
            NativeGpuMemoryRequirements requirements = backend.GetLinearMemoryRequirements(8192, kind);
            heap = backend.CreateGpuHeap(checked(requirements.Size * 2), requirements.Alignment, kind, [requirements.Compatibility]);
            Value = backend.CreateLinearRegion(8192, heap, requirements.Size);
        }
        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            backend.DestroyLinearRegion(Value);
            backend.DestroyGpuHeap(heap);
        }
    }

    private sealed class Texture : IDisposable
    {
        private readonly DirectX12Backend backend;
        private readonly NativeGpuHeap heap;
        public NativeGpuTextureHandle Value { get; }
        public Texture(DirectX12Backend backend, NativeGpuTextureDescription description)
        {
            this.backend = backend;
            NativeGpuMemoryRequirements requirements = backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
            heap = backend.CreateGpuHeap(checked(requirements.Size * 2), requirements.Alignment, NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
            Value = backend.CreateTexture(description, heap, requirements.Size);
        }
        public void Dispose() { backend.DestroyTexture(Value); backend.DestroyGpuHeap(heap); }
    }
}
