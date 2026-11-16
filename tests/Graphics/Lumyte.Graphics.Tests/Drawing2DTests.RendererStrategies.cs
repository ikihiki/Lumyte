using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Verifies actual strategy execution through independent direct-target pixel results.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Executes GPU-generated standard path membership and CPU-generated sparse strips on Vulkan.</summary>
    /// <returns>The completed independent readbacks.</returns>
    [GpuFact]
    public async Task VulkanRendererStrategiesAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckRendererStrategiesAsync(device);
    }

    /// <summary>Executes the same correlated coverage contract on native wgpu.</summary>
    /// <returns>The completed independent readbacks.</returns>
    [GpuFact]
    public async Task WgpuRendererStrategiesAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckRendererStrategiesAsync(device);
    }

    private static async Task CheckRendererStrategiesAsync(IGraphicDevice device)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        var path = new PathBuilder2D();
        path.MoveTo(new(0.5f, 0));
        path.LineTo(new(3, 0));
        path.LineTo(new(3, 2));
        path.LineTo(new(0.5f, 2));
        path.Close();
        canvas.FillPath(path.Build(), _red);
        canvas.FillPath(path.Build(), _white);
        DrawList2D list = canvas.End();
        using IRenderer2D standard = Renderer2D.CreateDefault(device);
        using IRenderer2D sparse = new SparseStripRenderer2D(device);
        using IRenderer2D compute = new ComputeRenderer2D(device);
        foreach (IRenderer2D renderer in new[] { standard, sparse, compute })
        {
            using IGraphicsTexture target = device.CreateTexture(new() { Width = 4, Height = 2, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            using IGraphicsTextureView view = target.CreateView();
            var destination = new RenderTarget2D { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = _blue };
            if (ReferenceEquals(renderer, compute) && device.Caps.ArgumentTableBindingLimits.BuffersPerDraw < 10)
            {
                await Assert.ThrowsAsync<NotSupportedException>(async () => await renderer.PrepareAsync(list, destination));
                continue;
            }

            using PreparedDraw2D prepared = await renderer.PrepareAsync(list, destination);
            TextureCopyLayout layout = device.GetTextureCopyLayout(target.Format);
            uint pitch = ((32 + layout.BytesPerRowAlignment - 1) / layout.BytesPerRowAlignment) * layout.BytesPerRowAlignment;
            using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 2, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
            var observed = new PartitionCommands(commands);
            prepared.Record(observed);
            Assert.Equal(ReferenceEquals(renderer, sparse) ? 0 : ReferenceEquals(renderer, standard) ? 1 : 11, observed.ComputePasses);
            commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), After = _copyRead });
            commands.CopyTextureToBuffer(new() { Texture = target, Width = 4, Height = 2 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 2 });
            commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = _copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
            commands.Finish();
            using IGraphicsSubmission submission = device.Queue.Submit([commands]);
            await submission.WaitAsync();
            await readback.MapAsync();
            byte[] bytes = new byte[readback.Count];
            readback.CopyTo(bytes);
            readback.Unmap();
            var pixels = new Vector4[8];
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    int offset = checked((int)((y * pitch) + (x * 8)));
                    pixels[(y * 4) + x] = new(ReadHalf(bytes, offset), ReadHalf(bytes, offset + 2), ReadHalf(bytes, offset + 4), ReadHalf(bytes, offset + 6));
                }
            }

            AssertPixel(pixels, 4, 0, 0, new(0.75f, 0.5f, 0.75f, 1));
            AssertPixel(pixels, 4, 1, 0, Vector4.One);
            AssertPixel(pixels, 4, 3, 0, new(0, 0, 1, 1));
            commands.Dispose();
        }
    }
}
