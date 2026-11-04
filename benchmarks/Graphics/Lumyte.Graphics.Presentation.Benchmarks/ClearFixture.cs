using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Wgpu;

namespace Lumyte.Graphics.Presentation.Benchmarks;

internal static class ClearFixture
{
    internal const uint Width = 1920;

    internal const uint Height = 1080;

    internal static void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView view, long inputId)
    {
        // Exact endpoint colors are independent of sRGB conversion and BGRA/RGBA ordering.
        IRenderEncoder pass = commands.BeginRenderPass(new RenderPassDesc
        {
            ColorAttachments =
            [
                new RenderColorAttachmentDesc
                {
                    View = view,
                    LoadOp = AttachmentLoadOp.Clear,
                    StoreOp = AttachmentStoreOp.Store,
                    ClearValue = (inputId & 1) == 0 ? new(0, 1, 0, 1) : new(1, 0, 0, 1),
                },
            ],
        });
        pass.End();
    }

    internal static void VerifyHeadless(WgpuDevice device, TextureFormat format)
    {
        // Separate offscreen target; no surface readback and no reference renderer dependency.
        using IGraphicsTexture target = device.CreateTexture(new TextureDesc
        {
            Width = Width,
            Height = Height,
            Format = format,
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource,
        });
        using IGraphicsTextureView view = target.CreateView();
        const uint Pitch = 7680; // 1920*4 is already 256-byte aligned.
        const ulong Size = (ulong)Pitch * Height;
        using IGraphicsBuffer<byte> buffer = device.CreateBuffer(new BufferDesc<byte>
        {
            Count = Size,
            Usage = BufferUsage.CopyDestination,
            Memory = MemoryPreference.Readback,
        });
        byte[] bytes = new byte[checked((int)Size)];
        for (long inputId = 1; inputId <= 2; inputId++)
        {
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            Record(commands, view, inputId);
            commands.CopyTextureToBuffer(new TextureCopyRegion { Texture = target, Width = Width, Height = Height }, new BufferTextureCopyLayout
            {
                Buffer = buffer.SliceElements(0, Size),
                BytesPerRow = Pitch,
                RowsPerImage = Height,
            });
            commands.Finish();
            using IGraphicsSubmission submission = device.Queue.Submit([commands]);
            submission.WaitAsync().AsTask().GetAwaiter().GetResult();
            buffer.MapAsync().AsTask().GetAwaiter().GetResult();
            try
            {
                buffer.CopyTo(bytes);
                bool bgra = format is TextureFormat.Bgra8Unorm or TextureFormat.Bgra8Srgb;
                int lit = (inputId & 1) == 0 ? 1 : bgra ? 2 : 0;
                for (int pixel = 0; pixel < bytes.Length; pixel += 4)
                {
                    for (int channel = 0; channel < 4; channel++)
                    {
                        byte expected = channel == lit || channel == 3 ? (byte)255 : (byte)0;
                        if (bytes[pixel + channel] != expected)
                        {
                            throw new InvalidOperationException($"Independent analytic gate mismatch input={inputId}, pixel={pixel / 4}, channel={channel}.");
                        }
                    }
                }
            }
            finally
            {
                buffer.Unmap();
            }
        }
    }
}
