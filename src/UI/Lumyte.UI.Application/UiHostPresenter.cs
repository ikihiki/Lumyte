using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.UI.Application;

internal sealed partial class UiHostPresenter : IDisposable
{
    private readonly IGraphicDevice _device;
    private readonly Color2D _background;
    private readonly Renderer2D _renderer;
    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsSampler _sampler;
    private readonly Stack<IDisposable> _resources = [];
    private readonly List<Frame> _frames = [];
    private TextureFormat? _intermediate;

    internal UiHostPresenter(IGraphicDevice device, Color2D background)
    {
        _device = device;
        _background = background;
        try
        {
            var services = new ServiceCollection();
            services.UseGraphics2D();
            ServiceProvider provider = Own(services.BuildServiceProvider());
            IServiceScope scope = Own(provider.CreateScope());
            _renderer = Own(new Renderer2D(device, scope.ServiceProvider.GetRequiredService<DrawingCommandRenderers2D>()));
            IGraphicsShader vertex = Own(device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(UiHostPresenter).Assembly, "Lumyte.UI.Application.present-vertex.lshader")));
            IGraphicsShader fragment = Own(device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(UiHostPresenter).Assembly, "Lumyte.UI.Application.present-fragment.lshader")));
            _pipeline = Own(device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment }));
            _sampler = Own(device.CreateSampler(new()));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_frames.Count != 0)
        {
            throw new InvalidOperationException("Drain submitted frames before disposing the presenter.");
        }

        while (_resources.TryPeek(out IDisposable? resource))
        {
            resource.Dispose();
            _resources.Pop();
        }
    }

    internal static SwapchainDesc Configuration(IGraphicsSurface surface, int width, int height)
    {
        SurfaceCapabilities caps = surface.GetCapabilities();
        TextureFormat? selected = null;
        foreach (TextureFormat candidate in caps.Formats)
        {
            if (candidate is TextureFormat.Bgra8Srgb or TextureFormat.Rgba8Srgb)
            {
                selected = candidate;
                break;
            }

            if (selected is null && candidate is TextureFormat.Bgra8Unorm or TextureFormat.Rgba8Unorm)
            {
                selected = candidate;
            }
        }

        if (selected is not { } format || !caps.AlphaModes.Contains(SurfaceAlphaMode.Opaque))
        {
            throw new NotSupportedException("The target cannot present opaque sRGB content.");
        }

        return new() { Width = checked((uint)width), Height = checked((uint)height), Format = format, Usage = TextureUsage.RenderAttachment | (caps.SupportedUsage & TextureUsage.CopySource), AlphaMode = SurfaceAlphaMode.Opaque };
    }

    internal async ValueTask PrepareCandidateAsync(DrawList2D commands, uint width, uint height)
    {
        using IGraphicsTexture target = CreateIntermediate(width, height);
        using IGraphicsTextureView view = target.CreateView();
        using PreparedDraw2D prepared = await _renderer.PrepareAsync(commands, new() { View = view, PixelScale = 1, LoadOp = AttachmentLoadOp.Clear, ClearColor = _background });
    }

    internal async Task<SurfaceStatus> DrawAsync(IGraphicsSwapchain swapchain, DrawList2D list, Guid owner, Action<UiFramePixels>? capture = null, IReadOnlyList<UiVisualSnapshot>? visuals = null, Action? submitted = null)
    {
        var frame = new Frame(owner);
        if (visuals is not null)
        {
            foreach (UiVisualSnapshot visual in visuals)
            {
                frame.Own(visual.Retain());
            }
        }

        _frames.Add(frame);
        Exception? failure = null;
        try
        {
            IGraphicsTexture target = frame.Own(CreateIntermediate(swapchain.Configuration.Width, swapchain.Configuration.Height));
            bool captureDisplay = (swapchain.Configuration.Usage & TextureUsage.CopySource) != 0;
            TextureFormat readbackFormat = captureDisplay ? swapchain.Configuration.Format : target.Format;
            uint pitch = 0;
            IGraphicsBuffer<byte>? readback = null;
            if (capture is not null)
            {
                if (!captureDisplay && target.Format != TextureFormat.Rgba8Srgb)
                {
                    throw new NotSupportedException("Screenshot readback requires an sRGB RGBA8 intermediate.");
                }

                uint alignment = _device.GetTextureCopyLayout(readbackFormat).BytesPerRowAlignment;
                pitch = checked(((((target.Width * 4) + alignment) - 1) / alignment) * alignment);
                readback = frame.Own(_device.CreateBuffer<byte>(new() { Count = checked(pitch * target.Height), Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback }));
            }

            IGraphicsTextureView targetView = frame.Own(target.CreateView());
            PreparedDraw2D prepared = frame.Own(await _renderer.PrepareAsync(list, new() { View = targetView, PixelScale = 1, LoadOp = AttachmentLoadOp.Clear, ClearColor = _background }));
            IArgumentTable table = frame.Own(_device.CreateArgumentTable(new() { TextureCapacity = 1, SamplerCapacity = 1 }));
            uint encode = swapchain.Configuration.Format is TextureFormat.Bgra8Srgb or TextureFormat.Rgba8Srgb ? 0u : 1u;
            var arguments = new UiPresentArguments(table.WriteTexture(0, targetView), table.WriteSampler(0, _sampler), new(target.Width, target.Height), encode);

            // Browser canvas images expire with the rendering turn. Complete every asynchronous preparation before acquisition.
            IGraphicsSemaphore acquired = frame.Own(_device.CreateSemaphore());
            IGraphicsSemaphore rendered = frame.Own(_device.CreateSemaphore());
            SurfaceAcquireResult result = await swapchain.AcquireNextFrameAsync(acquired);
            if (result.Frame is not IGraphicsSurfaceFrame image)
            {
                return result.Status;
            }

            frame.Image = image;
            if (image.Texture.Width != target.Width || image.Texture.Height != target.Height)
            {
                return SurfaceStatus.Outdated;
            }

            IGraphicsTextureView display = frame.Own(image.Texture.CreateView());
            IGraphicsCommandBuffer commands = frame.Own(_device.CreateCommandBuffer(new()));
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
            var sample = new BarrierScope(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
            commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
            prepared.Record(commands);
            commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.Sampled, Before = color, After = sample });
            commands.Barrier(new TextureBarrierDesc { Texture = image.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
            IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = display, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
            try
            {
                render.SetPipeline(_pipeline);
                render.SetArgumentTable(table);
                render.SetRenderState(new() { ColorTargets = [new()], Rasterization = new() { Cull = CullMode.None } });
                render.SetViewport(new(0, 0, target.Width, target.Height, 0, 1));
                render.SetScissor(new(0, 0, target.Width, target.Height));
                render.SetBlendConstant(new(0, 0, 0, 0));
                render.SetStencilReference(0);
                render.SetArguments(in arguments);
                render.Draw(3);
            }
            finally
            {
                render.End();
            }

            TextureState imageState = TextureState.ColorAttachment;
            BarrierScope imageAccess = color;
            if (readback is not null)
            {
                var copy = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
                IGraphicsTexture copied = captureDisplay ? image.Texture : target;
                commands.Barrier(new TextureBarrierDesc { Texture = copied, Range = new(0, 1, 0, 1), BeforeState = captureDisplay ? TextureState.ColorAttachment : TextureState.Sampled, AfterState = TextureState.CopySource, Before = captureDisplay ? color : sample, After = copy });
                commands.CopyTextureToBuffer(new() { Texture = copied, Width = copied.Width, Height = copied.Height }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = copied.Height });
                commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
                if (captureDisplay)
                {
                    imageState = TextureState.CopySource;
                    imageAccess = copy;
                }
            }

            commands.Barrier(new TextureBarrierDesc { Texture = image.Texture, Range = new(0, 1, 0, 1), BeforeState = imageState, AfterState = TextureState.Present, Before = imageAccess, After = default });
            commands.Finish();
            frame.Submission = _device.Queue.Submit(new QueueSubmitDesc() { CommandBuffers = [commands], WaitSemaphores = [new() { Semaphore = acquired, Stages = PipelineStage.AllCommands }], SignalSemaphores = [rendered] });
            SurfaceStatus status = image.Present([rendered]);
            if (status == SurfaceStatus.Success && result.Status == SurfaceStatus.Suboptimal)
            {
                status = SurfaceStatus.Suboptimal;
            }

            submitted?.Invoke();
            await frame.DrainAsync();
            if (frame.IsRetired)
            {
                throw new InvalidOperationException("The submitted device generation was retired.");
            }

            if (readback is not null && status is SurfaceStatus.Success or SurfaceStatus.Suboptimal)
            {
                await readback.MapAsync(frame.RetirementToken);
                byte[] padded = new byte[checked((int)readback.Count)];
                try
                {
                    readback.CopyTo(padded);
                }
                finally
                {
                    readback.Unmap();
                }

                byte[] pixels = new byte[checked((int)(target.Width * target.Height * 4))];
                for (int row = 0; row < target.Height; row++)
                {
                    padded.AsSpan(checked(row * (int)pitch), checked((int)target.Width * 4)).CopyTo(pixels.AsSpan(checked(row * (int)target.Width * 4)));
                }

                if (readbackFormat is TextureFormat.Bgra8Unorm or TextureFormat.Bgra8Srgb)
                {
                    for (int index = 0; index < pixels.Length; index += 4)
                    {
                        (pixels[index], pixels[index + 2]) = (pixels[index + 2], pixels[index]);
                    }
                }

                capture!(new(target.Width, target.Height, pixels));
            }

            return status;
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            // Failed waits retain the frame and all children for retry or confirmed device retirement.
            try
            {
                await frame.DrainAsync();
                frame.Dispose();
                _frames.Remove(frame);
            }
            catch (Exception cleanup) when (failure is not null)
            {
                throw new AggregateException(failure, cleanup);
            }
            finally
            {
                frame.Finished.TrySetResult();
            }
        }
    }

    internal async ValueTask DrainAsync(Guid owner)
    {
        foreach (Frame frame in _frames.Where(frame => frame.Owner == owner).ToArray())
        {
            await frame.Finished.Task;
            await frame.DrainAsync();
            frame.Dispose();
            _frames.Remove(frame);
        }
    }

    internal async ValueTask ReleaseAfterDeviceRetirementAsync()
    {
        Frame[] frames = _frames.ToArray();
        foreach (Frame frame in frames)
        {
            frame.ConfirmDeviceRetirement();
        }

        foreach (Frame frame in frames)
        {
            await frame.Finished.Task;
            frame.Dispose();
            _frames.Remove(frame);
        }
    }

    private T Own<T>(T resource)
        where T : IDisposable
    {
        _resources.Push(resource);
        return resource;
    }

    private IGraphicsTexture CreateIntermediate(uint width, uint height)
    {
        var description = new TextureDesc { Width = width, Height = height, Format = _intermediate ?? TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled | TextureUsage.CopySource };
        try
        {
            IGraphicsTexture texture = _device.CreateTexture(description);
            _intermediate = description.Format;
            return texture;
        }
        catch (NotSupportedException) when (_intermediate is null)
        {
            _intermediate = TextureFormat.Rgba16Float;
            return _device.CreateTexture(description with { Format = TextureFormat.Rgba16Float });
        }
    }

    internal sealed class Frame(Guid owner) : IDisposable
    {
        private readonly Stack<IDisposable> _resources = [];
        private readonly CancellationTokenSource _retirement = new();
        private bool _submitted;
        private bool _released;
        private bool _retired;

        internal Guid Owner { get; } = owner;

        internal bool IsRetired => _retired;

        internal CancellationToken RetirementToken => _retirement.Token;

        internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal IGraphicsSurfaceFrame? Image { get; set; }

        internal IGraphicsSubmission? Submission { get; set; }

        public void Dispose()
        {
            if (!_retired && ((Submission is not null && !_submitted) || (Image is not null && !_released)))
            {
                throw new InvalidOperationException("Confirm submission and presentation completion before releasing frame resources.");
            }

            while (_resources.TryPeek(out IDisposable? resource))
            {
                resource.Dispose();
                _resources.Pop();
            }

            Submission?.Dispose();
            Submission = null;
            Image?.Dispose();
            Image = null;
            _retirement.Dispose();
        }

        internal void ConfirmDeviceRetirement()
        {
            if (_retired)
            {
                return;
            }

            _retired = true;
            _retirement.Cancel();
        }

        internal T Own<T>(T resource)
            where T : IDisposable
        {
            _resources.Push(resource);
            return resource;
        }

        internal async ValueTask DrainAsync()
        {
            if (_retired)
            {
                return;
            }

            try
            {
                if (!_submitted && Submission is { } submission)
                {
                    await submission.WaitAsync(_retirement.Token);
                    _submitted = true;
                }

                if (!_released && Image is { } image)
                {
                    await image.WaitForReleaseAsync(_retirement.Token);
                    _released = true;
                }
            }
            catch (OperationCanceledException) when (_retired)
            {
                // Only confirmed device retirement can replace both completion boundaries.
            }
        }
    }
}
