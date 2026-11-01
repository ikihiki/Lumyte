using System.Buffers;
using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class PreparedRectangleBatch2D : IPreparedCommandBatch2D
{
    private const int MaximumRectangles = 1024;
    private readonly List<IPreparedCommandBatch2D> _batches = [];

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

    public void Dispose()
    {
        while (_batches.Count != 0)
        {
            _batches[^1].Dispose();
            _batches.RemoveAt(_batches.Count - 1);
        }
    }

    internal static async ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken)
    {
        context.ValidateResources();
        cancellationToken.ThrowIfCancellationRequested();
        if (context.RectangleResources() is not { } resources)
        {
            return await PreparedGpuBatch2D.PrepareAsync(context, commands, cancellationToken).ConfigureAwait(false);
        }

        var result = new PreparedRectangleBatch2D();
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
                result._batches.Add(fast
                    ? await SolidBatch.PrepareAsync(context, resources, run, cancellationToken).ConfigureAwait(false)
                    : await PreparedGpuBatch2D.PrepareAsync(context, run, cancellationToken).ConfigureAwait(false));
                start = end;
            }

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
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

    private sealed class SolidBatch : IPreparedCommandBatch2D
    {
        private readonly RenderPreparationContext2D _context;
        private readonly SolidRectangleResources2D _resources;
        private readonly IDisposable _retained;
        private SolidRectangleResources2D.Entry? _entry;
        private SolidRectangleArguments2D _arguments;
        private int _vectors;
        private int _instances;
        private bool _recorded;
        private bool _disposed;

        private SolidBatch(RenderPreparationContext2D context, SolidRectangleResources2D resources)
        {
            (_context, _resources) = (context, resources);
            _retained = context.RetainResources();
        }

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
            SolidRectangleResources2D.Entry entry = _entry!;
            BufferSlice<Vector4> upload = entry.Upload.SliceElements(0, (ulong)_vectors);
            BufferSlice<Vector4> data = entry.Data.SliceElements(0, (ulong)_vectors);
            var shaderRead = new BarrierScope(PipelineStage.VertexShader | PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
            commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = upload, Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = data, Before = entry.Used ? shaderRead : default, After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
            commands.CopyBuffer(upload, data);
            commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = data, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = shaderRead });
            commands.Barrier(new BufferBarrierDesc<Vector4> { Buffer = upload, Before = new(PipelineStage.Copy, ResourceAccess.CopyRead), After = new(PipelineStage.Host, ResourceAccess.HostWrite) });
            entry.Used = true;
            IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = target, LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }] });
            try
            {
                render.SetPipeline(_resources.Pipeline);
                render.SetArgumentTable(entry.Table);
                render.SetRenderState(new()
                {
                    Rasterization = new() { Cull = CullMode.None },
                    ColorTargets = [new() { BlendEnable = true, Color = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha }, Alpha = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha } }],
                });
                render.SetViewport(new(0, 0, _context.Target.Description.Width, _context.Target.Description.Height, 0, 1));
                render.SetScissor(new(0, 0, _context.Target.Description.Width, _context.Target.Description.Height));
                render.SetBlendConstant(new(0, 0, 0, 0));
                render.SetStencilReference(0);
                render.SetArguments(in _arguments);
                render.Draw(6, (uint)_instances);
            }
            finally
            {
                render.End();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_entry is { } entry)
            {
                _resources.Return(entry);
                _entry = null;
            }

            _retained.Dispose();
            _disposed = true;
        }

        internal static async ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, SolidRectangleResources2D resources, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken)
        {
            var batch = new SolidBatch(context, resources);
            Vector4[]? values = null;
            try
            {
                int capacity = commands.Length * 6;
                ClipStack2D? previous = null;
                foreach (FillRectCommand2D command in commands.Span)
                {
                    if (!ReferenceEquals(previous, command.State.Clip))
                    {
                        capacity += command.State.Clip.Elements.Count * 3;
                        previous = command.State.Clip;
                    }
                }

                values = ArrayPool<Vector4>.Shared.Rent(capacity);
                batch._instances = commands.Length;
                batch._vectors = Fill(context.Target, commands.Span, values);
                batch._entry = resources.Rent(batch._vectors);
                await batch._entry.Upload.MapAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    batch._entry.Upload.SliceElements(0, (ulong)batch._vectors).CopyFrom(values.AsSpan(0, batch._vectors));
                }
                finally
                {
                    batch._entry.Upload.Unmap();
                }

                cancellationToken.ThrowIfCancellationRequested();
                batch._arguments = new(batch._entry.Table.WriteBuffer(0, batch._entry.Data.SliceElements(0, (ulong)batch._vectors)), new(context.Target.Description.Width, context.Target.Description.Height));
                return batch;
            }
            catch
            {
                if (batch._entry is { } entry)
                {
                    entry.Discard = true;
                }

                batch.Dispose();
                throw;
            }
            finally
            {
                if (values is not null)
                {
                    ArrayPool<Vector4>.Shared.Return(values);
                }
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
    }
}
