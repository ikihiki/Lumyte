using System.Buffers;
using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class PreparedRectangleBatch2D : IRetainedCommandBatch2D
{
    private const int MaximumRectangles = 1024;
    private readonly List<IRetainedCommandBatch2D> _batches = [];
    private readonly RenderPreparationContext2D _context;
    private bool _disposed;

    private PreparedRectangleBatch2D(RenderPreparationContext2D context)
    {
        _context = context;
    }

    public long EstimatedBytes => checked(256 + _batches.Sum(batch => batch.EstimatedBytes));

    public void DeclareResources(IDrawingResourceDeclarations2D resources)
    {
        foreach (IPreparedCommandBatch2D batch in _batches)
        {
            batch.DeclareResources(resources);
        }
    }

    public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
    {
        for (int i = 0; i < _batches.Count; i++)
        {
            if (i != 0)
            {
                var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
                commands.Barrier(new TextureBarrierDesc { Texture = target.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = attachment, After = attachment });
            }

            _batches[i].Record(commands, target);
        }
    }

    public void RecordRetained(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _context.ValidateResources();
        for (int i = 0; i < _batches.Count; i++)
        {
            if (i != 0)
            {
                var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
                commands.Barrier(new TextureBarrierDesc { Texture = target.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = attachment, After = attachment });
            }

            _batches[i].RecordRetained(commands, target, resident);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        while (_batches.Count != 0)
        {
            _batches[^1].Dispose();
            _batches.RemoveAt(_batches.Count - 1);
        }
    }

    internal static async ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken)
    {
        using var profile = DrawingProfileScope.Start(5);
        long runs = 0;
        try
        {
            context.ValidateResources();
            cancellationToken.ThrowIfCancellationRequested();
            if (context.RectangleResources() is not { } resources)
            {
                IPreparedCommandBatch2D fallback = await PreparedGpuBatch2D.PrepareAsync(context, commands, cancellationToken).ConfigureAwait(false);
                profile?.Success();
                return fallback;
            }

            var result = new PreparedRectangleBatch2D(context);
            try
            {
                int start = 0;
                while (start < commands.Length)
                {
                    bool fast = Eligible(commands.Span[start], context.Target);
                    int end = start + 1;
                    while (end < commands.Length && end - start < MaximumRectangles && Eligible(commands.Span[end], context.Target) == fast)
                    {
                        end++;
                    }

                    ReadOnlyMemory<FillRectCommand2D> run = commands.Slice(start, end - start);
                    result._batches.Add((IRetainedCommandBatch2D)(fast
                        ? await SolidBatch.PrepareAsync(context, resources, run, cancellationToken).ConfigureAwait(false)
                        : await PreparedGpuBatch2D.PrepareAsync(context, run, cancellationToken).ConfigureAwait(false)));
                    runs++;
                    start = end;
                }

                profile?.Success();
                return result;
            }
            catch (Exception error)
            {
                context.CleanupAfterFailure(result, error);
                throw;
            }
        }
        catch (Exception error)
        {
            profile?.Failure(error);
            throw;
        }
        finally
        {
            profile?.Counter(9, runs);
        }
    }

    private static bool Eligible(FillRectCommand2D command, DrawingTargetInfo2D target)
    {
        if (command.Paint.Root is not SolidPaintNode2D || command.State.Clip.Elements.Count > 8 || !Inverse(command.State.Transform, target, out _))
        {
            return false;
        }

        IReadOnlyList<ClipElement2D> clips = command.State.Clip.Elements;
        for (int i = 0; i < clips.Count; i++)
        {
            ClipElement2D clip = clips[i];
            if (!Rectangle(clip.Path, out _) || !Inverse(clip.Transform, target, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Inverse(Matrix3x2 transform, DrawingTargetInfo2D target, out Matrix3x2 inverse)
    {
        Matrix3x2 pixels = Matrix3x2.CreateScale(target.PixelScale) * Matrix3x2.CreateTranslation(-target.PixelOrigin);
        return Matrix3x2.Invert(transform * pixels, out inverse)
            && float.IsFinite(inverse.M11) && float.IsFinite(inverse.M12) && float.IsFinite(inverse.M21)
            && float.IsFinite(inverse.M22) && float.IsFinite(inverse.M31) && float.IsFinite(inverse.M32);
    }

    private static bool Rectangle(Path2D path, out Rect2D rect)
    {
        IReadOnlyList<PathSegment2D> segments = path.Segments;
        rect = default;
        if (segments.Count != 5 || segments[0].Verb != PathVerb2D.Move || segments[1].Verb != PathVerb2D.Line
            || segments[2].Verb != PathVerb2D.Line || segments[3].Verb != PathVerb2D.Line || segments[4].Verb != PathVerb2D.Close)
        {
            return false;
        }

        Vector2 a = segments[0].P0;
        Vector2 b = segments[1].P0;
        Vector2 c = segments[2].P0;
        Vector2 d = segments[3].P0;
        if (a.Y != b.Y || b.X != c.X || c.Y != d.Y || d.X != a.X || b.X < a.X || c.Y < a.Y)
        {
            return false;
        }

        rect = new(a.X, a.Y, b.X - a.X, c.Y - a.Y);
        return true;
    }

    private sealed class SolidBatch : IRetainedCommandBatch2D
    {
        private readonly RenderPreparationContext2D _context;
        private readonly SolidRectangleResources2D _resources;
        private readonly IDisposable _retained;
        private readonly IDisposable _tableLease;
        private SolidRectangleResources2D.Entry? _entry;
        private SolidRectangleArguments2D _arguments;
        private ShaderResources? _reservation;
        private int _vectors;
        private int _instances;
        private bool _recorded;
        private bool _disposed;

        private SolidBatch(RenderPreparationContext2D context, SolidRectangleResources2D resources)
        {
            (_context, _resources) = (context, resources);
            _retained = context.RetainResources();
            _tableLease = context.ArgumentPlan.Retain();
        }

        public long EstimatedBytes => checked(4096 + (long)(_entry?.Bytes ?? 0) + _context.ArgumentPlan.EstimatedBytes);

        public void DeclareResources(IDrawingResourceDeclarations2D resources)
        {
            ArgumentNullException.ThrowIfNull(resources);
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_recorded)
            {
                throw new InvalidOperationException("A rectangle batch can be recorded only once.");
            }

            _context.ValidateResources();
            _recorded = true;
            RecordCore(commands, target, resident: false);
        }

        public void RecordRetained(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident)
        {
            ArgumentNullException.ThrowIfNull(commands);
            ArgumentNullException.ThrowIfNull(target);
            ObjectDisposedException.ThrowIf(_disposed, this);
            _context.ValidateResources();
            RecordCore(commands, target, resident);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_entry is { } entry)
            {
                if (entry.Upload.IsMapped)
                {
                    entry.Upload.Unmap();
                }

                _resources.Return(entry);
                _entry = null;
            }

            _tableLease.Dispose();
            _retained.Dispose();
            _disposed = true;
        }

        internal static ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, SolidRectangleResources2D resources, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken)
        {
            var batch = new SolidBatch(context, resources);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                int capacity = commands.Length * 6;
                ClipStack2D? previous = null;
                foreach (FillRectCommand2D command in commands.Span)
                {
                    if (!ReferenceEquals(previous, command.State.Clip))
                    {
                        capacity = checked(capacity + (command.State.Clip.Elements.Count * 3));
                        previous = command.State.Clip;
                    }
                }

                batch._instances = commands.Length;
                batch._vectors = capacity;
                batch._entry = resources.Rent(capacity);
                ShaderResources reservation = context.ArgumentPlan.Reserve();
                batch._reservation = reservation;
                ArgumentRegistration<Vector4> data = reservation.RegisterBuffer(batch._entry.Data.SliceElements(0, (ulong)capacity));
                context.ArgumentPlan.MarkPreparedBuffer(reservation, batch._entry.Data, new(PipelineStage.Copy | PipelineStage.VertexShader, ResourceAccess.CopyWrite | ResourceAccess.ShaderRead));
                context.ArgumentPlan.DeferUpload(
                    reservation,
                    async token =>
                    {
                        await batch._entry.Upload.MapAsync(token).ConfigureAwait(false);
                        UploadMapped();
                    },
                    token => batch._entry.Upload.MapAsync(token),
                    UploadMapped);
                return ValueTask.FromResult<IPreparedCommandBatch2D>(batch);

                void UploadMapped()
                {
                    using var profile = DrawingProfileScope.Start(9);
                    Vector4[]? values = null;
                    long copied = 0;
                    try
                    {
                        values = ArrayPool<Vector4>.Shared.Rent(capacity);
                        int count = Fill(context.Target, commands.Span, values);
                        if (count != capacity)
                        {
                            throw new InvalidOperationException("Rectangle preflight and payload sizes differ.");
                        }

                        if (!batch._entry.Upload.IsMapped)
                        {
                            throw new InvalidOperationException("Prepare the rectangle staging map before materialization.");
                        }

                        try
                        {
                            batch._entry.Upload.SliceElements(0, (ulong)count).CopyFrom(values.AsSpan(0, count));
                            copied = (long)count * 16;
                        }
                        finally
                        {
                            batch._entry.Upload.Unmap();
                        }

                        batch._arguments = new(data.Reference, new(context.Target.Description.Width, context.Target.Description.Height));
                        profile?.Success();
                    }
                    catch (Exception error)
                    {
                        batch._entry.Discard = true;
                        profile?.Failure(error);
                        throw;
                    }
                    finally
                    {
                        if (values is not null)
                        {
                            ArrayPool<Vector4>.Shared.Return(values);
                        }

                        profile?.Counter(7, capacity);
                        profile?.Counter(8, copied);
                        profile?.Counter(10, commands.Length);
                    }
                }
            }
            catch (Exception error)
            {
                if (batch._entry is { } entry)
                {
                    entry.Discard = true;
                }

                context.CleanupAfterFailure(batch, error);
                throw;
            }
        }

        private static int Fill(DrawingTargetInfo2D target, ReadOnlySpan<FillRectCommand2D> commands, Span<Vector4> values)
        {
            int cursor = commands.Length * 6;
            ClipStack2D? previous = null;
            int clips = cursor;
            Matrix3x2 pixels = Matrix3x2.CreateScale(target.PixelScale) * Matrix3x2.CreateTranslation(-target.PixelOrigin);
            for (int i = 0; i < commands.Length; i++)
            {
                FillRectCommand2D command = commands[i];
                int offset = i * 6;
                Vector2 minimum = Vector2.Zero;
                Vector2 maximum = new(target.Description.Width, target.Description.Height);
                bool aligned = command.State.Transform.M12 == 0 && command.State.Transform.M21 == 0;
                Intersect(ref minimum, ref maximum, command.Rect, command.State.Transform * pixels);
                Write(values, offset + 1, command.Rect, command.State.Transform * pixels);
                values[offset + 4] = DrawingProgram2D.Linear(((SolidPaintNode2D)command.Paint.Root).Color);
                if (!ReferenceEquals(previous, command.State.Clip))
                {
                    clips = cursor;
                    for (int j = 0; j < command.State.Clip.Elements.Count; j++)
                    {
                        ClipElement2D clip = command.State.Clip.Elements[j];
                        _ = Rectangle(clip.Path, out Rect2D rect);
                        Write(values, cursor, rect, clip.Transform * pixels);
                        cursor += 3;
                    }

                    previous = command.State.Clip;
                }

                for (int j = 0; j < command.State.Clip.Elements.Count; j++)
                {
                    ClipElement2D clip = command.State.Clip.Elements[j];
                    _ = Rectangle(clip.Path, out Rect2D rect);
                    Intersect(ref minimum, ref maximum, rect, clip.Transform * pixels);
                    aligned &= clip.Transform.M12 == 0 && clip.Transform.M21 == 0;
                }

                float x = Math.Clamp(MathF.Floor(minimum.X), 0, target.Description.Width);
                float y = Math.Clamp(MathF.Floor(minimum.Y), 0, target.Description.Height);
                float right = Math.Clamp(MathF.Ceiling(maximum.X), x, target.Description.Width);
                float bottom = Math.Clamp(MathF.Ceiling(maximum.Y), y, target.Description.Height);
                values[offset] = new(x, y, right - x, bottom - y);
                values[offset + 5] = new(clips, command.State.Clip.Elements.Count, aligned ? 1 : 0, 0);
            }

            return cursor;
        }

        private static void Write(Span<Vector4> values, int offset, Rect2D rect, Matrix3x2 transform)
        {
            _ = Matrix3x2.Invert(transform, out Matrix3x2 inverse);
            values[offset] = new(rect.X, rect.Y, rect.Width, rect.Height);
            values[offset + 1] = new(inverse.M11, inverse.M21, inverse.M31, 0);
            values[offset + 2] = new(inverse.M12, inverse.M22, inverse.M32, 0);
        }

        private static void Intersect(ref Vector2 minimum, ref Vector2 maximum, Rect2D rect, Matrix3x2 transform)
        {
            var a = Vector2.Transform(new(rect.X, rect.Y), transform);
            var b = Vector2.Transform(new(rect.X + rect.Width, rect.Y), transform);
            var c = Vector2.Transform(new(rect.X, rect.Y + rect.Height), transform);
            var d = Vector2.Transform(new(rect.X + rect.Width, rect.Y + rect.Height), transform);
            var low = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            var high = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            if (!float.IsFinite(low.X) || !float.IsFinite(low.Y) || !float.IsFinite(high.X) || !float.IsFinite(high.Y))
            {
                throw new NotSupportedException("Transformed drawing bounds exceed the finite coordinate range.");
            }

            minimum = Vector2.Max(minimum, low - Vector2.One);
            maximum = Vector2.Min(maximum, high + Vector2.One);
        }

        private void RecordCore(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident)
        {
            SolidRectangleResources2D.Entry entry = _entry!;
            if (!resident)
            {
                BufferSlice<Vector4> upload = entry.Upload.SliceElements(0, (ulong)_vectors);
                BufferSlice<Vector4> data = entry.Data.SliceElements(0, (ulong)_vectors);
                var shaderRead = new BarrierScope(PipelineStage.VertexShader | PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
                commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = upload, Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
                commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = data, Before = entry.Used ? shaderRead : default, After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
                commands.CopyBuffer(upload, data);
                commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = data, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = shaderRead });
                commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = upload, Before = new(PipelineStage.Copy, ResourceAccess.CopyRead), After = new(PipelineStage.Host, ResourceAccess.HostWrite) });
                entry.Used = true;
            }

            IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = target, LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }] });
            try
            {
                render.SetPipeline(_resources.Pipeline);
                render.SetRenderState(new()
                {
                    Rasterization = new() { Cull = CullMode.None },
                    ColorTargets = [new() { BlendEnable = true, Color = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha }, Alpha = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha } }],
                });
                render.SetViewport(new(0, 0, _context.Target.Description.Width, _context.Target.Description.Height, 0, 1));
                render.SetScissor(new(0, 0, _context.Target.Description.Width, _context.Target.Description.Height));
                render.SetBlendConstant(new(0, 0, 0, 0));
                render.SetStencilReference(0);
                _reservation!.Draw(render, in _arguments, 6, (uint)_instances);
            }
            finally
            {
                render.End();
            }
        }
    }
}
