using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Exercises externally supplied presentation targets solely through common graphics APIs.</summary>
public static class SurfaceExercise
{
    /// <summary>Checks exact configuration, borrowed images, explicit submission, presentation and readback.</summary>
    /// <param name="device">The device selected for this surface.</param>
    /// <param name="surface">The already supplied graphics connection; no window or canvas is created here.</param>
    /// <returns>The verification report.</returns>
    public static async Task<string> RunAsync(IGraphicDevice device, IGraphicsSurface surface)
    {
        SurfaceCapabilities caps = surface.GetCapabilities();
        TextureFormat format = caps.Formats.Contains(TextureFormat.Rgba8Unorm) ? TextureFormat.Rgba8Unorm : TextureFormat.Bgra8Unorm;
        var desc = new SwapchainDesc
        {
            Width = caps.CurrentWidth ?? 8,
            Height = caps.CurrentHeight ?? 6,
            Format = format,
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource,
        };
        Expect<ArgumentException>(() => surface.CreateSwapchain(desc with { Width = 0 }));
        Expect<ArgumentException>(() => surface.CreateSwapchain(desc with { Height = 0 }));
        Expect<ArgumentException>(() => surface.CreateSwapchain(desc with { Format = (TextureFormat)999 }));
        Expect<NotSupportedException>(() => surface.CreateSwapchain(desc with { Format = TextureFormat.Depth32Float }));
        foreach (PresentMode mode in Enum.GetValues<PresentMode>().Except(caps.PresentModes))
        {
            Expect<NotSupportedException>(() => surface.CreateSwapchain(desc with { PresentMode = mode }));
        }

        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(desc);
        Expect<InvalidOperationException>(() => surface.CreateSwapchain(desc));
        Expect<InvalidOperationException>(surface.Dispose);
        if (device is IDisposable owner)
        {
            Expect<InvalidOperationException>(owner.Dispose);
        }

        for (int i = 0; i < 3; i++)
        {
            await DrawFrameAsync(device, swapchain);
        }

        await CheckUnsubmittedFramesAsync(device, swapchain);
        SurfaceAcquireResult discarded = await swapchain.AcquireNextFrameAsync();
        Require(discarded.Frame != null, "Discard test could not acquire an image.");
        await discarded.Frame!.WaitForReleaseAsync();
        discarded.Frame.Dispose();
        Require(discarded.Frame.Status == SurfaceFrameStatus.Disposed, "An unsubmitted frame was not released.");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await swapchain.AcquireNextFrameAsync(cancellation.Token);
            throw new InvalidOperationException("Acquisition ignored cancellation.");
        }
        catch (OperationCanceledException)
        {
        }

        SwapchainDesc resized = desc with { Width = caps.CurrentWidth ?? 12, Height = caps.CurrentHeight ?? 4 };
        swapchain.Reconfigure(resized);
        Require(swapchain.Configuration == resized, "Swapchain dimensions were implicitly changed.");
        await DrawFrameAsync(device, swapchain);
        swapchain.Dispose();
        swapchain.Dispose();
        Expect<ObjectDisposedException>(() => swapchain.Reconfigure(desc));
        return "Surface checks passed: exact configuration, borrowed images, explicit submit/present, invalidation, discard, resize and pixel readback.";
    }

    private static async Task CheckUnsubmittedFramesAsync(IGraphicDevice device, IGraphicsSwapchain swapchain)
    {
        SurfaceAcquireResult acquired = await swapchain.AcquireNextFrameAsync();
        IGraphicsSurfaceFrame frame = acquired.Frame ?? throw new InvalidOperationException("No image for frame submission validation.");
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new TextureBarrierDesc { Texture = frame.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
        commands.Finish();
        Expect<InvalidOperationException>(() => device.Queue.Submit([commands], frame));
        Require(frame.Status == SurfaceFrameStatus.Acquired && commands.State == CommandBufferState.Executable, "Invalid submission consumed a frame or command.");
        await frame.WaitForReleaseAsync();
        frame.Dispose();
        Expect<ObjectDisposedException>(() => device.Queue.Submit([commands]));
    }

    private static async Task DrawFrameAsync(IGraphicDevice device, IGraphicsSwapchain swapchain)
    {
        SurfaceAcquireResult acquired = await swapchain.AcquireNextFrameAsync();
        Require(acquired.Status is SurfaceStatus.Success or SurfaceStatus.Suboptimal && acquired.Frame != null, "The supplied test target did not produce an image.");
        IGraphicsSurfaceFrame frame = acquired.Frame!;
        Require(frame.Status == SurfaceFrameStatus.Acquired, "New frame state is incorrect.");
        IGraphicsTexture texture = frame.Texture;
        Require(texture.Width == swapchain.Configuration.Width && texture.Height == swapchain.Configuration.Height && texture.MipLevels == 1 && texture.ArrayLayers == 1, "Acquired image dimensions changed.");
        Expect<InvalidOperationException>(texture.Dispose);
        Expect<InvalidOperationException>(() => frame.Present());
        Expect<InvalidOperationException>(() => swapchain.Reconfigure(swapchain.Configuration));
        Expect<InvalidOperationException>(swapchain.Dispose);
        uint alignment = device.GetTextureCopyLayout(texture.Format).BytesPerRowAlignment;
        uint pitch = checked((((texture.Width * 4) + alignment - 1) / alignment) * alignment);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = checked(pitch * texture.Height), Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        IGraphicsTextureView view = texture.CreateView();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        var copy = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
        IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(1, 0, 0, 1) }] });
        render.End();
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = copy });
        commands.CopyTextureToBuffer(new() { Texture = texture, Width = texture.Width, Height = texture.Height }, new() { Buffer = readback.Slice(0, readback.Count), BytesPerRow = pitch, RowsPerImage = texture.Height });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.Slice(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.CopySource, AfterState = TextureState.Present, Before = copy, After = default });
        commands.Finish();
        Expect<InvalidOperationException>(() => device.Queue.Submit([commands]));
        using IGraphicsSubmission submission = device.Queue.Submit([commands], frame);
        Require(frame.Status == SurfaceFrameStatus.Submitted, "Frame submission did not transfer image use.");
        Expect<InvalidOperationException>(() => texture.CreateView());
        Require(frame.Present() is SurfaceStatus.Success or SurfaceStatus.Suboptimal, "Presentation failed.");
        Expect<InvalidOperationException>(() => frame.Present());
        Require(frame.Status == SurfaceFrameStatus.Presented, "Presentation state was not recorded.");
        await frame.WaitForReleaseAsync();
        await submission.WaitAsync();
        Expect<InvalidOperationException>(frame.Dispose);
        await readback.MapAsync();
        byte[] bytes = new byte[checked((int)readback.Count)];
        readback.CopyTo(bytes);
        readback.Unmap();
        byte[] pixel = texture.Format == TextureFormat.Rgba8Unorm ? [255, 0, 0, 255] : [0, 0, 255, 255];
        for (uint y = 0; y < texture.Height; y++)
        {
            for (uint x = 0; x < texture.Width; x++)
            {
                Require(bytes.AsSpan(checked((int)((y * pitch) + (x * 4))), 4).SequenceEqual(pixel), "Presented image clear/readback changed a pixel.");
            }
        }

        view.Dispose();
        commands.Dispose();
        frame.Dispose();
        frame.Dispose();
        Expect<ObjectDisposedException>(() => texture.CreateView());
        Expect<ObjectDisposedException>(() => frame.Present());
    }

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
}
