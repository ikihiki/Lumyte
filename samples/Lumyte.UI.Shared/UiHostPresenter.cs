using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.SampleSupport;

internal sealed class UiHostPresenter : IDisposable
{
    private readonly IGraphicDevice _device;
    private readonly Renderer2D _renderer;
    private readonly IGraphicsShader _vertex;
    private readonly IGraphicsShader _fragment;
    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsSampler _sampler;

    internal UiHostPresenter(IGraphicDevice device)
    {
        _device = device;
        _renderer = new(device, DrawingCommandRenderers2D.CreateDefault());
        _vertex = device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(UiHostPresenter).Assembly, "Lumyte.UI.Sample.present-vertex.lshader"));
        _fragment = device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(UiHostPresenter).Assembly, "Lumyte.UI.Sample.present-fragment.lshader"));
        _pipeline = device.CreateGraphicsPipeline(new() { VertexShader = _vertex, FragmentShader = _fragment });
        _sampler = device.CreateSampler(new());
    }

    public void Dispose()
    {
        _sampler.Dispose();
        _pipeline.Dispose();
        _fragment.Dispose();
        _vertex.Dispose();
        _renderer.Dispose();
    }

    internal static SwapchainDesc Configuration(IGraphicsSurface surface, int width, int height)
    {
        SurfaceCapabilities caps = surface.GetCapabilities();
        TextureFormat format = caps.Formats.Contains(TextureFormat.Bgra8Unorm) ? TextureFormat.Bgra8Unorm : TextureFormat.Rgba8Unorm;
        if (!caps.Formats.Contains(format))
        {
            throw new NotSupportedException("The sample presenter requires an unorm swapchain.");
        }

        return new() { Width = checked((uint)width), Height = checked((uint)height), Format = format };
    }

    internal async Task DrawAsync(IGraphicsSwapchain swapchain, DrawList2D list)
    {
        using IGraphicsSemaphore acquired = _device.CreateSemaphore();
        using IGraphicsSemaphore rendered = _device.CreateSemaphore();
        SurfaceAcquireResult result = await swapchain.AcquireNextFrameAsync(acquired);
        if (result.Frame is not IGraphicsSurfaceFrame frame)
        {
            if (result.Status is SurfaceStatus.Timeout or SurfaceStatus.Outdated)
            {
                return;
            }

            throw new InvalidOperationException($"Cannot acquire a UI frame: {result.Status}.");
        }

        bool released = false;
        try
        {
            using IGraphicsTexture target = _device.CreateTexture(new() { Width = frame.Texture.Width, Height = frame.Texture.Height, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled });
            using IGraphicsTextureView targetView = target.CreateView();
            using PreparedDraw2D prepared = await _renderer.PrepareAsync(list, new() { View = targetView, PixelScale = 1, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0.04f, 0.04f, 0.06f, 1) });
            using IArgumentTable table = _device.CreateArgumentTable(new() { TextureCapacity = 1, SamplerCapacity = 1 });
            var arguments = new UiPresentArguments(table.WriteTexture(0, targetView), table.WriteSampler(0, _sampler), new(target.Width, target.Height));
            using IGraphicsTextureView display = frame.Texture.CreateView();
            using IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new());
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
            var sample = new BarrierScope(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
            try
            {
                commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
                prepared.Record(commands);
                commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.Sampled, Before = color, After = sample });
                commands.Barrier(new TextureBarrierDesc { Texture = frame.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
                IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = display, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
                render.SetPipeline(_pipeline);
                render.SetArgumentTable(table);
                render.SetRenderState(new() { ColorTargets = [new()], Rasterization = new() { Cull = CullMode.None } });
                render.SetViewport(new(0, 0, target.Width, target.Height, 0, 1));
                render.SetScissor(new(0, 0, target.Width, target.Height));
                render.SetArguments(in arguments);
                render.Draw(3);
                render.End();
                commands.Barrier(new TextureBarrierDesc { Texture = frame.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.Present, Before = color, After = default });
                commands.Finish();
                using IGraphicsSubmission submission = _device.Queue.Submit(new QueueSubmitDesc { CommandBuffers = [commands], WaitSemaphores = [new() { Semaphore = acquired, Stages = PipelineStage.AllCommands }], SignalSemaphores = [rendered] });
                try
                {
                    SurfaceStatus status = frame.Present([rendered]);
                    if (status is not (SurfaceStatus.Success or SurfaceStatus.Suboptimal))
                    {
                        throw new InvalidOperationException($"UI presentation failed: {status}.");
                    }
                }
                finally
                {
                    await submission.WaitAsync();
                }
            }
            finally
            {
                // Keep the acquired image and every referenced resource alive through release.
                await frame.WaitForReleaseAsync();
                display.Dispose();
                commands.Dispose();
                frame.Dispose();
                released = true;
            }
        }
        finally
        {
            if (!released)
            {
                await frame.WaitForReleaseAsync();
                frame.Dispose();
            }
        }
    }
}
