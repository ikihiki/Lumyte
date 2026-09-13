using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;
using Lumyte.Graphics.Native.Resources;

namespace Lumyte.Graphics.Tests;

// Linked into the Vulkan test project so both native backends exercise the same public utility contract.
// This fixture tests successful GPU completion; it does not model failure retirement or device shutdown.
internal static class NativeMemoryArenaConformance
{
    internal static void ReuseLinearSlices(INativeGpuBackend backend)
    {
        NativeGpuMemoryRequirements requirements = backend.GetLinearMemoryRequirements(256, NativeGpuMemoryKind.GpuOnly);
        using var arena = new GpuMemoryArena(backend, checked(requirements.Size * 2));
        using var upload = new OwnedLinear(backend, 256, NativeGpuMemoryKind.CpuVisible);
        using var readback = new OwnedLinear(backend, 256, NativeGpuMemoryKind.Readback);
        using var first = new PlacedLinear(backend, arena, requirements.Size, requirements.Alignment,
            [requirements.Compatibility], 256);
        using var second = new PlacedLinear(backend, arena, requirements.Size, requirements.Alignment,
            [requirements.Compatibility], 256);
        byte[] expected = Pattern(128, 17);
        Marshal.Copy(expected, 0, upload.Region.CpuAddress, expected.Length);
        AssertDisjointSlices(first.Slice, second.Slice);

        CopyPair(backend, upload.Region, first.Region, second.Region, readback.Region, writeSecond: true);
        Assert.Equal(expected, Read(readback.Region.CpuAddress, 128));
        NativeGpuHeap previousHeap = first.Slice.Heap;
        ulong previousOffset = first.Slice.Offset;
        first.Dispose();
        using var replacement = new PlacedLinear(backend, arena, requirements.Size, requirements.Alignment,
            [requirements.Compatibility], 256);
        byte[] changed = Pattern(64, 83);
        Marshal.Copy(changed, 0, upload.Region.CpuAddress, changed.Length);

        CopyPair(backend, upload.Region, replacement.Region, second.Region, readback.Region, writeSecond: false);

        Assert.Same(previousHeap, replacement.Slice.Heap);
        Assert.Equal(previousOffset, replacement.Slice.Offset);
        Assert.Equal(changed.Concat(expected.Skip(64)), Read(readback.Region.CpuAddress, 128));
    }

    internal static void TransferThroughMixedHeap(INativeGpuBackend backend)
    {
        var description = new NativeGpuTextureDescription(NativeGpuTextureDimension.TwoD,
            8, 8, 1, 1, 1, 1, GpuFormat.Rgba8Unorm,
            NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination);
        NativeGpuMemoryRequirements linear = backend.GetLinearMemoryRequirements(4096, NativeGpuMemoryKind.GpuOnly);
        NativeGpuMemoryRequirements texture = backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
        NativeGpuMemoryCompatibility[] compatibilities = [linear.Compatibility, texture.Compatibility];
        ulong alignment = Math.Max(linear.Alignment, texture.Alignment);
        ulong blockSize = checked(Align(linear.Size, alignment) + Align(texture.Size, alignment));
        using var arena = new GpuMemoryArena(backend, blockSize);
        using var upload = new OwnedLinear(backend, 4096, NativeGpuMemoryKind.CpuVisible);
        using var readback = new OwnedLinear(backend, 4096, NativeGpuMemoryKind.Readback);
        using var placedLinear = new PlacedLinear(backend, arena, linear.Size, alignment, compatibilities, 4096);
        GpuMemorySlice textureSlice = arena.Allocate(texture.Size, alignment, NativeGpuMemoryKind.GpuOnly, compatibilities);
        NativeGpuTextureHandle? placedTexture = null;
        try
        {
            placedTexture = backend.CreateTexture(description, textureSlice.Heap, textureSlice.Offset);
            AssertDisjointSlices(placedLinear.Slice, textureSlice);
            byte[] expected = Pattern(256, 41);
            for (int row = 0; row < 8; row++)
            { Marshal.Copy(expected, row * 32, upload.Region.CpuAddress + row * 256, 32); }
            var view = new NativeGpuTextureView(placedTexture, NativeGpuTextureViewDimension.TwoD,
                GpuFormat.Rgba8Unorm, NativeGpuTextureAspect.Color, 0, 1, 0, 1);
            var footprint = new NativeGpuTextureCopyFootprint(0, NativeGpuTextureAspect.Color,
                0, 1, default, new(8, 8, 1), 256, 2048);
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.CopyMemory(new(upload.Region, 0, 2048), new(placedLinear.Region, 0, 2048));
            CopyDependency(commands);
            commands.DiscardTexture(view, backend.Capabilities.ExplicitTextureTransitions
                ? GpuTextureLayout.CopyDestination : GpuTextureLayout.General);
            commands.CopyMemoryToTexture(new(placedLinear.Region, 0, 2048), placedTexture, footprint);
            if (backend.Capabilities.ExplicitTextureTransitions)
            { commands.TextureTransition(view, GpuTextureLayout.CopyDestination, GpuTextureLayout.CopySource); }
            else { CopyDependency(commands); }
            commands.CopyTextureToMemory(placedTexture, new(readback.Region, 0, 2048), footprint);
            commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);

            SubmitAndWait(backend, commands);

            byte[] actual = Enumerable.Range(0, 8)
                .SelectMany(row => Read(readback.Region.CpuAddress + row * 256, 32)).ToArray();
            Assert.Equal(expected, actual);
        }
        finally
        {
            if (placedTexture is not null) { backend.DestroyTexture(placedTexture); }
            arena.Release(textureSlice);
        }
    }

    private static void CopyPair(INativeGpuBackend backend, NativeGpuLinearRegion upload,
        NativeGpuLinearRegion first, NativeGpuLinearRegion second, NativeGpuLinearRegion readback, bool writeSecond)
    {
        using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
        // The caller supplies the dependency even when replacing a completed placed resource.
        commands.Barrier(GpuStage.All, GpuAccess.CopyRead | GpuAccess.CopyWrite,
            GpuStage.All, GpuAccess.CopyRead | GpuAccess.CopyWrite);
        commands.CopyMemory(new(upload, 0, 64), new(first, 0, 64));
        if (writeSecond) { commands.CopyMemory(new(upload, 64, 64), new(second, 0, 64)); }
        CopyDependency(commands);
        commands.CopyMemory(new(first, 0, 64), new(readback, 0, 64));
        commands.CopyMemory(new(second, 0, 64), new(readback, 64, 64));
        commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);
        SubmitAndWait(backend, commands);
    }

    private static void CopyDependency(NativeGpuCommandBuffer commands)
        => commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Copy, GpuAccess.CopyRead | GpuAccess.CopyWrite);

    private static void AssertDisjointSlices(GpuMemorySlice first, GpuMemorySlice second)
    {
        Assert.Same(first.Heap, second.Heap);
        Assert.True(first.Offset + first.Size <= second.Offset || second.Offset + second.Size <= first.Offset,
            "Live slices in one backing heap must occupy non-overlapping byte ranges.");
    }

    private static void SubmitAndWait(INativeGpuBackend backend, NativeGpuCommandBuffer commands)
    {
        using NativeGpuSemaphore completion = backend.CreateSemaphore();
        backend.MainQueue.Submit([commands], new(completion, 1));
        commands.Dispose();
        completion.WaitCpu(1);
    }

    private static ulong Align(ulong value, ulong alignment) => checked((value + alignment - 1) / alignment * alignment);
    private static byte[] Pattern(int count, int seed) => Enumerable.Range(0, count).Select(index => (byte)(index * 37 + seed)).ToArray();
    private static byte[] Read(nint address, int size)
    {
        var bytes = new byte[size];
        Marshal.Copy(address, bytes, 0, size);
        return bytes;
    }

    private sealed class PlacedLinear : IDisposable
    {
        private readonly INativeGpuBackend backend;
        private readonly GpuMemoryArena arena;
        private bool disposed;

        internal PlacedLinear(INativeGpuBackend backend, GpuMemoryArena arena, ulong size, ulong alignment,
            ReadOnlySpan<NativeGpuMemoryCompatibility> compatibilities, ulong logicalSize)
        {
            this.backend = backend;
            this.arena = arena;
            Slice = arena.Allocate(size, alignment, NativeGpuMemoryKind.GpuOnly, compatibilities);
            try { Region = backend.CreateLinearRegion(logicalSize, Slice.Heap, Slice.Offset); }
            catch { arena.Release(Slice); throw; }
        }

        internal GpuMemorySlice Slice { get; }
        internal NativeGpuLinearRegion Region { get; }

        public void Dispose()
        {
            if (disposed) { return; }
            backend.DestroyLinearRegion(Region);
            arena.Release(Slice);
            disposed = true;
        }
    }

    private sealed class OwnedLinear : IDisposable
    {
        private readonly INativeGpuBackend backend;
        private readonly NativeGpuHeap heap;

        internal OwnedLinear(INativeGpuBackend backend, ulong size, NativeGpuMemoryKind kind)
        {
            this.backend = backend;
            NativeGpuMemoryRequirements requirements = backend.GetLinearMemoryRequirements(size, kind);
            heap = backend.CreateGpuHeap(requirements.Size, requirements.Alignment, kind, [requirements.Compatibility]);
            try { Region = backend.CreateLinearRegion(size, heap, 0); }
            catch { backend.DestroyGpuHeap(heap); throw; }
        }

        internal NativeGpuLinearRegion Region { get; }

        public void Dispose()
        {
            backend.DestroyLinearRegion(Region);
            backend.DestroyGpuHeap(heap);
        }
    }
}
