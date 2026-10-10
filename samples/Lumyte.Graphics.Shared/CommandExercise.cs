using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Exercises GPU copies, explicit dependencies and pass scopes through common APIs.</summary>
public static class CommandExercise
{
    private static readonly BarrierScope _hostWrite = new(PipelineStage.Host, ResourceAccess.HostWrite);
    private static readonly BarrierScope _hostRead = new(PipelineStage.Host, ResourceAccess.HostRead);
    private static readonly BarrierScope _copyRead = new(PipelineStage.Copy, ResourceAccess.CopyRead);
    private static readonly BarrierScope _copyWrite = new(PipelineStage.Copy, ResourceAccess.CopyWrite);
    private static readonly BarrierScope _colorWrite = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);

    /// <summary>Verifies upload, partial copies, padded layered texture copies, clear and command lifetime.</summary>
    /// <param name="device">The already created backend device.</param>
    /// <returns>The report after exact GPU readback and rejection checks succeed.</returns>
    public static async Task<string> RunAsync(IGraphicDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        CheckReleasedResource(device);
        await CheckBuffersAsync(device);
        foreach (TextureFormat format in Enum.GetValues<TextureFormat>())
        {
            if (format is not (TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8))
            {
                await CheckTexturesAsync(device, format);
                await CheckClearAsync(device, format);
            }
        }

        return "Command checks passed: GPU buffer copy, padded mip/layer copies, clear, pass scope and submission.";
    }

    private static void CheckReleasedResource(IGraphicDevice device)
    {
        using IGraphicsBuffer<uint> source = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.CopySource });
        using IGraphicsBuffer<uint> destination = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.CopyDestination });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.CopyBuffer(source.Slice(0, 4), destination.Slice(0, 4));
        commands.Finish();
        source.Dispose();
        Expect<ObjectDisposedException>(() => device.Queue.Submit([commands]));
        Require(commands.State == CommandBufferState.Executable, "Input failure changed the command state.");
    }

    private static async Task CheckBuffersAsync(IGraphicDevice device)
    {
        uint[] values = [11, 22, 33, 44, 55, 66, 77, 88];
        using IGraphicsBuffer<uint> upload = device.CreateBuffer<uint>(new() { Count = 8, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        using IGraphicsBuffer<uint> gpu = device.CreateBuffer<uint>(new() { Count = 8, Usage = BufferUsage.CopySource | BufferUsage.CopyDestination });
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        Expect<InvalidOperationException>(() => device.Queue.Submit([commands]));
        Expect<ArgumentException>(() => device.Queue.Submit([]));
        await upload.MapAsync();
        upload.CopyFrom(values);
        Expect<InvalidOperationException>(() => commands.CopyBuffer(upload.Slice(0, 8), gpu.Slice(0, 8)));
        upload.Unmap();
        Expect<ArgumentException>(() => commands.Barrier(new MemoryBarrierDesc { Before = new(PipelineStage.Host, ResourceAccess.CopyRead), After = _copyRead }));
        Expect<ArgumentException>(() => commands.Barrier(new BufferBarrierDesc<uint> { Buffer = upload.Slice(0, 8), Before = _copyRead, After = _copyWrite }));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = upload.Slice(0, 8), Before = _hostWrite, After = _copyRead });
        Expect<ArgumentException>(() => commands.CopyBuffer(upload.Slice(0, 8), readback.Slice(0, 4)));
        Expect<ArgumentException>(() => commands.CopyBuffer(gpu.Slice(0, 4), gpu.Slice(4, 4)));
        commands.CopyBuffer(upload.Slice(0, 8), gpu.Slice(0, 8));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = gpu.Slice(0, 8), Before = _copyWrite, After = _copyRead });
        commands.CopyBuffer(gpu.Slice(2, 4), readback.Slice(0, 4));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = readback.Slice(0, 4), Before = _copyWrite, After = _hostRead });
        IComputeEncoder compute = commands.BeginComputePass(new());
        Expect<InvalidOperationException>(commands.Finish);
        Expect<InvalidOperationException>(() => commands.CopyBuffer(upload.Slice(0, 8), gpu.Slice(0, 8)));
        compute.End();
        Expect<InvalidOperationException>(compute.End);
        commands.Finish();
        Require(commands.State == CommandBufferState.Executable, "Finish did not finalize recording.");
        Expect<InvalidOperationException>(commands.Finish);
        Expect<ArgumentException>(() => device.Queue.Submit([commands, commands]));
        await upload.MapAsync();
        Expect<InvalidOperationException>(() => device.Queue.Submit([commands]));
        upload.Unmap();
        var submitted = new List<IGraphicsCommandBuffer> { commands };
        using IGraphicsSubmission submission = device.Queue.Submit(submitted);
        submitted.Clear();

        // Completion is acknowledged by Status or WaitAsync; observing Pending cannot freeze GPU progress.
        Require(commands.State == CommandBufferState.Submitted, "Submit did not mark the commands as submitted.");
        Expect<InvalidOperationException>(commands.Dispose);
        Expect<InvalidOperationException>(() => device.Queue.Submit([commands]));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            await submission.WaitAsync(canceled.Token);
            throw new InvalidOperationException("A canceled wait was accepted.");
        }
        catch (OperationCanceledException)
        {
            // Waiting is canceled without canceling the GPU submission.
        }

        await submission.WaitAsync();
        Require(submission.Status == SubmissionStatus.Completed && commands.State == CommandBufferState.Completed, "Submission did not complete.");
        await readback.MapAsync();
        uint[] actual = new uint[4];
        readback.CopyTo(actual);
        readback.Unmap();
        Require(actual.AsSpan().SequenceEqual(values.AsSpan(2, 4)), "Partial buffer GPU copy changed the bytes.");
    }

    private static async Task CheckTexturesAsync(IGraphicDevice device, TextureFormat format)
    {
        TextureCopyLayout constraints = device.GetTextureCopyLayout(format);
        uint rowBytes = 3 * constraints.BytesPerTexel;
        uint pitch = checked(((rowBytes + constraints.BytesPerRowAlignment - 1) / constraints.BytesPerRowAlignment) * constraints.BytesPerRowAlignment);
        uint rows = 3;
        uint offset = checked((uint)constraints.BufferOffsetAlignmentInBytes);
        uint bytes = checked(offset + (pitch * rows * 2));
        byte[] expected = new byte[bytes];
        for (uint layer = 0; layer < 2; layer++)
        {
            for (uint y = 0; y < 2; y++)
            {
                for (uint x = 0; x < rowBytes; x++)
                {
                    expected[offset + (layer * rows * pitch) + (y * pitch) + x] = checked((byte)(1 + (layer * 50) + (y * rowBytes) + x));
                }
            }
        }

        using IGraphicsBuffer<byte> upload = device.CreateBuffer<byte>(new() { Count = bytes, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = bytes, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        var desc = new TextureDesc { Width = 8, Height = 8, MipLevels = 2, ArrayLayers = 3, Format = format, Usage = TextureUsage.CopySource | TextureUsage.CopyDestination };
        using IGraphicsTexture source = device.CreateTexture(desc);
        using IGraphicsTexture destination = device.CreateTexture(desc);
        var sourceRegion = new TextureCopyRegion { Texture = source, MipLevel = 1, OriginX = 1, OriginY = 1, Width = 3, Height = 2, BaseArrayLayer = 1, ArrayLayerCount = 2 };
        TextureCopyRegion destinationRegion = sourceRegion with { Texture = destination, OriginX = 0, OriginY = 0 };
        var sourceLayout = new BufferTextureCopyLayout { Buffer = upload.Slice(offset, bytes - offset), BytesPerRow = pitch, RowsPerImage = rows };
        BufferTextureCopyLayout destinationLayout = sourceLayout with { Buffer = readback.Slice(offset, bytes - offset) };
        await upload.MapAsync();
        upload.CopyFrom(expected);
        upload.Unmap();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        Expect<ArgumentException>(() => commands.Barrier(new TextureBarrierDesc { Texture = source, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.Present, Before = default, After = default }));
        Expect<InvalidOperationException>(() => commands.CopyBufferToTexture(sourceLayout, sourceRegion));
        Transition(commands, source, new(1, 1, 1, 2), TextureState.Undefined, TextureState.CopyDestination, default, _copyWrite);
        Transition(commands, destination, new(1, 1, 1, 2), TextureState.Undefined, TextureState.CopyDestination, default, _copyWrite);
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = upload.Slice(0, bytes), Before = _hostWrite, After = _copyRead });
        Expect<ArgumentException>(() => commands.CopyBufferToTexture(sourceLayout with { BytesPerRow = 1 }, sourceRegion));
        if (format == TextureFormat.Rgba16Float)
        {
            Expect<ArgumentException>(() => commands.CopyBufferToTexture(sourceLayout with { Buffer = upload.Slice(4, bytes - 4) }, sourceRegion));
            Expect<ArgumentException>(() => commands.CopyBufferToTexture(sourceLayout with { BytesPerRow = pitch + 4 }, sourceRegion));
        }

        commands.CopyBufferToTexture(sourceLayout, sourceRegion);
        Transition(commands, source, new(1, 1, 1, 2), TextureState.CopyDestination, TextureState.CopySource, _copyWrite, _copyRead);
        commands.CopyTexture(sourceRegion, destinationRegion);
        Transition(commands, destination, new(1, 1, 1, 2), TextureState.CopyDestination, TextureState.CopySource, _copyWrite, _copyRead);
        commands.CopyTextureToBuffer(destinationRegion, destinationLayout);
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.Slice(0, bytes), Before = _copyWrite, After = _hostRead });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        byte[] actual = new byte[bytes];
        readback.CopyTo(actual);
        readback.Unmap();
        for (uint layer = 0; layer < 2; layer++)
        {
            for (uint y = 0; y < 2; y++)
            {
                int start = checked((int)(offset + (layer * rows * pitch) + (y * pitch)));
                Require(actual.AsSpan(start, checked((int)rowBytes)).SequenceEqual(expected.AsSpan(start, checked((int)rowBytes))), "Layered partial texture GPU copy changed the texels.");
            }
        }
    }

    private static async Task CheckClearAsync(IGraphicDevice device, TextureFormat format)
    {
        TextureCopyLayout layout = device.GetTextureCopyLayout(format);
        uint pitch = Math.Max(layout.BytesPerRowAlignment, 4 * layout.BytesPerTexel);
        byte[] pixel = format switch
        {
            TextureFormat.R8Unorm => [255],
            TextureFormat.Rg8Unorm => [255, 0],
            TextureFormat.R16Float => [0, 60],
            TextureFormat.Rg16Float => [0, 60, 0, 0],
            TextureFormat.Rgba16Float => [0, 60, 0, 0, 0, 0, 0, 60],
            TextureFormat.Rgb10A2Unorm => [255, 3, 0, 192],
            TextureFormat.Bgra8Unorm or TextureFormat.Bgra8Srgb => [0, 0, 255, 255],
            _ => [255, 0, 0, 255],
        };
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 4, Height = 3, Format = format, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = texture.CreateView();
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 3, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        Transition(commands, texture, new(0, 1, 0, 1), TextureState.Undefined, TextureState.ColorAttachment, default, _colorWrite);
        IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(1, 0, 0, 1) }] });
        Expect<InvalidOperationException>(() => commands.Barrier(new MemoryBarrierDesc { Before = _colorWrite, After = _copyRead }));
        render.End();
        Expect<InvalidOperationException>(render.End);
        Transition(commands, texture, new(0, 1, 0, 1), TextureState.ColorAttachment, TextureState.CopySource, _colorWrite, _copyRead);
        commands.CopyTextureToBuffer(new() { Texture = texture, Width = 4, Height = 3 }, new() { Buffer = readback.Slice(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 3 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.Slice(0, readback.Count), Before = _copyWrite, After = _hostRead });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        byte[] actual = new byte[checked((int)readback.Count)];
        readback.CopyTo(actual);
        readback.Unmap();
        for (uint y = 0; y < 3; y++)
        {
            for (uint x = 0; x < 4; x++)
            {
                uint index = (y * pitch) + (x * layout.BytesPerTexel);
                Require(actual.AsSpan(checked((int)index), pixel.Length).SequenceEqual(pixel), "Render pass clear/store did not preserve the exact red pixels.");
            }
        }
    }

    private static void Transition(IGraphicsCommandBuffer commands, IGraphicsTexture texture, TextureSubresourceRange range, TextureState before, TextureState after, BarrierScope beforeScope, BarrierScope afterScope) =>
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = range, BeforeState = before, AfterState = after, Before = beforeScope, After = afterScope });

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
