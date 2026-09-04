using System.Numerics;

using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.TwoD.Tests;

public abstract class BackendConformanceTests
{
    private const uint Width = 64;
    private const uint Height = 64;
    private const ulong RowPitch = Width * 4;
    private const ulong ByteCount = RowPitch * Height;

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void PrimitiveAndPolygonRoutesRenderWithoutGeometryBindings()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.FillRectangle(new(4, 4, 24, 24), Brush.Solid(new(1, 0, 0)));
        encoder.FillRoundedRectangle(new(36, 4, 24, 24), new(8), Brush.Solid(new(0, 1, 0)));
        encoder.DrawLine(new(8, 32), new(56, 32), 4, Brush.Solid(Color.White));
        encoder.FillEllipse(new(4, 36, 24, 24), Brush.Solid(new(0, 0, 1)));
        encoder.DrawGeometry(
            PolygonGeometry.FromConvexPolygon([
                new(36, 60),
                new(60, 60),
                new(48, 36),
            ]),
            Matrix3x2.Identity,
            Brush.Solid(new(1, 1, 0)));
        DisplayList displayList = encoder.Finish();
        using PreparedDisplayList prepared = renderer.Prepare(displayList, target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "two-d",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 0, 0)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 16, 16, 255, 0, 0, 255);
        AssertPixelNear(pixels, 48, 16, 0, 255, 0, 255);
        AssertPixelNear(pixels, 32, 32, 255, 255, 255, 255);
        AssertPixelNear(pixels, 16, 48, 0, 0, 255, 255);
        AssertPixelNear(pixels, 48, 48, 255, 255, 0, 255);
        AssertPixelNear(pixels, 1, 1, 0, 0, 0, 0);
        AssertPixelNear(pixels, 36, 4, 0, 0, 0, 0);
        AssertPixelNear(pixels, 4, 36, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void ClipPreservesPainterOrderWithinItsBounds()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(new(1, 0, 0)));
        encoder.Save();
        encoder.Clip(new(0, 0, 32, Height));
        encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(new(0, 0, 1)));
        encoder.Restore();
        DisplayList displayList = encoder.Finish();
        using PreparedDisplayList prepared = renderer.Prepare(displayList, target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "two-d",
            renderer,
            prepared,
            new RenderTarget(target.Handle, target.Description, GpuAttachmentLoadOperation.Clear));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 16, 16, 0, 0, 255, 255);
        AssertPixelNear(pixels, 48, 16, 255, 0, 0, 255);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void ImageRouteUsesPremultipliedSourceOverBlending()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        var imageDescription = new GpuTextureDescription(
            2,
            2,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.Sampled | GpuTextureUsage.CopyDestination);
        using var image = BackendTexture.Create(backend, imageDescription);
        byte[] texels =
        [
            255, 0, 0, 128, 255, 0, 0, 128,
            255, 0, 0, 128, 255, 0, 0, 128,
        ];
        UploadTexture(backend, image.Handle, image.Description, texels);
        SamplerId sampler = backend.CreateSampler(default);
        try
        {
            using var renderer = new Renderer(backend);
            ImageId registered = renderer.RegisterImage(image.Handle, image.Description, sampler);
            using CommandEncoder encoder = renderer.CreateCommandEncoder();
            encoder.DrawImage(registered, new(0, 0, Width, Height));
            DisplayList displayList = encoder.Finish();
            using PreparedDisplayList prepared = renderer.Prepare(displayList, target.Description);
            var graph = new GpuRenderGraph();
            graph.AddTwoD(
                "two-d",
                renderer,
                prepared,
                new RenderTarget(
                    target.Handle,
                    target.Description,
                    GpuAttachmentLoadOperation.Clear,
                    ClearColor: new(0, 0, 1, 1)));

            using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
            byte[] pixels = ReadPixels(backend, target.Handle);

            AssertPixelNear(pixels, 32, 32, 128, 0, 127, 255, tolerance: 2);
        }
        finally
        {
            backend.DestroySampler(sampler);
        }
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void SolidBrushUsesPremultipliedSourceOverBlending()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.FillRectangle(
            new(0, 0, Width, Height),
            Brush.Solid(new(1, 0, 0, 0.5f)));
        DisplayList displayList = encoder.Finish();
        using PreparedDisplayList prepared = renderer.Prepare(displayList, target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "two-d",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 1, 1)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 32, 32, 128, 0, 127, 255, tolerance: 2);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void PartialBufferWritesPreserveUnaffectedBytes()
    {
        using IGpuBackend backend = CreateBackend();
        byte[] initial = Enumerable.Range(0, 16).Select(static value => (byte)value).ToArray();
        byte[] replacement = [40, 41, 42, 43];
        byte[] expected = [0, 1, 2, 3, 40, 41, 42, 43, 8, 9, 10, 11, 12, 13, 14, 15];

        byte[] actual = WriteAndReadBuffer(backend, initial, replacement, 4);

        Assert.Equal(expected, actual);
    }

    protected abstract IGpuBackend CreateBackend();

    private static GpuTextureDescription TargetDescription() => new(
        Width,
        Height,
        GpuFormat.Rgba8Unorm,
        GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource);

    private static void UploadTexture(
        IGpuBackend backend,
        GpuTextureHandle texture,
        GpuTextureDescription description,
        ReadOnlySpan<byte> texels)
    {
        var footprint = new GpuTextureCopyFootprint(
            description.Width,
            description.Height,
            4,
            description.Width * 4);
        if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
        {
            backend.WriteTexture(texture, texels, footprint);
            return;
        }

        var bufferDescription = new GpuBufferDescription(
            checked((ulong)texels.Length),
            GpuBufferUsage.CopySource);
        GpuBufferMemoryRequirements requirements = backend.GetBufferMemoryRequirements(bufferDescription);
        GpuMemoryAllocation allocation = backend.AllocateMemory(
            requirements.Size,
            requirements.Alignment,
            GpuMemoryKind.HostMapped,
            requirements.Compatibility);
        GpuBufferHandle upload = default;
        try
        {
            upload = backend.CreatePlacedBuffer(bufferDescription, allocation);
            backend.WriteBuffer(upload, texels);
            GpuCommandBuffer commands = backend.MainQueue.StartCommandRecording()
                .CopyMemoryToTexture(
                    backend.GetBufferMemoryAddress(upload, 0, checked((ulong)texels.Length)),
                    texture,
                    footprint)
                .Barrier(GpuStage.Copy, GpuStage.PixelShader);
            Submit(backend, commands);
        }
        finally
        {
            if (!upload.IsNull) { backend.DestroyBuffer(upload); }
            backend.FreeMemory(allocation);
        }
    }

    private static byte[] ReadPixels(IGpuBackend backend, GpuTextureHandle texture)
    {
        if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
        {
            return backend.ReadTexture(texture, new(Width, Height, 4, RowPitch));
        }

        var description = new GpuBufferDescription(ByteCount, GpuBufferUsage.CopyDestination);
        GpuBufferMemoryRequirements requirements = backend.GetBufferMemoryRequirements(description);
        GpuMemoryAllocation allocation = backend.AllocateMemory(
            requirements.Size,
            requirements.Alignment,
            GpuMemoryKind.HostCached,
            requirements.Compatibility);
        GpuBufferHandle readback = default;
        try
        {
            readback = backend.CreatePlacedBuffer(description, allocation);
            GpuCommandBuffer commands = backend.MainQueue.StartCommandRecording()
                .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                .CopyTextureToMemory(
                    texture,
                    backend.GetBufferMemoryAddress(readback, 0, ByteCount),
                    new(Width, Height, 4, RowPitch));
            Submit(backend, commands);
            return allocation.MappedBytes()[..checked((int)ByteCount)].ToArray();
        }
        finally
        {
            if (!readback.IsNull) { backend.DestroyBuffer(readback); }
            backend.FreeMemory(allocation);
        }
    }

    private static byte[] WriteAndReadBuffer(
        IGpuBackend backend,
        ReadOnlySpan<byte> initial,
        ReadOnlySpan<byte> replacement,
        ulong destinationOffset)
    {
        var description = new GpuBufferDescription(
            checked((ulong)initial.Length),
            GpuBufferUsage.ShaderData | GpuBufferUsage.CopySource | GpuBufferUsage.CopyDestination);
        if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
        {
            GpuBufferHandle buffer = backend.CreateBuffer(description);
            try
            {
                backend.WriteBuffer(buffer, initial);
                backend.WriteBuffer(buffer, destinationOffset, replacement);
                return backend.ReadBuffer(buffer);
            }
            finally
            {
                backend.DestroyBuffer(buffer);
            }
        }

        GpuBufferMemoryRequirements requirements = backend.GetBufferMemoryRequirements(description);
        GpuMemoryAllocation allocation = backend.AllocateMemory(
            requirements.Size,
            requirements.Alignment,
            GpuMemoryKind.HostMapped,
            requirements.Compatibility);
        GpuBufferHandle placedBuffer = default;
        try
        {
            placedBuffer = backend.CreatePlacedBuffer(description, allocation);
            backend.WriteBuffer(placedBuffer, initial);
            backend.WriteBuffer(placedBuffer, destinationOffset, replacement);
            return allocation.MappedBytes()[..initial.Length].ToArray();
        }
        finally
        {
            if (!placedBuffer.IsNull) { backend.DestroyBuffer(placedBuffer); }
            backend.FreeMemory(allocation);
        }
    }

    private static void Submit(IGpuBackend backend, GpuCommandBuffer commands)
    {
        using GpuSemaphore completion = backend.MainQueue.CreateSemaphore();
        backend.MainQueue.Submit([commands], completion, 1);
        backend.MainQueue.Wait(completion, 1);
    }

    private static void AssertPixelNear(
        ReadOnlySpan<byte> pixels,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha,
        int tolerance = 1)
    {
        int offset = checked((y * (int)Width + x) * 4);
        byte[] actual = pixels.Slice(offset, 4).ToArray();
        byte[] expected = [red, green, blue, alpha];
        bool matches = actual.Zip(expected).All(pair => Math.Abs(pair.First - pair.Second) <= tolerance);
        Assert.True(
            matches,
            $"Pixel ({x}, {y}) expected [{string.Join(", ", expected)}] "
                + $"within {tolerance}, but was [{string.Join(", ", actual)}].");
    }

    private sealed class BackendTexture : IDisposable
    {
        private readonly IGpuBackend backend;
        private GpuMemoryAllocation allocation;

        private BackendTexture(
            IGpuBackend backend,
            GpuTextureHandle handle,
            GpuTextureDescription description,
            GpuMemoryAllocation allocation)
        {
            this.backend = backend;
            Handle = handle;
            Description = description;
            this.allocation = allocation;
        }

        public GpuTextureHandle Handle { get; }
        public GpuTextureDescription Description { get; }

        public static BackendTexture Create(
            IGpuBackend backend,
            GpuTextureDescription description)
        {
            if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
            {
                return new(backend, backend.CreateTexture(description), description, default);
            }

            GpuTextureMemoryRequirements requirements = backend.GetTextureMemoryRequirements(description);
            GpuMemoryAllocation allocation = backend.AllocateMemory(
                requirements.Size,
                requirements.Alignment,
                GpuMemoryKind.DeviceLocal,
                requirements.Compatibility);
            try
            {
                return new(
                    backend,
                    backend.CreatePlacedTexture(description, allocation),
                    description,
                    allocation);
            }
            catch
            {
                backend.FreeMemory(allocation);
                throw;
            }
        }

        public void Dispose()
        {
            backend.DestroyTexture(Handle);
            if (!allocation.MemoryAddress.IsNull)
            {
                backend.FreeMemory(allocation);
                allocation = default;
            }
        }
    }
}
