using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class PreparedSparseStripBatch2D : IRetainedCommandBatch2D, IRetainedMemory2D
{
    private readonly RenderPreparationContext2D _context;
    private readonly List<IDisposable> _owned = [];
    private readonly List<Action<IGraphicsCommandBuffer>> _copies = [];
    private readonly List<Action<IDrawingResourceDeclarations2D>> _copyDeclarations = [];
    private readonly List<Action<IDrawingResourceDeclarations2D>> _readDeclarations = [];
    private readonly List<Func<CancellationToken, ValueTask>> _maps = [];
    private readonly List<Action> _fills = [];
    private readonly List<Action> _unmaps = [];
    private readonly DrawRecordPage2D _page = new();
    private readonly ShaderResources _scope;
    private readonly List<DrawingProgram2D> _programs = [];
    private readonly IGraphicsPipeline _pipeline;
    private IGpuRef<DrawRecord2D>? _records;
    private IGpuRef<DrawPageMetadata2D>? _metadata;
    private IGpuRef<DrawDirectoryEntry2D>? _paints;
    private IGpuRef<DrawDirectoryEntry2D>? _clips;
    private IGpuRef<Vector4>? _numeric;
    private IGpuRef<MaskDescriptor2D>? _masks;
    private IGpuRef<ImagePaintData2D>? _images;
    private IGpuRef<IGraphicsSampler>? _sampler;
    private IGpuRef<IGraphicsTextureView>? _fallback;
    private int _count;
    private long _bytes;
    private bool _copied;
    private bool _disposed;
    private bool _recorded;

    private PreparedSparseStripBatch2D(RenderPreparationContext2D context)
    {
        _context = context;
        Own(context.RetainResources());
        Own(context.ArgumentPlan.Retain());
        _scope = context.ArgumentPlan.Reserve();
        _pipeline = context.RecordResources()!.GetPipeline(3);
    }

    public long EstimatedBytes => checked(_bytes + _context.ArgumentPlan.EstimatedBytes);

    internal IReadOnlyList<ShaderResources> Reservations => [_scope];

    public void AccountMemory(Dictionary<object, long> owners)
    {
        owners.TryAdd(this, _bytes);
        owners.TryAdd(_context.ArgumentPlan, _context.ArgumentPlan.EstimatedBytes);
    }

    public void DeclareResources(IDrawingResourceDeclarations2D resources)
    {
        var read = new BarrierScope(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
        foreach (IGraphicsTextureView view in _programs.SelectMany(p => p.Views).Distinct())
        {
            resources.UseTextureView(view, read);
        }

        foreach (IGraphicsSampler sampler in _programs.SelectMany(p => p.Samplers).Distinct())
        {
            resources.UseSampler(sampler);
        }
    }

    public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target) => RecordRetained(commands, target, false);

    public void RecordRetained(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_recorded && !resident)
        {
            throw new InvalidOperationException("A sparse batch records only once.");
        }

        _recorded = true;
        _context.ValidateResources();
        if (_programs.SelectMany(p => p.Views).Any(v => ReferenceEquals(v.Texture, target.Texture)))
        {
            throw new ArgumentException("A sparse drawing cannot sample its active target.", nameof(target));
        }

        _context.ArgumentPlan.InitializeFallback(commands);
        if (!_copied && !resident)
        {
            Copy(commands);
        }

        if (_count == 0)
        {
            return;
        }

        DrawingTargetInfo2D info = _context.Target;
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
        for (uint i = 0; i < _count; i++)
        {
            var args = new DrawRangeArguments2D(_records!, _metadata!, i, 1, new(info.Description.Width, info.Description.Height), info.PixelOrigin, 0, 0);
            _scope.Draw(render, in args, 6, 1, 0, 0);
        }

        render.End();
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (Action unmap in _unmaps)
        {
            unmap();
        }

        while (_owned.Count != 0)
        {
            _owned[^1].Dispose();
            _owned.RemoveAt(_owned.Count - 1);
        }
    }

    internal static ValueTask<PreparedSparseStripBatch2D> PrepareAsync(RenderPreparationContext2D context, DrawList2D content, CancellationToken cancellationToken)
    {
        SparseStripPlan2D strips = context.CpuShapes?.GetSparse(content, context.Target, context.MaxWorkingBytes, cancellationToken)
            ?? SparseStripBuilder2D.Create(content, context.Target, context.MaxWorkingBytes, cancellationToken);
        var batch = new PreparedSparseStripBatch2D(context) { _bytes = strips.WorkingBytes };
        try
        {
            batch.Build(strips, cancellationToken);
            return ValueTask.FromResult(batch);
        }
        catch (Exception error)
        {
            context.CleanupAfterFailure(batch, error);
            throw;
        }
    }

    internal void Copy(IGraphicsCommandBuffer commands)
    {
        if (_copied)
        {
            throw new InvalidOperationException("Sparse uploads record only once.");
        }

        _copied = true;
        foreach (Action<IGraphicsCommandBuffer> copy in _copies)
        {
            copy(commands);
        }
    }

    internal void DeclareCopy(IDrawingResourceDeclarations2D resources)
    {
        foreach (Action<IDrawingResourceDeclarations2D> declare in _copyDeclarations)
        {
            declare(resources);
        }
    }

    internal void DeclareRaster(IDrawingResourceDeclarations2D resources)
    {
        DeclareResources(resources);
        foreach (Action<IDrawingResourceDeclarations2D> declare in _readDeclarations)
        {
            declare(resources);
        }
    }

    private static Vector4 Bits(int x, int y, int z, int w) => new(BitConverter.Int32BitsToSingle(x), BitConverter.Int32BitsToSingle(y), BitConverter.Int32BitsToSingle(z), BitConverter.Int32BitsToSingle(w));

    private static uint ClipBits(NormalizedClip2D clip, int x, int y)
    {
        if (clip.Kind == NormalizedClipKind2D.None)
        {
            return 65535;
        }

        ScissorRect bounds = clip.Bounds;
        if (x < bounds.X || y < bounds.Y || x >= (long)bounds.X + bounds.Width || y >= (long)bounds.Y + bounds.Height)
        {
            return 0;
        }

        if (clip.Kind == NormalizedClipKind2D.Scissor)
        {
            return 65535;
        }

        if (clip.Kind == NormalizedClipKind2D.Empty)
        {
            return 0;
        }

        if (clip.Kind == NormalizedClipKind2D.SampleMask)
        {
            int q = checked(((y - (int)bounds.Y) * (int)bounds.Width) + x - (int)bounds.X);
            return (uint)clip.Mask[q / 4][q % 4];
        }

        uint bits = 0;
        for (int sample = 0; sample < 16; sample++)
        {
            int sx = (x * 4) + (sample % 4), sy = (y * 4) + (sample / 4);
            if (sx >= clip.Samples.X && sy >= clip.Samples.Y && sx < clip.Samples.Z && sy < clip.Samples.W)
            {
                bits |= 1u << sample;
            }
        }

        return bits;
    }

    private static DrawingCommand2D PaintOnly(DrawingCommand2D command) => command switch
    {
        FillRectCommand2D c => new DrawPaintGraphCommand2D(c.Paint, c.State),
        FillRoundedRectCommand2D c => new DrawPaintGraphCommand2D(c.Paint, c.State),
        FillCircleCommand2D c => new DrawPaintGraphCommand2D(c.Paint, c.State),
        FillPathCommand2D c => new DrawPaintGraphCommand2D(c.Paint, c.State),
        StrokePathCommand2D c => new DrawPaintGraphCommand2D(c.Paint, c.State),
        DrawLineCommand2D c => new DrawPaintGraphCommand2D(c.Paint, c.State),
        DrawPaintGraphCommand2D => command,
        _ => command,
    };

    private void Build(SparseStripPlan2D plan, CancellationToken token)
    {
        // Compile paint/clip once per input. Shapes are replaced by CPU-authoritative strips.
        var templates = new DrawRecord2D[plan.Commands.Length];
        var clips = new NormalizedClip2D[plan.Commands.Length];
        var paints = new Dictionary<DrawingCommand2D, (DrawingProgram2D Program, uint Root)>();
        for (int i = 0; i < plan.Commands.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            DrawingCommand2D command = plan.Commands[i];
            DrawingCommand2D paintCommand = PaintOnly(command);
            if (!paints.TryGetValue(paintCommand, out (DrawingProgram2D Program, uint Root) compiled))
            {
                // Source/page paint aliases do not share this program's host arrays.
                // Admit each actual owner before compilation, and keep its conservative
                // capacity in every subsequent page/peak and retained-memory charge.
                long capacity = DrawingWorkingBudget2D.Validate<DrawingCommand2D>(
                    new DrawingCommand2D[] { paintCommand }, _context.Target, _context.MaxWorkingBytes - _bytes);
                _bytes = checked(_bytes + capacity);
                var program = new DrawingProgram2D(_scope, _context.Target, _context.Clips, recordLowering: true);
                uint root = program.Compile(paintCommand);
                program.ValidateFiniteData();
                compiled = (program, root);
                paints.Add(paintCommand, compiled);
                _programs.Add(program);
            }

            NormalizedClip2D clip = _context.Clips.Get(command.State.Clip, Matrix3x2.Identity, command.State.Clip.Elements);
            clips[i] = clip;
            int before = _page.Records.Count;
            _page.Add(paintCommand, compiled.Program, compiled.Root, new(0, 0, _context.Target.Description.Width, _context.Target.Description.Height), clip);
            templates[i] = _page.Records.Count > before ? _page.Records[^1] : default;
        }

        _page.Records.Clear();
        long stripCells = plan.Strips.Sum(strip => checked(2L + (strip.MaskOffset < 0 && clips[strip.CommandOrdinal].Kind == NormalizedClipKind2D.None ? 0 : (strip.Length + 3L) / 4)));
        if (checked(stripCells + _page.Numeric.Count) > 262144 || checked(_bytes + (stripCells * 64) + (plan.Strips.LongLength * 4096)) > _context.MaxWorkingBytes)
        {
            throw new NotSupportedException("Sparse strip payload exceeds the preallocation budget.");
        }

        foreach (SparseStrip2D strip in plan.Strips)
        {
            DrawRecord2D record = templates[strip.CommandOrdinal];
            if (record.Kind == 0)
            {
                continue;
            }

            // Existing paint evaluator already preserves per-sample paint/clip correlation.
            // Encode each run as a pre-normalized clip mask; no shape predicate runs on the GPU.
            uint offset = checked((uint)_page.Numeric.Count);
            NormalizedClip2D originalClip = clips[strip.CommandOrdinal];
            bool full = strip.MaskOffset < 0 && originalClip.Kind == NormalizedClipKind2D.None;
            for (int x = 0; !full && x < strip.Length; x += 4)
            {
                Vector4 bits = default;
                for (int j = 0; j < 4 && x + j < strip.Length; j++)
                {
                    uint shape = strip.MaskOffset < 0 ? ushort.MaxValue : plan.BoundaryMasks[strip.MaskOffset + x + j];
                    bits[j] = shape & ClipBits(originalClip, strip.X + x + j, strip.Y);
                }

                _page.Numeric.Add(bits);
            }

            uint clipId = checked((uint)_page.Clips.Count);
            uint header = checked((uint)_page.Numeric.Count);
            _page.Numeric.Add(full ? Bits(strip.X * 4, strip.Y * 4, (strip.X + strip.Length) * 4, (strip.Y + 1) * 4) : Bits(strip.X, strip.Y, strip.Length, 1));
            _page.Numeric.Add(new(BitConverter.UInt32BitsToSingle(offset), 0, 0, 0));

            _page.Clips.Add(new(full ? 3u : 4u, header, 2, 0));
            _page.Records.Add(record with { DeviceBounds = new(strip.X, strip.Y, strip.X + strip.Length, strip.Y + 1), ClipId = clipId });
        }

        _count = _page.Records.Count;
        if (_page.Numeric.Count > 262144)
        {
            throw new NotSupportedException("Sparse strips exceed the numeric geometry/paint/clip budget.");
        }

        _bytes = checked(_bytes + (_page.Records.Count * 4096L) + (_page.Numeric.Count * 64L) + (_page.Paints.Count * 4096L));
        if (_bytes > _context.MaxWorkingBytes)
        {
            throw new NotSupportedException("Sparse GPU paint resources exceed MaxWorkingBytes.");
        }

        if (_count == 0)
        {
            return;
        }

        IGraphicsSampler sampler = _context.ArgumentPlan.Sampler(_context.Resources);
        _scope.RegisterSampler(sampler, value => _sampler = value);
        _context.ArgumentPlan.Fallback(_scope, value => _fallback = value);
        AddShader(_page.Records, value => _records = value);
        AddShader(_page.Paints, value => _paints = value);
        AddShader(_page.Clips, value => _clips = value);
        AddNumeric(_page.Numeric);
        AddShader(_page.Masks.Count == 0 ? new List<MaskDescriptor2D> { default } : _page.Masks, value => _masks = value, () => _page.Masks.Count == 0 ? [new(default, default, 0, 0, 0, 0, _fallback!, _sampler!)] : _page.Masks.ToArray());
        AddShader(_page.Images.Count == 0 ? new List<ImagePaintData2D> { default } : _page.Images, value => _images = value, () => _page.Images.Count == 0 ? [new(default, default, default, default, 0, 0, 0, 0, 0, 0, 0, 0, _fallback!, _sampler!)] : _page.Images.ToArray());
        AddShader(new List<DrawPageMetadata2D> { default }, value => _metadata = value, () => [new(_paints!, _clips!, _numeric!, _masks!, _images!)]);
        _context.ArgumentPlan.DeferUpload(_scope, UploadAsync, MapAsync, Fill);
    }

    private void AddShader<T>(List<T> values, Action<IGpuRef<T>> resolved, Func<T[]>? late = null)
        where T : struct, IShaderArguments
    {
        ulong count = checked((ulong)Math.Max(1, values.Count));
        ShaderArtifact artifact = _pipeline.Desc.FragmentShader!.Artifact;
        IGraphicsShaderDataBuffer<T> upload = Own(_context.Device.CreateShaderDataBuffer<T>(artifact, count, MemoryPreference.Upload));
        IGraphicsShaderDataBuffer<T> data = Own(_context.Device.CreateShaderDataBuffer<T>(artifact, count));
        _scope.RegisterBuffer(data.SliceElements(0, count), resolved);
        _copyDeclarations.Add(resources =>
        {
            resources.UseShaderDataBuffer(upload, new(PipelineStage.Copy, ResourceAccess.CopyRead));
            resources.UseShaderDataBuffer(data, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        });
        _readDeclarations.Add(resources => resources.UseShaderDataBuffer(data, new(PipelineStage.VertexShader | PipelineStage.FragmentShader, ResourceAccess.ShaderRead)));
        _context.ArgumentPlan.MarkPreparedShaderData(_scope, data.SliceElements(0, count), new(PipelineStage.Copy | PipelineStage.VertexShader | PipelineStage.FragmentShader, ResourceAccess.CopyWrite | ResourceAccess.ShaderRead));
        _maps.Add(upload.MapAsync);
        _unmaps.Add(() =>
        {
            if (upload.IsMapped)
            {
                upload.Unmap();
            }
        });
        _fills.Add(() => upload.CopyFrom(late is null ? CollectionsMarshal.AsSpan(values) : late()));
        _copies.Add(commands =>
        {
            commands.Barrier(new ShaderDataBufferBarrierDesc<T> { Buffer = upload.SliceElements(0, count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.CopyBuffer(upload.SliceElements(0, count), data.SliceElements(0, count));
            commands.Barrier(new ShaderDataBufferBarrierDesc<T> { Buffer = data.SliceElements(0, count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.VertexShader | PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
        });
    }

    private void AddNumeric(List<Vector4> values)
    {
        ulong count = checked((ulong)Math.Max(1, values.Count));
        IGraphicsBuffer<Vector4> upload = Own(_context.Device.CreateBuffer<Vector4>(new() { Count = count, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }));
        IGraphicsBuffer<Vector4> data = Own(_context.Device.CreateBuffer<Vector4>(new() { Count = count, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead }));
        _scope.RegisterBuffer(data.SliceElements(0, count), value => _numeric = value);
        _copyDeclarations.Add(resources =>
        {
            resources.UseBuffer(upload, new(PipelineStage.Copy, ResourceAccess.CopyRead));
            resources.UseBuffer(data, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        });
        _readDeclarations.Add(resources => resources.UseBuffer(data, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead)));
        _context.ArgumentPlan.MarkPreparedBuffer(_scope, data.SliceElements(0, count), new(PipelineStage.Copy | PipelineStage.FragmentShader, ResourceAccess.CopyWrite | ResourceAccess.ShaderRead));
        _maps.Add(upload.MapAsync);
        _unmaps.Add(() =>
        {
            if (upload.IsMapped)
            {
                upload.Unmap();
            }
        });
        _fills.Add(() => upload.CopyFrom(CollectionsMarshal.AsSpan(values)));
        _copies.Add(commands =>
        {
            commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = upload.SliceElements(0, count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.CopyBuffer(upload.SliceElements(0, count), data.SliceElements(0, count));
            commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = data.SliceElements(0, count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
        });
    }

    private async ValueTask MapAsync(CancellationToken token)
    {
        foreach (Func<CancellationToken, ValueTask> map in _maps)
        {
            await map(token).ConfigureAwait(false);
        }
    }

    private async ValueTask UploadAsync(CancellationToken token)
    {
        await MapAsync(token).ConfigureAwait(false);
        Fill();
    }

    private void Fill()
    {
        try
        {
            _page.Resolve(null!, _sampler!);
            foreach (Action fill in _fills)
            {
                fill();
            }
        }
        finally
        {
            foreach (Action unmap in _unmaps)
            {
                unmap();
            }
        }
    }

    private T Own<T>(T owner)
        where T : IDisposable
    {
        _owned.Add(owner);
        return owner;
    }
}
