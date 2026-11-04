using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Incremental.Benchmarks;

// Single-owner prototype: callers serialize CPU access and own target transitions,
// command buffers and submissions. Each slot owns distinct resident GPU storage.
internal sealed class DirectRectangleRenderer : IDisposable
{
    private const int RunSize = 1024;
    private const int ChunksPerRun = RunSize / DirectRectangleScene.ChunkSize;
    private readonly IGraphicDevice _device;
    private readonly SolidRectangleResources2D _resources;
    private int _slots;
    private bool _disposed;

    internal DirectRectangleRenderer(IGraphicDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _resources = new(device);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_slots != 0)
        {
            throw new InvalidOperationException("Dispose every slot before disposing the renderer.");
        }

        _resources.Dispose();
        _disposed = true;
    }

    internal Slot CreateSlot(DirectRectangleScene scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        var slot = new Slot(this, scene);
        _slots++;
        return slot;
    }

    internal sealed class Slot : IDisposable
    {
        private readonly DirectRectangleRenderer _owner;
        private readonly int _count;
        private readonly Run[] _runs;
        private readonly DirectRectangleScene.Page?[] _resident;
        private bool _quarantined;
        private bool _busy;
        private bool _disposed;
        private bool _reportedAllocations;

        internal Slot(DirectRectangleRenderer owner, DirectRectangleScene scene)
        {
            _owner = owner;
            _count = scene.Count;
            _resident = new DirectRectangleScene.Page?[scene.Pages.Length];
            _runs = new Run[checked((int)(((long)_count + RunSize - 1) / RunSize))];
            int created = 0;
            try
            {
                for (; created < _runs.Length; created++)
                {
                    _runs[created] = new(owner._device, Math.Min(RunSize, _count - (created * RunSize)));
                }
            }
            catch
            {
                for (int i = created - 1; i >= 0; i--)
                {
                    _runs[i].Dispose();
                }

                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_busy || _quarantined)
            {
                throw new InvalidOperationException("Cannot dispose a busy or quarantined slot.");
            }

            for (int i = _runs.Length - 1; i >= 0; i--)
            {
                _runs[i].Dispose();
            }

            _disposed = true;
            _owner._slots--;
        }

        internal async ValueTask<Frame> PrepareAsync(DirectRectangleScene scene)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(scene);
            if (_busy || _quarantined)
            {
                throw new InvalidOperationException("The slot already has an active frame or is quarantined.");
            }

            if (scene.Count != _count)
            {
                throw new NotSupportedException("A slot has fixed topology and command count.");
            }

            _busy = true;
            try
            {
                bool[] dirty = new bool[_resident.Length];
                int chunks = 0;
                long bytes = 0;
                for (int runIndex = 0; runIndex < _runs.Length; runIndex++)
                {
                    int first = runIndex * ChunksPerRun;
                    int end = Math.Min(first + ChunksPerRun, _resident.Length);
                    bool map = false;
                    for (int pageIndex = first; pageIndex < end; pageIndex++)
                    {
                        if (!ReferenceEquals(_resident[pageIndex], scene.Pages[pageIndex]))
                        {
                            dirty[pageIndex] = true;
                            chunks++;
                            bytes += (long)scene.Pages[pageIndex].Count * DirectRectangleScene.VectorsPerRectangle * 16;
                            map = true;
                        }
                    }

                    if (!map)
                    {
                        continue;
                    }

                    Run run = _runs[runIndex];
                    await run.Entry.Upload.MapAsync().ConfigureAwait(false);
                    try
                    {
                        for (int pageIndex = first; pageIndex < end; pageIndex++)
                        {
                            if (dirty[pageIndex])
                            {
                                DirectRectangleScene.Page page = scene.Pages[pageIndex];
                                ulong offset = (ulong)((pageIndex % ChunksPerRun) * DirectRectangleScene.ChunkSize * DirectRectangleScene.VectorsPerRectangle);
                                run.Entry.Upload.SliceElements(offset, (ulong)page.Payload.Length).CopyFrom(page.Payload);
                            }
                        }
                    }
                    finally
                    {
                        try
                        {
                            run.Entry.Upload.Unmap();
                        }
                        catch
                        {
                            _quarantined = true;
                            throw;
                        }
                    }
                }

                int allocations = _reportedAllocations ? 0 : _runs.Length * 3;
                var frame = new Frame(this, scene, dirty, chunks, bytes, allocations);
                _reportedAllocations = true;
                return frame;
            }
            catch
            {
                _busy = false;
                throw;
            }
        }

        internal sealed class Frame(Slot slot, DirectRectangleScene scene, bool[] dirty, int dirtyChunks, long uploadedBytes, int gpuAllocations) : IDisposable
        {
            private readonly Slot _slot = slot;
            private readonly DirectRectangleScene _scene = scene;
            private readonly bool[] _dirty = dirty;
            private IGraphicsCommandBuffer? _commands;
            private IGraphicsSubmission? _submission;
            private bool _recordSucceeded;
            private bool _released;

            internal long UploadedBytes { get; } = uploadedBytes;

            internal int DirtyChunks { get; } = dirtyChunks;

            // Planned copy calls: contiguous dirty pages merge, but never across GPU runs.
            internal int CopyRanges { get; } = CountCopyRanges(dirty);

            // Renderer never packs commands; use scene.LastPackedCommands for scene construction cost.
            internal int PackedCommands => 0;

            internal int DrawCount => _slot._runs.Length;

            // Slot's eager allocation count (two buffers plus one table per run),
            // attributed once to its first successful Prepare; excludes shared pipeline/shaders.
            internal int GpuAllocations { get; } = gpuAllocations;

            public void Dispose()
            {
                if (_released)
                {
                    return;
                }

                if (_submission is not null)
                {
                    // Never silently recycle pending, failed or externally disposed submissions.
                    Complete();
                    return;
                }

                if (_commands is not null && _commands.State != CommandBufferState.Disposed)
                {
                    throw new InvalidOperationException("Dispose the recorded, never-submitted commands before aborting this frame.");
                }

                // If submission happened but attachment failed, even disposing commands cannot
                // establish completion. Refuse reuse rather than guessing about GPU ownership.
                if (_commands is not null)
                {
                    // Caller must use AbortUnsubmitted after explicitly confirming no Submit occurred.
                    throw new InvalidOperationException("Use AbortUnsubmitted only if these disposed commands were never submitted.");
                }

                Release();
            }

            internal void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
            {
                ObjectDisposedException.ThrowIf(_released, this);
                ArgumentNullException.ThrowIfNull(commands);
                ArgumentNullException.ThrowIfNull(target);
                if (_commands is not null)
                {
                    throw new InvalidOperationException("A frame can be recorded only once.");
                }

                if (commands.State != CommandBufferState.Recording || target.Texture.Width != 1024 || target.Texture.Height != 1024
                    || target.Info.BaseMipLevel != 0 || target.Info.MipLevelCount != 1
                    || target.Info.BaseArrayLayer != 0 || target.Info.ArrayLayerCount != 1
                    || target.Info.Dimension != TextureViewDimension.D2
                    || target.Info.Format is not (TextureFormat.Rgba8Srgb or TextureFormat.Rgba16Float)
                    || (target.Texture.Usage & TextureUsage.RenderAttachment) == 0)
                {
                    throw new NotSupportedException("A recording command buffer and fixed 1024 target are required.");
                }

                // Set before the first write: even partial recording pins this slot.
                _commands = commands;
                IRenderEncoder clear = commands.BeginRenderPass(new()
                {
                    ColorAttachments = [new() { View = target, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(0, 0, 0, 0) }],
                });
                clear.End();
                AttachmentBarrier(commands, target);
                for (int runIndex = 0; runIndex < _slot._runs.Length; runIndex++)
                {
                    if (runIndex != 0)
                    {
                        AttachmentBarrier(commands, target);
                    }

                    Run run = _slot._runs[runIndex];
                    int first = runIndex * ChunksPerRun;
                    int end = Math.Min(first + ChunksPerRun, _dirty.Length);
                    for (int pageIndex = first; pageIndex < end; pageIndex++)
                    {
                        if (!_dirty[pageIndex])
                        {
                            continue;
                        }

                        ulong offset = (ulong)((pageIndex % ChunksPerRun) * DirectRectangleScene.ChunkSize * DirectRectangleScene.VectorsPerRectangle);
                        ulong length = (ulong)_scene.Pages[pageIndex].Payload.Length;
                        while (pageIndex + 1 < end && _dirty[pageIndex + 1])
                        {
                            pageIndex++;
                            length += (ulong)_scene.Pages[pageIndex].Payload.Length;
                        }

                        BufferSlice<Vector4> upload = run.Entry.Upload.SliceElements(offset, length);
                        BufferSlice<Vector4> data = run.Entry.Data.SliceElements(offset, length);
                        var shaderRead = new BarrierScope(PipelineStage.VertexShader | PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
                        commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = upload, Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
                        commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = data, Before = run.Entry.Used ? shaderRead : default, After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
                        commands.CopyBuffer(upload, data);
                        commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = data, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = shaderRead });
                        commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = upload, Before = new(PipelineStage.Copy, ResourceAccess.CopyRead), After = new(PipelineStage.Host, ResourceAccess.HostWrite) });
                    }

                    IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = target, LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }] });
                    try
                    {
                        render.SetPipeline(_slot._owner._resources.Pipeline);
                        render.SetArgumentTable(run.Entry.Table);
                        render.SetRenderState(new()
                        {
                            Rasterization = new() { Cull = CullMode.None },
                            ColorTargets = [new() { BlendEnable = true, Color = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha }, Alpha = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha } }],
                        });
                        render.SetViewport(new(0, 0, 1024, 1024, 0, 1));
                        render.SetScissor(new(0, 0, 1024, 1024));
                        render.SetBlendConstant(new(0, 0, 0, 0));
                        render.SetStencilReference(0);
                        SolidRectangleArguments2D arguments = run.Arguments;
                        render.SetArguments(in arguments);
                        render.Draw(6, (uint)run.Count);
                    }
                    finally
                    {
                        render.End();
                    }
                }

                _recordSucceeded = true;
            }

            internal void Submitted(IGraphicsSubmission submission)
            {
                ObjectDisposedException.ThrowIf(_released, this);
                ArgumentNullException.ThrowIfNull(submission);
                if (!_recordSucceeded || _submission is not null || _commands!.State is not (CommandBufferState.Submitted or CommandBufferState.Completed))
                {
                    throw new InvalidOperationException("Attach the actual submission once, after successful queue submission.");
                }

                // The abstraction cannot prove queue membership: caller must attach the exact returned submission.
                _submission = submission;
            }

            internal void Complete()
            {
                ObjectDisposedException.ThrowIf(_released, this);
                if (_submission is null || _submission.Status != SubmissionStatus.Completed
                    || _slot._owner._device.Status != GraphicsDeviceStatus.Ready)
                {
                    throw new InvalidOperationException("The attached submission must be completed before publishing residency.");
                }

                for (int i = 0; i < _slot._resident.Length; i++)
                {
                    _slot._resident[i] = _scene.Pages[i];
                }

                foreach (Run run in _slot._runs)
                {
                    run.Entry.Used = true;
                }

                Release();
            }

            internal void AbortUnsubmitted()
            {
                ObjectDisposedException.ThrowIf(_released, this);
                if (_submission is not null || (_commands is not null && _commands.State != CommandBufferState.Disposed))
                {
                    throw new InvalidOperationException("Abort requires never-submitted, already disposed command memory.");
                }

                // Explicit caller assertion: no queue submission occurred, including throwing Submit calls.
                Release();
            }

            private static int CountCopyRanges(bool[] dirty)
            {
                int ranges = 0;
                for (int i = 0; i < dirty.Length; i++)
                {
                    if (dirty[i] && (i % ChunksPerRun == 0 || !dirty[i - 1]))
                    {
                        ranges++;
                    }
                }

                return ranges;
            }

            private static void AttachmentBarrier(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
            {
                var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
                commands.Barrier(new TextureBarrierDesc { Texture = target.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = attachment, After = attachment });
            }

            private void Release()
            {
                _released = true;
                _slot._busy = false;
            }
        }

        private sealed class Run : IDisposable
        {
            internal Run(IGraphicDevice device, int count)
            {
                Count = count;
                Entry = new(device, (ulong)(count * DirectRectangleScene.VectorsPerRectangle));
                try
                {
                    Arguments = new(Entry.Table.WriteBuffer(0, Entry.Data.SliceElements(0, (ulong)(count * DirectRectangleScene.VectorsPerRectangle))), new(1024, 1024));
                }
                catch
                {
                    Entry.Dispose();
                    throw;
                }
            }

            internal int Count { get; }

            internal SolidRectangleResources2D.Entry Entry { get; }

            internal SolidRectangleArguments2D Arguments { get; }

            public void Dispose() => Entry.Dispose();
        }
    }
}
