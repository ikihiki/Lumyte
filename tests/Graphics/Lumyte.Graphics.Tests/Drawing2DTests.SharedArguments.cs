using System.Buffers.Binary;
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Exercises shared-table retained ownership on actual backend submissions.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks depth one/three, reverse retirement and analytic mixed-pipeline pixels on Vulkan.</summary>
    /// <returns>The asynchronous GPU verification.</returns>
    [GpuFact]
    public async Task VulkanSharedArgumentRetainedFramesAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckSharedArgumentFramesAsync(device, 1);
        await CheckSharedArgumentFramesAsync(device, 3);
        using var foreign = VulkanDevice.Create();
        using IGraphicsTexture texture = foreign.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled });
        using IGraphicsTextureView view = texture.CreateView();
        using var plan = new DrawingArgumentPlan2D(device);
        DrawingArgumentRegistration2D<IGraphicsTextureView> reference = plan.RegisterTexture(view);
        await Assert.ThrowsAsync<ArgumentException>(async () => await plan.SealAsync());
        Assert.Throws<InvalidOperationException>(() => reference.Reference);
    }

    /// <summary>Checks the same retained ownership and output on the portable backend.</summary>
    /// <returns>The asynchronous GPU verification.</returns>
    [GpuFact]
    public async Task WgpuSharedArgumentRetainedFramesAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckSharedArgumentFramesAsync(device, 1);
        await CheckSharedArgumentFramesAsync(device, 3);
    }

    private static async Task CheckSharedArgumentFramesAsync(IGraphicDevice device, int depth)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(1, 1, 5, 5), new Color2D(1, 0, 0, 0.5f));
        canvas.Save();
        canvas.ClipRect(new(3, 1, 4, 5));
        canvas.FillPath(Rectangle(2, 1, 5, 5), new Color2D(0, 0, 1, 0.5f));
        canvas.Restore();
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, canvas.End());
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { MaxFramesInFlight = depth });
        var slots = new List<SharedArgumentFrame>();
        try
        {
            for (int i = 0; i < depth; i++)
            {
                var slot = new SharedArgumentFrame(device);
                slots.Add(slot);
                using DrawingSceneReadLease2D read = scene.Borrow();
                slot.Frame = await renderer.PrepareAsync(read, new() { View = slot.View, LoadOp = AttachmentLoadOp.Clear });
                var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
                slot.Commands.Barrier(new TextureBarrierDesc { Texture = slot.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = attachment });
                slot.Frame.Record(slot.Commands);
                slot.Commands.Barrier(new TextureBarrierDesc { Texture = slot.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = attachment, After = _copyRead });
                slot.Commands.CopyTextureToBuffer(new() { Texture = slot.Texture, Width = 8, Height = 8 }, new() { Buffer = slot.Readback.SliceElements(0, slot.Readback.Count), BytesPerRow = slot.Pitch, RowsPerImage = 8 });
                slot.Commands.Barrier(new BufferBarrierDesc<byte> { Buffer = slot.Readback.SliceElements(0, slot.Readback.Count), Before = _copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
                slot.Commands.Finish();
                slot.Submission = device.Queue.Submit([slot.Commands]);
                slot.Frame.AttachSubmission(slot.Submission);
            }

            for (int i = slots.Count - 1; i >= 0; i--)
            {
                SharedArgumentFrame slot = slots[i];
                await slot.Submission!.WaitAsync();
                slot.Commands.Dispose();
                slot.Frame!.Retire();
                slot.Retired = true;
                await slot.Readback.MapAsync();
                byte[] bytes = new byte[checked((int)slot.Readback.Count)];
                slot.Readback.CopyTo(bytes);
                slot.Readback.Unmap();
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        Vector4 expected = Vector4.Zero;
                        if (y >= 1 && y < 6)
                        {
                            if (x >= 1 && x < 6)
                            {
                                expected = new(0.5f, 0, 0, 0.5f);
                            }

                            if (x >= 3 && x < 7)
                            {
                                expected = new Vector4(0, 0, 0.5f, 0.5f) + (expected * 0.5f);
                            }
                        }

                        int offset = checked((int)((uint)y * slot.Pitch) + (x * 8));
                        var actual = new Vector4(HalfAt(0), HalfAt(2), HalfAt(4), HalfAt(6));
                        Assert.InRange(Vector4.Distance(expected, actual), 0, 0.002f);
                        float HalfAt(int component) => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + component, 2)));
                    }
                }
            }
        }
        finally
        {
            foreach (SharedArgumentFrame slot in slots)
            {
                slot.Dispose();
            }
        }
    }

    private sealed class SharedArgumentFrame : IDisposable
    {
        internal SharedArgumentFrame(IGraphicDevice device)
        {
            Texture = device.CreateTexture(new() { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            View = Texture.CreateView();
            TextureCopyLayout layout = device.GetTextureCopyLayout(TextureFormat.Rgba16Float);
            Pitch = ((64 + layout.BytesPerRowAlignment - 1) / layout.BytesPerRowAlignment) * layout.BytesPerRowAlignment;
            Readback = device.CreateBuffer<byte>(new() { Count = Pitch * 8, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
            Commands = device.CreateCommandBuffer(new());
        }

        internal IGraphicsTexture Texture { get; }

        internal IGraphicsTextureView View { get; }

        internal IGraphicsBuffer<byte> Readback { get; }

        internal IGraphicsCommandBuffer Commands { get; }

        internal uint Pitch { get; }

        internal PreparedDrawingFrame2D? Frame { get; set; }

        internal IGraphicsSubmission? Submission { get; set; }

        internal bool Retired { get; set; }

        public void Dispose()
        {
            if (Submission is not null)
            {
                Submission.WaitAsync().GetAwaiter().GetResult();
                Commands.Dispose();
                if (!Retired)
                {
                    Frame!.Retire();
                    Retired = true;
                }

                Submission.Dispose();
            }
            else
            {
                Commands.Dispose();
                Frame?.Dispose();
            }

            Frame?.Dispose();
            Readback.Dispose();
            View.Dispose();
            Texture.Dispose();
        }
    }
}
