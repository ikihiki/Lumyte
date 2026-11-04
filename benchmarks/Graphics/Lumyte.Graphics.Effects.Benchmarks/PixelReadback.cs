using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Effects.Benchmarks;

internal static class PixelReadback
{
    internal static async Task<byte[]> ReadAsync(IGraphicDevice device, IGraphicsTexture texture)
    {
        TextureCopyLayout layout = device.GetTextureCopyLayout(TextureFormat.Rgba16Float);
        uint rowBytes = checked(texture.Width * layout.BytesPerTexel);
        uint alignment = layout.BytesPerRowAlignment;
        uint pitch = checked(((rowBytes + alignment - 1) / alignment) * alignment);
        using IGraphicsBuffer<byte> buffer = device.CreateBuffer<byte>(new()
        {
            Count = checked((ulong)pitch * texture.Height),
            Usage = BufferUsage.CopyDestination,
            Memory = MemoryPreference.Readback,
        });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());

        // Rendering exported this image to CopySource; readback does not change that state.
        commands.CopyTextureToBuffer(
            new() { Texture = texture, Width = texture.Width, Height = texture.Height },
            new() { Buffer = buffer.SliceElements(0, buffer.Count), BytesPerRow = pitch, RowsPerImage = texture.Height });
        commands.Barrier(new BufferBarrierDesc<byte>
        {
            Buffer = buffer.SliceElements(0, buffer.Count),
            Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite),
            After = new(PipelineStage.Host, ResourceAccess.HostRead),
        });
        commands.Finish();
        IGraphicsSubmission submission;
        try
        {
            submission = device.Queue.Submit([commands]);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Readback submission unconfirmed: {error}");
            Environment.Exit(3);
            throw;
        }

        try
        {
            await submission.WaitAsync();
            if (submission.Status != SubmissionStatus.Completed)
            {
                throw new InvalidOperationException("Readback completion unconfirmed.");
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Readback GPU completion unconfirmed: {error}");
            Environment.Exit(3);
            throw;
        }

        submission.Dispose();
        await buffer.MapAsync();
        try
        {
            byte[] padded = new byte[checked((int)buffer.Count)];
            buffer.CopyTo(padded);
            byte[] packed = new byte[checked((int)(rowBytes * texture.Height))];
            for (int row = 0; row < texture.Height; row++)
            {
                padded.AsSpan(checked(row * (int)pitch), checked((int)rowBytes)).CopyTo(packed.AsSpan(checked(row * (int)rowBytes)));
            }

            return packed;
        }
        finally
        {
            buffer.Unmap();
        }
    }
}
