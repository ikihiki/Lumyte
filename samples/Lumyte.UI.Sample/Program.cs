using System.Numerics;
using System.Text;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.UI;
using static Lumyte.UI.Compose;

if (args is not ["record-only"] and not ["pointer-only"] and not ["vulkan"] and not ["wgpu"])
{
    Console.Error.WriteLine("Usage: dotnet run --project samples/Lumyte.UI.Sample -- record-only|pointer-only|vulkan|wgpu");
    return 1;
}

int clicks = 0;
Definitions.Rectangle accent = Rectangle(width: 72, height: 28, fill: new Color2D(1, 0.3f, 0.1f, 0.7f));
Definitions.Rectangle header = Rectangle(width: 160, height: 24, fill: new Color2D(0.2f, 0.6f, 1, 1));
Definitions.Button button = Button(onClick: () => clicks++)[accent, Button.Background()[Rectangle(fill: new Color2D(0.1f, 0.2f, 0.4f, 1))]];
Definitions.Rectangle overflow = Rectangle(width: 220, height: 200, fill: new Color2D(0.3f, 1, 0.5f, 0.5f));
Definitions.Stack column = Stack(spacing: 12)[header, button, overflow];
Definitions.Panel root = Panel(clipToBounds: true)[column];
using var surface = new UiSurface(root);
if (args is ["pointer-only"])
{
    surface.SetViewport(new(240, 160));
    surface.UpdateLayout();
    surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(10, 40)));
    surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(230, 150)));
    surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(10, 40)));
    surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(10, 40)));
    surface.Dispatch(new UiFocusLostInput());
    Console.WriteLine($"Clicks {clicks}; pressed {((ButtonElement)surface.RootElement.Children[0].Children[1]).IsPressed}");
    return clicks == 1 && !((ButtonElement)surface.RootElement.Children[0].Children[1]).IsPressed ? 0 : 1;
}

using VulkanDevice? vulkan = args[0] == "vulkan" ? VulkanDevice.Create() : null;
using WgpuDevice? wgpu = args[0] == "wgpu" ? WgpuDevice.Create() : null;
IGraphicDevice? device = vulkan is not null ? vulkan : wgpu;
using Renderer2D? renderer = device is null ? null : new(device, DrawingCommandRenderers2D.CreateDefault());
foreach (Vector2 viewport in new[] { new Vector2(240, 160), new Vector2(320, 220) })
{
    surface.SetViewport(viewport);
    var canvas = new Canvas2D();
    canvas.Begin();
    surface.Record(canvas);
    DrawList2D list = canvas.End();
    Console.WriteLine($"Viewport {viewport}; root {surface.RootElement.Size}; accent {surface.RootElement.Children[0].Children[1].Children[0].Bounds}");
    if (device is not null && renderer is not null)
    {
        string path = $"ui-{(int)viewport.X}x{(int)viewport.Y}.ppm";
        await RenderAsync(device, renderer, list, (uint)viewport.X, (uint)viewport.Y, path);
        Console.WriteLine($"Saved {path}");
    }
}

return 0;

static async Task RenderAsync(IGraphicDevice device, Renderer2D renderer, DrawList2D list, uint width, uint height, string path)
{
    using IGraphicsTexture target = device.CreateTexture(new() { Width = width, Height = height, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
    using IGraphicsTextureView view = target.CreateView();
    using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0.04f, 0.04f, 0.06f, 1) });
    uint alignment = device.GetTextureCopyLayout(target.Format).BytesPerRowAlignment;
    uint pitch = (((width * 4) + alignment - 1) / alignment) * alignment;
    using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * height, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
    using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
    var colorScope = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
    var copyRead = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
    var copyWrite = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite);
    commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = colorScope });
    prepared.Record(commands);
    commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = colorScope, After = copyRead });
    commands.CopyTextureToBuffer(new() { Texture = target, Width = width, Height = height }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = height });
    commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
    commands.Finish();
    using IGraphicsSubmission submission = device.Queue.Submit([commands]);
    await submission.WaitAsync();
    await readback.MapAsync();
    byte[] bytes = new byte[checked((int)readback.Count)];
    readback.CopyTo(bytes);
    readback.Unmap();

    // GPU work is complete before command buffers, prepared batches, views and textures are returned.
    using FileStream output = File.Create(path);
    await output.WriteAsync(Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
    byte[] rgb = new byte[checked((int)(width * height * 3))];
    for (uint y = 0; y < height; y++)
    {
        for (uint x = 0; x < width; x++)
        {
            bytes.AsSpan(checked((int)((y * pitch) + (x * 4))), 3).CopyTo(rgb.AsSpan(checked((int)(((y * width) + x) * 3)), 3));
        }
    }

    await output.WriteAsync(rgb);
}
