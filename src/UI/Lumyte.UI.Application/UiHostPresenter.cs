using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.UI.Application;

internal sealed partial class UiHostPresenter : IDisposable
{
    private readonly IGraphicDevice _device;
    private readonly Color2D _background;
    private readonly UiIntermediateTexturePool _intermediates;
    private readonly Renderer2D _renderer;
    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsSampler _sampler;
    private readonly Stack<IDisposable> _resources = [];
    private readonly List<Frame> _frames = [];
    private TextureFormat? _intermediate;

    internal UiHostPresenter(IGraphicDevice device, Color2D background, int maximumFrames = 3)
    {
        _device = device;
        _background = background;
        _intermediates = new(CreateIntermediate);
        try
        {
            var services = new ServiceCollection();
            services.UseGraphics2D();
            ServiceProvider provider = Own(services.BuildServiceProvider());
            IServiceScope scope = Own(provider.CreateScope());
            _renderer = Own(new Renderer2D(device, scope.ServiceProvider.GetRequiredService<DrawingCommandRenderers2D>(), retentionOptions: new() { MaxFramesInFlight = maximumFrames }));
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
        using UiApplicationProfiling.Scope? profile = UiApplicationProfiling.Begin(24);
        try
        {
            if (_frames.Count != 0)
            {
                throw new InvalidOperationException("Drain submitted frames before disposing the presenter.");
            }

            _intermediates.Dispose();
            while (_resources.TryPeek(out IDisposable? resource))
            {
                resource.Dispose();
                _resources.Pop();
            }
        }
        catch (Exception profileError)
        {
            profile?.Failed(profileError);
            throw;
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

    internal async ValueTask PrepareCandidateAsync(DrawingSnapshot2D commands, uint width, uint height)
    {
        using UiApplicationProfiling.Scope? profile = UiApplicationProfiling.Begin(28);
        try
        {
            using IGraphicsTexture target = CreateIntermediate(width, height);
            using IGraphicsTextureView view = target.CreateView();
            using PreparedDrawingFrame2D prepared = await _renderer.PrepareAsync(commands, new() { View = view, PixelScale = 1, LoadOp = AttachmentLoadOp.Clear, ClearColor = _background });
        }
        catch (Exception profileError)
        {
            profile?.Failed(profileError);
            throw;
        }
    }

    internal async Task<SurfaceStatus> DrawAsync(IGraphicsSwapchain swapchain, DrawingSnapshot2D scene, Guid owner, Action<UiFramePixels>? capture = null, IReadOnlyList<UiVisualSnapshot>? visuals = null, Action? submitted = null, int attempt = 1)
    {
        using UiApplicationProfiling.Scope? profile = UiApplicationProfiling.Begin(8);
        try
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
            UiApplicationProfiling.Scope? activeProfile = null;
            long widthPixels = 0;
            long heightPixels = 0;
            long? readbackBytes = null;
            long? captureBytes = null;
            long drawCalls = 0;
            bool targetMeasured = false;
            try
            {
                UiApplicationProfiling.Scope? prepareProfile = activeProfile = UiApplicationProfiling.Begin(9);
                UiIntermediateTexturePool.Entry intermediate = _intermediates.Rent(swapchain.Configuration.Width, swapchain.Configuration.Height, _intermediate ?? TextureFormat.Rgba8Srgb);
                frame.Intermediate = intermediate;
                IGraphicsTexture target = intermediate.Texture;
                if (prepareProfile is not null)
                {
                    widthPixels = target.Width;
                    heightPixels = target.Height;
                    targetMeasured = true;
                }

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

                if (prepareProfile is not null && readback is not null)
                {
                    readbackBytes = checked((long)readback.Count);
                }

                IGraphicsTextureView targetView = intermediate.View;
                var drawingTarget = new RenderTarget2D { View = targetView, PixelScale = 1, LoadOp = AttachmentLoadOp.Clear, ClearColor = _background };
                frame.Retained = await _renderer.PrepareAsync(scene, drawingTarget);

                IArgumentTable table = frame.Own(_device.CreateArgumentTable(new() { TextureCapacity = 1, SamplerCapacity = 1 }));
                uint encode = swapchain.Configuration.Format is TextureFormat.Bgra8Srgb or TextureFormat.Rgba8Srgb ? 0u : 1u;
                var arguments = new UiPresentArguments(table.WriteTexture(0, targetView), table.WriteSampler(0, _sampler), new(target.Width, target.Height), encode);

                // Browser canvas images expire with the rendering turn. Complete every asynchronous preparation before acquisition.
                IGraphicsSemaphore acquired = frame.Own(_device.CreateSemaphore());
                IGraphicsSemaphore rendered = frame.Own(_device.CreateSemaphore());
                prepareProfile?.Counter(8, target.Width);
                prepareProfile?.Counter(9, target.Height);
                prepareProfile?.Counter(10, visuals?.Count ?? 0);
                if (readback is not null)
                {
                    prepareProfile?.Counter(11, checked((long)readback.Count));
                }

                prepareProfile?.Dispose();
                activeProfile = null;
                SurfaceAcquireResult result;
                using (UiApplicationProfiling.Scope? acquireProfile = UiApplicationProfiling.Begin(10))
                {
                    try
                    {
                        result = await swapchain.AcquireNextFrameAsync(acquired);
                    }
                    catch (Exception error)
                    {
                        acquireProfile?.Failed(error);
                        throw;
                    }
                }

                UiApplicationProfiling.Log.SurfaceResult(frame.ProfileKey.Run, frame.ProfileKey.Frame, 0, 0, 10, 1, (int)result.Status, attempt);
                if (result.Frame is not IGraphicsSurfaceFrame image)
                {
                    if (profile is not null)
                    {
                        profile.Outcome = 3;
                    }

                    return result.Status;
                }

                frame.Image = image;
                if (image.Texture.Width != target.Width || image.Texture.Height != target.Height)
                {
                    UiApplicationProfiling.Log.SurfaceResult(frame.ProfileKey.Run, frame.ProfileKey.Frame, 0, 0, 10, 3, (int)SurfaceStatus.Outdated, attempt);
                    if (profile is not null)
                    {
                        profile.Outcome = 3;
                    }

                    return SurfaceStatus.Outdated;
                }

                UiApplicationProfiling.Scope? recordProfile = activeProfile = UiApplicationProfiling.Begin(11);
                IGraphicsTextureView display = frame.Own(image.Texture.CreateView());
                IGraphicsCommandBuffer commands = frame.Own(_device.CreateCommandBuffer(new()));
                var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
                var sample = new BarrierScope(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
                commands.Barrier(intermediate.ColorAttachmentBarrier);
                frame.RetainedRecorded = true;
                frame.Retained.Record(commands);

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
                    if (recordProfile is not null)
                    {
                        drawCalls++;
                    }
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
                recordProfile?.Counter(13, drawCalls);
                recordProfile?.Dispose();
                activeProfile = null;
                using (UiApplicationProfiling.Scope? submitProfile = UiApplicationProfiling.Begin(12))
                {
                    try
                    {
                        frame.SubmissionAttempted = true;
                        frame.Submission = _device.Queue.Submit(new QueueSubmitDesc() { CommandBuffers = [commands], WaitSemaphores = [new() { Semaphore = acquired, Stages = PipelineStage.AllCommands }], SignalSemaphores = [rendered] });
                        submitProfile?.Counter(12, 1);
                    }
                    catch (Exception error)
                    {
                        submitProfile?.Counter(12, 0);
                        submitProfile?.Failed(error);
                        throw;
                    }
                }

                frame.Retained?.AttachSubmission(frame.Submission);
                intermediate.State = readback is not null && !captureDisplay ? TextureState.CopySource : TextureState.Sampled;
                SurfaceStatus status;
                using (UiApplicationProfiling.Scope? presentProfile = UiApplicationProfiling.Begin(13))
                {
                    try
                    {
                        status = image.Present([rendered]);
                    }
                    catch (Exception error)
                    {
                        presentProfile?.Failed(error);
                        throw;
                    }
                }

                UiApplicationProfiling.Log.SurfaceResult(frame.ProfileKey.Run, frame.ProfileKey.Frame, 0, 0, 13, 2, (int)status, attempt);
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
                    UiApplicationProfiling.Scope? captureProfile = activeProfile = UiApplicationProfiling.Begin(29);
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

                    if (captureProfile is not null)
                    {
                        captureBytes = pixels.Length;
                    }

                    capture!(new(target.Width, target.Height, pixels));
                    captureProfile?.Counter(14, pixels.Length);
                    captureProfile?.Dispose();
                    activeProfile = null;
                }

                return status;
            }
            catch (Exception error)
            {
                profile?.Failed(error);
                if (activeProfile?.Stage == 9)
                {
                    if (targetMeasured)
                    {
                        activeProfile.Counter(8, widthPixels);
                        activeProfile.Counter(9, heightPixels);
                    }

                    activeProfile.Counter(10, visuals?.Count ?? 0);
                    if (readbackBytes is { } bytes)
                    {
                        activeProfile.Counter(11, bytes);
                    }
                }
                else if (activeProfile?.Stage == 11)
                {
                    activeProfile.Counter(13, drawCalls);
                }
                else if (activeProfile?.Stage == 29 && captureBytes is { } outputBytes)
                {
                    activeProfile.Counter(14, outputBytes);
                }

                activeProfile?.FailAndDispose(error);
                failure = error;
                frame.Intermediate?.Discard();
                throw;
            }
            finally
            {
                activeProfile?.Dispose();

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
        catch (Exception profileError)
        {
            profile?.Failed(profileError);
            throw;
        }
        finally
        {
            profile?.Counter(15, _frames.Count);
        }
    }

    internal async ValueTask DrainAsync(Guid owner)
    {
        using UiApplicationProfiling.Scope? profile = UiApplicationProfiling.Begin(21, new UiApplicationProfiling.Key(Guid.Empty, 0));
        if (UiApplicationProfiling.Log.IsEnabled(System.Diagnostics.Tracing.EventLevel.Informational, UiApplicationProfiling.Keywords.Control))
        {
            UiApplicationProfiling.Log.OwnerRelation(profile?.Span ?? 0, owner);
        }

        try
        {
            foreach (Frame frame in _frames.Where(frame => frame.Owner == owner).ToArray())
            {
                using (UiApplicationProfiling.Scope? waitProfile = UiApplicationProfiling.Begin(22, frame.ProfileKey))
                {
                    try
                    {
                        await frame.Finished.Task;
                    }
                    catch (Exception error)
                    {
                        waitProfile?.Failed(error);
                        throw;
                    }
                }

                await frame.DrainAsync();
                frame.Dispose();
                _frames.Remove(frame);
            }
        }
        catch (Exception profileError)
        {
            profile?.Failed(profileError);
            throw;
        }
        finally
        {
            profile?.Counter(15, _frames.Count);
        }
    }

    internal async ValueTask ReleaseAfterDeviceRetirementAsync()
    {
        using UiApplicationProfiling.Scope? profile = UiApplicationProfiling.Begin(23);
        try
        {
            _intermediates.StopRenting();
            Frame[] frames = _frames.ToArray();
            foreach (Frame frame in frames)
            {
                frame.ConfirmDeviceRetirement();
            }

            foreach (Frame frame in frames)
            {
                using (UiApplicationProfiling.Scope? waitProfile = UiApplicationProfiling.Begin(22, frame.ProfileKey))
                {
                    try
                    {
                        await frame.Finished.Task;
                    }
                    catch (Exception error)
                    {
                        waitProfile?.Failed(error);
                        throw;
                    }
                }

                frame.Dispose();
                _frames.Remove(frame);
            }

            _intermediates.Dispose();
        }
        catch (Exception profileError)
        {
            profile?.Failed(profileError);
            throw;
        }
        finally
        {
            profile?.Counter(15, _frames.Count);
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
        private bool _resourcesReleased;

        internal UiApplicationProfiling.Key ProfileKey { get; } = UiApplicationProfiling.ReadKey();

        internal Guid Owner { get; } = owner;

        internal bool IsRetired => _retired;

        internal CancellationToken RetirementToken => _retirement.Token;

        internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal IGraphicsSurfaceFrame? Image { get; set; }

        internal IGraphicsSubmission? Submission { get; set; }

        internal PreparedDrawingFrame2D? Retained { get; set; }

        internal bool RetainedRecorded { get; set; }

        internal bool SubmissionAttempted { get; set; }

        internal UiIntermediateTexturePool.Entry? Intermediate { get; set; }

        public void Dispose()
        {
            using UiApplicationProfiling.Scope? profile = UiApplicationProfiling.Begin(16, ProfileKey);
            try
            {
                if (!_retired && ((SubmissionAttempted && Submission is null) || (Submission is not null && !_submitted) || (Image is not null && !_released)))
                {
                    throw new InvalidOperationException("Confirm submission and presentation completion before releasing frame resources.");
                }

                try
                {
                    while (_resources.TryPeek(out IDisposable? resource))
                    {
                        resource.Dispose();
                        _resources.Pop();
                    }

                    if (Retained is { } retained)
                    {
                        if (_retired)
                        {
                            retained.ReleaseAfterDeviceRetirement();
                        }
                        else if (RetainedRecorded)
                        {
                            if (Submission is null)
                            {
                                retained.AbortUnsubmitted();
                            }
                            else
                            {
                                retained.Retire();
                            }
                        }

                        retained.Dispose();
                        Retained = null;
                    }

                    Submission?.Dispose();
                    Submission = null;
                    SubmissionAttempted = false;
                    Image?.Dispose();
                    Image = null;
                    Intermediate?.Release();
                    Intermediate = null;
                    _retirement.Dispose();
                    if (!_resourcesReleased)
                    {
                        _resourcesReleased = true;
                        UiApplicationProfiling.Log.FrameResourcesReleased(ProfileKey.Run, ProfileKey.Frame, 0, 0, 0, _retired ? 1 : 0);
                    }
                }
                catch
                {
                    Intermediate?.Discard();
                    throw;
                }
            }
            catch (Exception profileError)
            {
                profile?.Failed(profileError);
                throw;
            }
        }

        internal void ConfirmDeviceRetirement()
        {
            if (_retired)
            {
                return;
            }

            _retired = true;
            UiApplicationProfiling.Log.DeviceRetired(ProfileKey.Run, ProfileKey.Frame, 0, 0, 0);
            Intermediate?.Discard();
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
                    using (UiApplicationProfiling.Scope? waitProfile = UiApplicationProfiling.Begin(14, ProfileKey))
                    {
                        try
                        {
                            await submission.WaitAsync(_retirement.Token);
                        }
                        catch (Exception error)
                        {
                            waitProfile?.Failed(error);
                            throw;
                        }
                    }

                    _submitted = true;
                }

                if (!_released && Image is { } image)
                {
                    using (UiApplicationProfiling.Scope? waitProfile = UiApplicationProfiling.Begin(15, ProfileKey))
                    {
                        try
                        {
                            await image.WaitForReleaseAsync(_retirement.Token);
                        }
                        catch (Exception error)
                        {
                            waitProfile?.Failed(error);
                            throw;
                        }
                    }

                    _released = true;
                }
            }
            catch (OperationCanceledException) when (_retired)
            {
                // Only confirmed device retirement can replace both completion boundaries.
            }
            catch
            {
                Intermediate?.Discard();
                throw;
            }
        }
    }
}
