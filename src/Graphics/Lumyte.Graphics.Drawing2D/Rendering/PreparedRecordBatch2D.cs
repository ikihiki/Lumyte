using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class PreparedRecordBatch2D : IRetainedCommandBatch2D, IRetainedMemory2D
{
    private static readonly BarrierScope _read = new(PipelineStage.VertexShader | PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
    private readonly RenderPreparationContext2D _context;
    private readonly DrawRecordResources2D _resources;
    private readonly List<IDisposable> _owned = [];
    private readonly List<Page> _pages = [];
    private readonly HashSet<IGraphicsTextureView> _views = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IGraphicsSampler> _samplers = new(ReferenceEqualityComparer.Instance);
    private bool _recorded;
    private bool _disposed;
    private long _bytes;

    private PreparedRecordBatch2D(RenderPreparationContext2D context, DrawRecordResources2D resources)
    {
        _context = context;
        _resources = resources;
        Own(context.RetainResources());
        Own(context.ArgumentPlan.Retain());
    }

    /// <summary>Owns a mapped upload and its completion-retained destination.</summary>
    private interface IStream
    {
        /// <summary>Maps staging before synchronous graph finalization.</summary>
        /// <param name="token">Cancels mapping before recording.</param>
        /// <returns>Completion of the staging map.</returns>
        ValueTask MapAsync(CancellationToken token);

        /// <summary>Releases a staging map, including on failure.</summary>
        void Unmap();

        /// <summary>Records the declared copy producer and shader-read transition.</summary>
        /// <param name="commands">The command buffer owning the copy.</param>
        void Copy(IGraphicsCommandBuffer commands);
    }

    public long EstimatedBytes => checked(_bytes + _context.ArgumentPlan.EstimatedBytes);

    internal IEnumerable<(IPreparedCommandBatch2D Batch, IReadOnlyList<ShaderResources> Reservations)> GraphPages
    {
        get
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                yield return (new PageView(this, i), _pages[i].Scopes);
            }
        }
    }

    public void AccountMemory(Dictionary<object, long> owners)
    {
        owners.TryAdd(this, _bytes);
        owners.TryAdd(_context.ArgumentPlan, _context.ArgumentPlan.EstimatedBytes);
    }

    public void DeclareResources(IDrawingResourceDeclarations2D resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (IGraphicsTextureView view in _views)
        {
            resources.UseTextureView(view, _read);
        }

        foreach (IGraphicsSampler sampler in _samplers)
        {
            resources.UseSampler(sampler);
        }
    }

    public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
    {
        if (_recorded)
        {
            throw new InvalidOperationException("A prepared command batch can be recorded only once.");
        }

        _recorded = true;
        RecordRetained(commands, target, false);
    }

    public void RecordRetained(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _context.ValidateResources();
        if (_views.Any(view => ReferenceEquals(view.Texture, target.Texture)))
        {
            throw new ArgumentException("A command cannot sample its active target.", nameof(target));
        }

        _context.ArgumentPlan.InitializeFallback(commands);
        foreach (Page page in _pages)
        {
            if (page.RecordCount == 0)
            {
                continue;
            }

            if (!resident)
            {
                page.Copy(commands);
            }

            page.Record(commands, target, 0, (uint)page.RecordCount);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (Page page in _pages)
        {
            page.Unmap();
            page.Cpu.ReleasePackingData();
        }

        while (_owned.Count != 0)
        {
            _owned[^1].Dispose();
            _owned.RemoveAt(_owned.Count - 1);
        }
    }

    internal static ValueTask<IPreparedCommandBatch2D> PrepareAsync<T>(RenderPreparationContext2D context, ReadOnlyMemory<T> commands, CancellationToken cancellationToken)
        where T : DrawingCommand2D
    {
        DrawRecordResources2D? resources = context.RecordResources();
        if (resources is null)
        {
            return PreparedGpuBatch2D.PrepareAsync(context, commands, cancellationToken);
        }

        using var compile = DrawingProfileScope.Start(6);
        var batch = new PreparedRecordBatch2D(context, resources);
        try
        {
            Page? page = null;
            foreach (T command in commands.Span)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NormalizedClip2D clip = context.Clips.Get(command.State.Clip, Matrix3x2.Identity, command.State.Clip.Elements);
                if (clip.Kind == NormalizedClipKind2D.Empty)
                {
                    continue;
                }

                if (DrawRecordPage2D.TrySimple(command, context.Target, clip, out DrawRecord2D simple, out Color2D color))
                {
                    if (simple.Kind == 0)
                    {
                        continue;
                    }

                    if (page is null || !page.CanAdmit(null, 0, clip, color))
                    {
                        page?.Allocate();
                        page = new(batch);
                        batch._pages.Add(page);
                    }

                    if (!page.CanAdmit(null, 0, clip, color))
                    {
                        throw new NotSupportedException("A single draw exceeds the fixed-record stream and resource budget.");
                    }

                    page.Cpu.AddSimple(simple, color, clip);
                    continue;
                }

                ShaderResources commandScope = context.ArgumentPlan.Reserve();
                var program = new DrawingProgram2D(commandScope, context.Target, context.Clips, recordLowering: true);
                uint root = program.Compile(command);
                program.ValidateFiniteData();
                DrawingInstruction terminal = program.Instructions[(int)root];
                if (terminal.Kind is 16 or 17)
                {
                    terminal = program.Instructions[(int)terminal.First];
                }

                if (terminal.Kind != 11 || (command is not DrawPaintGraphCommand2D && program.Instructions[(int)terminal.First].Kind == 0))
                {
                    context.ArgumentPlan.CancelUnpublished(commandScope);
                    continue;
                }

                ScissorRect bounds = DrawingBounds2D.GetScissor(command, null, context.Target, null, false, false, program.Clip?.Bounds);
                if (bounds.Width == 0 || bounds.Height == 0)
                {
                    context.ArgumentPlan.CancelUnpublished(commandScope);
                    continue;
                }

                if (page is null || !page.CanAdmit(program, root, clip))
                {
                    page?.Allocate();
                    page = new(batch);
                    batch._pages.Add(page);
                }

                if (!page.CanAdmit(program, root, clip))
                {
                    throw new NotSupportedException("A single draw exceeds the fixed-record stream and resource budget.");
                }

                page.Admit(commandScope, program);
                page.Cpu.Add(command, program, root, bounds, clip);
                batch._views.UnionWith(program.Views);
                batch._samplers.UnionWith(program.Samplers);
            }

            page?.Allocate();
            compile?.Success();
            return ValueTask.FromResult<IPreparedCommandBatch2D>(batch);
        }
        catch (Exception error)
        {
            compile?.Failure(error);
            context.CleanupAfterFailure(batch, error);
            throw;
        }
    }

    internal void RecordRange(IGraphicsCommandBuffer commands, IGraphicsTextureView target, int pageIndex, uint first, uint count, bool resident = false)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, _pages.Count);
        Page page = _pages[pageIndex];
        DrawRecordPage2D.ValidateRange(first, count, (uint)page.RecordCount);
        if (count == 0)
        {
            return;
        }

        _context.ValidateResources();
        if (page.SamplesTarget(target))
        {
            throw new ArgumentException("A command cannot sample its active target.", nameof(target));
        }

        _context.ArgumentPlan.InitializeFallback(commands);
        if (!resident)
        {
            page.Copy(commands);
        }

        page.Record(commands, target, first, count);
    }

    private T Own<T>(T value)
        where T : IDisposable
    {
        _owned.Add(value);
        return value;
    }

    private sealed class Page
    {
        private readonly PreparedRecordBatch2D _batch;
        private readonly PageResources _group;
        private readonly List<IStream> _streams = [];
        private readonly HashSet<IGraphicsTextureView> _views = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<IGraphicsSampler> _samplers = new(ReferenceEqualityComparer.Instance);
        private Stream<DrawRecord2D> _records = null!;
        private Stream<DrawDirectoryEntry2D> _paints = null!;
        private Stream<DrawDirectoryEntry2D> _clips = null!;
        private Stream<ImagePaintData2D> _images = null!;
        private Stream<MaskDescriptor2D> _masks = null!;
        private Stream<DrawPageMetadata2D>? _metadata;
        private Stream<DrawBasicMetadata2D>? _basicMetadata;
        private Stream<DrawMaskMetadata2D>? _maskMetadata;
        private Stream<DrawImageMetadata2D>? _imageMetadata;
        private IGraphicsPipeline _pipeline = null!;
        private int _mode;
        private ulong _numericCount;
        private IGraphicsBuffer<Vector4>? _numericUpload;
        private IGraphicsBuffer<Vector4>? _numeric;
        private IGpuRef<Vector4>? _numericRef;
        private IGpuRef<IGraphicsSampler>? _sampler;

        internal Page(PreparedRecordBatch2D batch)
        {
            _batch = batch;
            Reservation = batch._context.ArgumentPlan.Reserve();
            Scopes.Add(Reservation);
            _group = new(Reservation);
        }

        internal DrawRecordPage2D Cpu { get; } = new();

        internal int RecordCount { get; private set; }

        internal ShaderResources Reservation { get; }

        internal List<ShaderResources> Scopes { get; } = [];

        internal bool SamplesTarget(IGraphicsTextureView target) => _views.Any(view => ReferenceEquals(view.Texture, target.Texture));

        internal void DeclareResources(IDrawingResourceDeclarations2D resources)
        {
            foreach (IGraphicsTextureView view in _views)
            {
                resources.UseTextureView(view, _read);
            }

            foreach (IGraphicsSampler sampler in _samplers)
            {
                resources.UseSampler(sampler);
            }
        }

        internal bool CanAdmit(DrawingProgram2D? program, uint root, NormalizedClip2D clip, Color2D color = default)
        {
            ArgumentTableBindingLimits limits = _batch._context.Device.Caps.ArgumentTableBindingLimits;
            DeviceCaps caps = _batch._context.Device.Caps;
            ulong maximum = caps.MaxStorageBufferBindingSize == 0 ? caps.MaxBufferSize : Math.Min(caps.MaxBufferSize, caps.MaxStorageBufferBindingSize);
            maximum = Math.Min(Math.Min(maximum, limits.MaximumBufferRangeBytes), int.MaxValue);
            if (!Cpu.FitsAddition(program, root, clip, color, maximum, out uint buffers, out bool needsSampler))
            {
                return false;
            }

            uint views = checked((uint)(program is null ? _views.Count : _views.Union(program.Views, ReferenceEqualityComparer.Instance).Count()));
            uint samplers = checked((uint)(program is null ? _samplers.Count : _samplers.Union(program.Samplers, ReferenceEqualityComparer.Instance).Count()));
            if (needsSampler)
            {
                IGraphicsSampler nearest = _batch._context.ArgumentPlan.Sampler(_batch._context.Resources);
                if (!_samplers.Contains(nearest) && (program is null || !program.Samplers.Contains(nearest)))
                {
                    samplers++;
                }
            }

            uint textureBindings = Math.Max(views, limits.MinimumTextureBindings);
            uint samplerBindings = Math.Max(samplers, limits.MinimumSamplerBindings);
            ulong combined = limits.ReservedBindings + limits.ColorAttachmentBindingWeight + ((ulong)textureBindings * limits.TextureBindingWeight) + ((ulong)samplerBindings * limits.SamplerBindingWeight) + ((ulong)buffers * limits.BufferBindingWeight);
            return views <= limits.TextureSlots && textureBindings <= limits.TexturesPerDraw && samplers <= limits.SamplerSlots && samplerBindings <= limits.SamplersPerDraw
                && Math.Min(limits.BufferSlots, limits.BuffersPerDraw) >= buffers && combined <= limits.CombinedBindings;
        }

        internal void Admit(ShaderResources scope, DrawingProgram2D program)
        {
            _group.Join(scope);
            Scopes.Add(scope);
            _views.UnionWith(program.Views);
            _samplers.UnionWith(program.Samplers);
        }

        internal void Allocate()
        {
            if (Cpu.Records.Count == 0)
            {
                return;
            }

            if (_batch._context.Device.GetBufferLayout<Vector4>().ElementStrideInBytes != 16)
            {
                throw new NotSupportedException("The backend does not implement the canonical numeric stream stride.");
            }

            RecordCount = Cpu.Records.Count;
            _mode = Cpu.IsSimple ? 4 : (Cpu.Masks.Count > 0 ? 1 : 0) | (Cpu.Images.Count > 0 ? 2 : 0);
            _pipeline = _batch._resources.GetPipeline(_mode);
            _records = Create<DrawRecord2D>(Cpu.Records.Count, 96);
            if (_mode != 4)
            {
                _paints = Create<DrawDirectoryEntry2D>(Cpu.Paints.Count, 16);
                if (Cpu.Clips.Count > 1)
                {
                    _clips = Create<DrawDirectoryEntry2D>(Cpu.Clips.Count, 16);
                }

                if (Cpu.Images.Count > 0)
                {
                    _images = Create<ImagePaintData2D>(Cpu.Images.Count, 128);
                    IGraphicsSampler sampler = _batch._context.ArgumentPlan.Sampler(_batch._context.Resources);
                    _batch._samplers.Add(sampler);
                    _samplers.Add(sampler);
                    Reservation.RegisterSampler(sampler, reference => _sampler = reference);
                }

                if (Cpu.Masks.Count > 0)
                {
                    _masks = Create<MaskDescriptor2D>(Cpu.Masks.Count, 80);
                }

                switch (_mode)
                {
                    case 0: _basicMetadata = Create<DrawBasicMetadata2D>(1, 48); break;
                    case 1: _maskMetadata = Create<DrawMaskMetadata2D>(1, 64); break;
                    case 2: _imageMetadata = Create<DrawImageMetadata2D>(1, 64); break;
                    default: _metadata = Create<DrawPageMetadata2D>(1, 80); break;
                }
            }

            ulong count = checked((ulong)Math.Max(1, Cpu.Numeric.Count));
            _numericCount = count;
            DrawNumericPool2D.Lease numeric = _batch.Own(_batch._resources.NumericPool.Rent(checked((int)count)));
            _numericUpload = numeric.Upload;
            _numeric = numeric.Data;
            Reservation.RegisterBuffer(_numeric.SliceElements(0, count), reference => _numericRef = reference);
            _batch._context.ArgumentPlan.MarkPreparedBuffer(Reservation, _numeric.SliceElements(0, count), new(PipelineStage.Copy | PipelineStage.VertexShader | PipelineStage.FragmentShader, ResourceAccess.CopyWrite | ResourceAccess.ShaderRead));
            _batch._bytes = checked(_batch._bytes + (long)(_numeric.SizeInBytes + _numericUpload.SizeInBytes));
            _batch._context.ArgumentPlan.DeferUpload(Reservation, UploadAsync, MapAsync, Fill);
        }

        internal void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target, uint first, uint count)
        {
            DrawRecordPage2D.ValidateRange(first, count, (uint)RecordCount);
            if (count == 0)
            {
                return;
            }

            DrawingTargetInfo2D info = _batch._context.Target;
            IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = target, LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }] });
            render.SetPipeline(_pipeline);
            render.SetRenderState(new()
            {
                Rasterization = new() { Cull = CullMode.None },
                ColorTargets = [new() { BlendEnable = true, Color = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha }, Alpha = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha } }],
            });
            render.SetViewport(new(0, 0, info.Description.Width, info.Description.Height, 0, 1));
            render.SetScissor(new(0, 0, info.Description.Width, info.Description.Height));
            render.SetBlendConstant(default);
            render.SetStencilReference(0);

            if (_mode == 4)
            {
                DrawSimple(render, info, first, count);
            }
            else
            {
                switch (_mode)
                {
                    case 0: Draw(render, info, _basicMetadata!.Reference!, first, count); break;
                    case 1: Draw(render, info, _maskMetadata!.Reference!, first, count); break;
                    case 2: Draw(render, info, _imageMetadata!.Reference!, first, count); break;
                    default: Draw(render, info, _metadata!.Reference!, first, count); break;
                }
            }

            render.End();
            var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
            commands.Barrier(new TextureBarrierDesc { Texture = target.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = attachment, After = attachment });
        }

        internal void Copy(IGraphicsCommandBuffer commands)
        {
            foreach (IStream stream in _streams)
            {
                stream.Copy(commands);
            }

            commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = _numericUpload!.SliceElements(0, _numericCount), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.CopyBuffer(_numericUpload.SliceElements(0, _numericCount), _numeric!.SliceElements(0, _numericCount));
            commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = _numeric.SliceElements(0, _numericCount), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = _read });
        }

        internal void Unmap()
        {
            foreach (IStream stream in _streams)
            {
                stream.Unmap();
            }

            if (_numericUpload?.IsMapped == true)
            {
                _numericUpload.Unmap();
            }
        }

        private void Draw<TMetadata>(IRenderEncoder render, DrawingTargetInfo2D info, IGpuRef<TMetadata> metadata, uint first, uint count)
            where TMetadata : struct, IShaderData
        {
            var args = new DrawRangeArguments2D<TMetadata>(_records.Reference!, metadata, first, count, new(info.Description.Width, info.Description.Height), info.PixelOrigin, 0, 0);
            if (_batch._context.Device.Caps.ShaderTarget == ShaderTarget.SpirV)
            {
                Reservation.Draw(render, in args, 6, count, 0, 0);
            }
            else
            {
                for (uint i = 0; i < count; i++)
                {
                    DrawRangeArguments2D<TMetadata> selected = args with { FirstRecord = first + i, RecordCount = 1 };
                    Reservation.Draw(render, in selected, 6, 1, 0, 0);
                }
            }
        }

        private void DrawSimple(IRenderEncoder render, DrawingTargetInfo2D info, uint first, uint count)
        {
            var args = new SimpleDrawRangeArguments2D(_records.Reference!, _numericRef!, first, count, new(info.Description.Width, info.Description.Height), info.PixelOrigin, 0, 0);
            if (_batch._context.Device.Caps.ShaderTarget == ShaderTarget.SpirV)
            {
                Reservation.Draw(render, in args, 6, count, 0, 0);
            }
            else
            {
                for (uint i = 0; i < count; i++)
                {
                    SimpleDrawRangeArguments2D selected = args with { FirstRecord = first + i, RecordCount = 1 };
                    Reservation.Draw(render, in selected, 6, 1, 0, 0);
                }
            }
        }

        private Stream<T> Create<T>(int count, ulong stride)
            where T : struct, IShaderData
        {
            var result = new Stream<T>(_batch, Reservation, count, stride, _pipeline.Desc.FragmentShader!.Artifact);
            _streams.Add(result);
            return result;
        }

        private async ValueTask MapAsync(CancellationToken token)
        {
            foreach (IStream stream in _streams)
            {
                await stream.MapAsync(token).ConfigureAwait(false);
            }

            await _numericUpload!.MapAsync(token).ConfigureAwait(false);
        }

        private async ValueTask UploadAsync(CancellationToken token)
        {
            await MapAsync(token).ConfigureAwait(false);
            Fill();
        }

        private void Fill()
        {
            using var profile = DrawingProfileScope.Start(7);
            long copied = 0;
            try
            {
                Cpu.Resolve(null!, _sampler!);
                copied += _records.Fill(CollectionsMarshal.AsSpan(Cpu.Records));
                if (_mode != 4)
                {
                    copied += _paints.Fill(CollectionsMarshal.AsSpan(Cpu.Paints));
                    IGpuRef<DrawDirectoryEntry2D> clips = _paints.Reference!;
                    if (Cpu.Clips.Count > 1)
                    {
                        copied += _clips.Fill(CollectionsMarshal.AsSpan(Cpu.Clips));
                        clips = _clips.Reference!;
                    }

                    if (Cpu.Images.Count > 0)
                    {
                        copied += _images.Fill(CollectionsMarshal.AsSpan(Cpu.Images));
                    }

                    if (Cpu.Masks.Count > 0)
                    {
                        copied += _masks.Fill(CollectionsMarshal.AsSpan(Cpu.Masks));
                    }

                    copied += _mode switch
                    {
                        0 => _basicMetadata!.Fill([new(_paints.Reference!, clips, _numericRef!)]),
                        1 => _maskMetadata!.Fill([new(_paints.Reference!, clips, _numericRef!, _masks.Reference!)]),
                        2 => _imageMetadata!.Fill([new(_paints.Reference!, clips, _numericRef!, _images.Reference!)]),
                        _ => _metadata!.Fill([new(_paints.Reference!, clips, _numericRef!, _masks.Reference!, _images.Reference!)]),
                    };
                }

                if (Cpu.Numeric.Count == 0)
                {
                    _numericUpload!.CopyFrom([Vector4.Zero]);
                    copied += 16;
                }
                else
                {
                    _numericUpload!.CopyFrom(CollectionsMarshal.AsSpan(Cpu.Numeric));
                    copied += (long)Cpu.Numeric.Count * 16;
                }

                profile?.Success();
            }
            catch (Exception error)
            {
                profile?.Failure(error);
                throw;
            }
            finally
            {
                profile?.Counter(8, copied);
                Cpu.ReleasePackingData();
                Unmap();
            }
        }
    }

    private sealed class PageView(PreparedRecordBatch2D owner, int index) : IPreparedCommandBatch2D
    {
        private bool _recorded;

        public void DeclareResources(IDrawingResourceDeclarations2D resources) => owner._pages[index].DeclareResources(resources);

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
        {
            if (_recorded)
            {
                throw new InvalidOperationException("A prepared page can be recorded only once.");
            }

            _recorded = true;
            owner.RecordRange(commands, target, index, 0, (uint)owner._pages[index].RecordCount);
        }

        public void Dispose()
        {
            // The graph owns the containing batch once, not one native owner per view.
        }
    }

    private sealed class PageResources(ShaderResources resources) : ShaderResources(resources)
    {
        internal void Join(ShaderResources command) => JoinOperation(command);
    }

    private sealed class Stream<T> : IStream
        where T : struct, IShaderData
    {
        private readonly ulong _count;
        private readonly IGraphicsShaderDataBuffer<T> _upload;
        private readonly IGraphicsShaderDataBuffer<T> _data;

        internal Stream(PreparedRecordBatch2D owner, ShaderResources reservation, int count, ulong stride, ShaderArtifact artifact)
        {
            _count = checked((ulong)count);
            ulong capacity = _count;
            _upload = owner.Own(owner._context.Device.CreateShaderDataBuffer<T>(artifact, capacity, MemoryPreference.Upload));
            _data = owner.Own(owner._context.Device.CreateShaderDataBuffer<T>(artifact, (ulong)count));
            if (_data.ShaderElementStrideInBytes != stride || _upload.ShaderElementStrideInBytes != stride)
            {
                throw new NotSupportedException("The backend does not implement the canonical fixed draw ABI.");
            }

            reservation.RegisterBuffer(_data.SliceElements(0, _count), reference => Reference = reference);
            owner._context.ArgumentPlan.MarkPreparedShaderData(reservation, _data.SliceElements(0, _count), new(PipelineStage.Copy | PipelineStage.VertexShader | PipelineStage.FragmentShader, ResourceAccess.CopyWrite | ResourceAccess.ShaderRead));
            owner._bytes = checked(owner._bytes + (long)(_data.SizeInBytes + _upload.SizeInBytes) + ((long)capacity * 16) + ((long)count * 2048));
        }

        internal IGpuRef<T>? Reference { get; private set; }

        public ValueTask MapAsync(CancellationToken token) => _upload.MapAsync(token);

        public void Unmap()
        {
            if (_upload.IsMapped)
            {
                _upload.Unmap();
            }
        }

        public void Copy(IGraphicsCommandBuffer commands)
        {
            commands.Barrier(new ShaderDataBufferBarrierDesc<T> { Buffer = _upload.SliceElements(0, _count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.CopyBuffer(_upload.SliceElements(0, _count), _data.SliceElements(0, _count));
            commands.Barrier(new ShaderDataBufferBarrierDesc<T> { Buffer = _data.SliceElements(0, _count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = _read });
        }

        internal long Fill(ReadOnlySpan<T> values)
        {
            _upload.CopyFrom(values);
            return checked((long)((ulong)values.Length * _upload.ShaderElementStrideInBytes));
        }
    }
}
