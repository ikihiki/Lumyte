using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Checks independent presentation targets sharing one device exclusively through common APIs.</summary>
public static class MultiSurfaceExercise
{
    /// <summary>Draws to two targets, then closes the first while continuing to use the second.</summary>
    /// <param name="device">The shared device and queue.</param>
    /// <param name="first">The owned first surface; this test disposes it after releasing its swapchain.</param>
    /// <param name="second">The caller-owned second surface, which remains valid on return.</param>
    /// <returns>The multi-target verification report.</returns>
    public static async Task<string> RunAsync(IGraphicDevice device, IGraphicsSurface first, IGraphicsSurface second)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (ReferenceEquals(first, second))
        {
            throw new ArgumentException("Supply two independent presentation targets.");
        }

        SwapchainDesc firstDesc = Configuration(first, 8, 6);
        SwapchainDesc secondDesc = Configuration(second, 5, 9);
        using IGraphicsSwapchain firstSwapchain = first.CreateSwapchain(firstDesc);
        using IGraphicsSwapchain secondSwapchain = second.CreateSwapchain(secondDesc);
        Console.WriteLine("Multiple surfaces: acquiring both targets before submission.");
        using (RecordedFrame a = await RecordAsync(device, firstSwapchain, new(1, 0, 0, 1)))
        {
            using RecordedFrame b = await RecordAsync(device, secondSwapchain, new(0, 1, 0, 1));
            Require(!ReferenceEquals(a.Frame.Texture, b.Frame.Texture), "Independent targets shared one acquired image.");
            using IGraphicsSubmission submission = device.Queue.Submit(new QueueSubmitDesc
            {
                CommandBuffers = [a.Commands, b.Commands],
                WaitSemaphores = [a.Wait, b.Wait],
                SignalSemaphores = [a.Rendered, b.Rendered],
            });
            Present(b);
            Present(a);
            await a.Frame.WaitForReleaseAsync();
            await b.Frame.WaitForReleaseAsync();
            await submission.WaitAsync();
            await a.CheckPixelsAsync();
            await b.CheckPixelsAsync();
        }

        Console.WriteLine("Multiple surfaces: resizing the first while the second image remains acquired.");
        using (RecordedFrame b = await RecordAsync(device, secondSwapchain, new(0, 0, 1, 1)))
        {
            SwapchainDesc resized = Configuration(first, 11, 4);
            firstSwapchain.Reconfigure(resized);
            Require(secondSwapchain.Configuration == secondDesc && b.Frame.Status == SurfaceFrameStatus.Acquired, "Resizing one target changed the other target.");
            using RecordedFrame a = await RecordAsync(device, firstSwapchain, new(1, 1, 1, 1));
            using IGraphicsSubmission submittedA = Submit(device, a);
            Present(a);
            using IGraphicsSubmission submittedB = Submit(device, b);
            Present(b);
            await a.Frame.WaitForReleaseAsync();
            await b.Frame.WaitForReleaseAsync();
            await submittedA.WaitAsync();
            await submittedB.WaitAsync();
            await a.CheckPixelsAsync();
            await b.CheckPixelsAsync();
        }

        Console.WriteLine("Multiple surfaces: closing the first while the second image remains acquired.");
        using (RecordedFrame b = await RecordAsync(device, secondSwapchain, new(1, 0, 0, 1)))
        {
            firstSwapchain.Dispose();
            first.Dispose();
            Expect<ObjectDisposedException>(() => first.GetCapabilities());
            Require(b.Frame.Status == SurfaceFrameStatus.Acquired && secondSwapchain.Configuration == secondDesc, "Closing one target invalidated the other target.");
            using IGraphicsSubmission submission = Submit(device, b);
            Present(b);
            await b.Frame.WaitForReleaseAsync();
            await submission.WaitAsync();
            await b.CheckPixelsAsync();
        }

        secondSwapchain.Reconfigure(Configuration(second, 7, 3));
        using (RecordedFrame b = await RecordAsync(device, secondSwapchain, new(0, 1, 0, 1)))
        {
            using IGraphicsSubmission submission = Submit(device, b);
            Present(b);
            await b.Frame.WaitForReleaseAsync();
            await submission.WaitAsync();
            await b.CheckPixelsAsync();
        }

        return "Multiple surface checks passed: simultaneous acquisition, distinct pixel results, shared and independent submissions, separate presentation signals, independent resize/close and surviving-target rendering.";
    }

    private static SwapchainDesc Configuration(IGraphicsSurface surface, uint width, uint height)
    {
        SurfaceCapabilities caps = surface.GetCapabilities();
        return new()
        {
            Width = caps.CurrentWidth ?? width,
            Height = caps.CurrentHeight ?? height,
            Format = caps.Formats.Contains(TextureFormat.Rgba8Unorm) ? TextureFormat.Rgba8Unorm : TextureFormat.Bgra8Unorm,
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource,
        };
    }

    private static async Task<RecordedFrame> RecordAsync(IGraphicDevice device, IGraphicsSwapchain swapchain, ClearColor clear)
    {
        IGraphicsSemaphore acquired = device.CreateSemaphore();
        IGraphicsSemaphore rendered = device.CreateSemaphore();
        SurfaceAcquireResult result = await swapchain.AcquireNextFrameAsync(acquired);
        IGraphicsSurfaceFrame frame = result.Frame ?? throw new InvalidOperationException($"Presentation target acquisition failed: {result.Status}.");
        IGraphicsTexture texture = frame.Texture;
        uint alignment = device.GetTextureCopyLayout(texture.Format).BytesPerRowAlignment;
        uint pitch = checked((((texture.Width * 4) + alignment - 1) / alignment) * alignment);
        IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = checked(pitch * texture.Height), Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        IGraphicsTextureView view = texture.CreateView();
        IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        var copy = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
        IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = clear }] });
        render.End();
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = copy });
        commands.CopyTextureToBuffer(new() { Texture = texture, Width = texture.Width, Height = texture.Height }, new() { Buffer = readback.Slice(0, readback.Count), BytesPerRow = pitch, RowsPerImage = texture.Height });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.Slice(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.CopySource, AfterState = TextureState.Present, Before = copy, After = default });
        commands.Finish();
        byte[] pixel = [checked((byte)(clear.Red * 255)), checked((byte)(clear.Green * 255)), checked((byte)(clear.Blue * 255)), checked((byte)(clear.Alpha * 255))];
        if (texture.Format == TextureFormat.Bgra8Unorm)
        {
            (pixel[0], pixel[2]) = (pixel[2], pixel[0]);
        }

        return new(frame, acquired, rendered, readback, view, commands, pitch, pixel);
    }

    private static IGraphicsSubmission Submit(IGraphicDevice device, RecordedFrame frame) => device.Queue.Submit(new QueueSubmitDesc
    {
        CommandBuffers = [frame.Commands],
        WaitSemaphores = [frame.Wait],
        SignalSemaphores = [frame.Rendered],
    });

    private static void Present(RecordedFrame frame) => Require(frame.Frame.Present([frame.Rendered]) is SurfaceStatus.Success or SurfaceStatus.Suboptimal, "Presentation failed for one target.");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Expect<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class RecordedFrame(IGraphicsSurfaceFrame frame, IGraphicsSemaphore acquired, IGraphicsSemaphore rendered, IGraphicsBuffer<byte> readback, IGraphicsTextureView view, IGraphicsCommandBuffer commands, uint pitch, byte[] pixel) : IDisposable
    {
        public IGraphicsSurfaceFrame Frame => frame;

        public IGraphicsSemaphore Rendered => rendered;

        public SemaphoreWaitDesc Wait { get; } = new() { Semaphore = acquired, Stages = PipelineStage.AllCommands };

        public IGraphicsCommandBuffer Commands => commands;

        public async Task CheckPixelsAsync()
        {
            await readback.MapAsync();
            byte[] bytes = new byte[checked((int)readback.Count)];
            readback.CopyTo(bytes);
            readback.Unmap();
            for (uint y = 0; y < frame.Texture.Height; y++)
            {
                for (uint x = 0; x < frame.Texture.Width; x++)
                {
                    Require(bytes.AsSpan(checked((int)((y * pitch) + (x * 4))), 4).SequenceEqual(pixel), "One presentation target read back another target's color or incorrect pixels.");
                }
            }
        }

        public void Dispose()
        {
            view.Dispose();
            commands.Dispose();
            frame.Dispose();
            readback.Dispose();
            acquired.Dispose();
            rendered.Dispose();
        }
    }
}
