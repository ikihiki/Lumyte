using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>CPU encoding only: raw curves, finite reservations, paint and normalized clips.</summary>
internal sealed class ComputeExecutionPlan2D
{
    internal DrawRecordPage2D Page { get; } = new();

    internal List<Vector4> Input { get; } = [];

    internal HashSet<IGraphicsTextureView> Views { get; } = new(ReferenceEqualityComparer.Instance);

    internal HashSet<IGraphicsSampler> Samplers { get; } = new(ReferenceEqualityComparer.Instance);

    internal uint[] Configuration { get; } = new uint[16];

    internal uint StorageCount { get; private set; }

    internal uint WorkCount { get; private set; }

    internal uint BackdropRows { get; private set; }

    internal long EstimatedBytes { get; private set; }

    internal uint ColorOffset => Configuration[8];

    internal uint PixelCount => checked(Configuration[2] * Configuration[3]);

    internal static uint Groups(long count, DeviceCaps caps)
    {
        if (caps.MaxComputeInvocationsPerWorkgroup < 256 || caps.MaxComputeWorkgroupSizeX < 16 || caps.MaxComputeWorkgroupSizeY < 16 || caps.MaxComputeWorkgroupSizeZ < 1)
        {
            throw new NotSupportedException("Tiled compute requires 16x16 workgroups.");
        }

        long groups = Math.Max(1, checked((count + 255) / 256));
        if (groups > caps.MaxComputeWorkgroupsPerDimension)
        {
            throw new NotSupportedException("Tiled compute dispatch exceeds the device limit.");
        }

        return checked((uint)groups);
    }

    internal static ComputeExecutionPlan2D Encode(RenderPreparationContext2D context, ReadOnlyMemory<DrawingCommand2D> commands, ShaderResources scope, CancellationToken token)
    {
        try
        {
            return EncodeChecked(context, commands, scope, token);
        }
        catch (OverflowException error)
        {
            throw new NotSupportedException("Tiled compute scene capacity overflow.", error);
        }
    }

    internal static long CandidateTiles(Vector4 bounds, uint width, uint height)
    {
        long left = Math.Clamp((long)Math.Floor(bounds.X / 16), 0, (width + 15L) / 16);
        long top = Math.Clamp((long)Math.Floor(bounds.Y / 16), 0, (height + 15L) / 16);
        long right = Math.Clamp((long)Math.Ceiling(bounds.Z / 16), left, (width + 15L) / 16);
        long bottom = Math.Clamp((long)Math.Ceiling(bounds.W / 16), top, (height + 15L) / 16);
        return checked((right - left) * (bottom - top));
    }

    private static ComputeExecutionPlan2D EncodeChecked(RenderPreparationContext2D context, ReadOnlyMemory<DrawingCommand2D> commands, ShaderResources scope, CancellationToken token)
    {
        var result = new ComputeExecutionPlan2D();
        DeviceCaps caps = context.Device.Caps;
        uint width = context.Target.Description.Width, height = context.Target.Description.Height;
        _ = Groups(checked((long)width * height), caps);
        if (context.MaxWorkingBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context));
        }

        if (checked((commands.Length * 512L) + ((long)width * height * 16) + 8192) > context.MaxWorkingBytes)
        {
            throw new NotSupportedException("Tiled compute scene exceeds the initial working-byte reservation.");
        }

        var geometry = new List<ComputeGeometryPlan2D?>();
        ComputeGeometryCapacity2D total = default;
        Matrix3x2 pixels = Matrix3x2.CreateScale(context.Target.PixelScale) * Matrix3x2.CreateTranslation(-context.Target.PixelOrigin);
        ulong maximum = Math.Min(caps.MaxBufferSize, caps.ArgumentTableBindingLimits.MaximumBufferRangeBytes);
        if (caps.MaxStorageBufferBindingSize != 0)
        {
            maximum = Math.Min(maximum, caps.MaxStorageBufferBindingSize);
        }

        foreach (DrawingCommand2D command in commands.Span)
        {
            token.ThrowIfCancellationRequested();
            long remaining = checked(context.MaxWorkingBytes - total.WorkingBytes - ((long)width * height * 16) - (result.Page.Numeric.Count * 64L) - (result.Page.Records.Count * 384L) - 8192);
            if (remaining <= 0)
            {
                throw new NotSupportedException("Tiled compute exhausted its cumulative preparation reservation.");
            }

            ComputeGeometryPlan2D? path = null;
            DrawingCommand2D lowered = command;
            switch (command)
            {
                case FillPathCommand2D fill:
                    path = context.CpuShapes?.GetGeometry(fill.Path, fill.State.Transform * pixels, fill.Rule, null, remaining)
                        ?? ComputeGeometryPlan2D.Create(fill.Path, fill.State.Transform * pixels, fill.Rule, maxWorkingBytes: remaining);
                    lowered = new DrawPaintGraphCommand2D(fill.Paint, fill.State);
                    break;
                case StrokePathCommand2D stroke:
                    path = context.CpuShapes?.GetGeometry(stroke.Path, stroke.State.Transform * pixels, FillRule2D.NonZero, stroke.Style, remaining)
                        ?? ComputeGeometryPlan2D.Create(stroke.Path, stroke.State.Transform * pixels, stroke: stroke.Style, maxWorkingBytes: remaining);
                    lowered = new DrawPaintGraphCommand2D(stroke.Paint, stroke.State);
                    break;
                case DrawLineCommand2D line:
                    var builder = new PathBuilder2D();
                    builder.MoveTo(line.Start);
                    builder.LineTo(line.End);
                    path = ComputeGeometryPlan2D.Create(builder.Build(), line.State.Transform * pixels, stroke: line.Style, maxWorkingBytes: remaining);
                    lowered = new DrawPaintGraphCommand2D(line.Paint, line.State);
                    break;
                case FillRectCommand2D or FillRoundedRectCommand2D or FillCircleCommand2D or DrawImageCommand2D or DrawCoverageMaskCommand2D or DrawDistanceFieldCommand2D or DrawMeshCommand2D or DrawPaintGraphCommand2D:
                    break;
                default:
                    throw new NotSupportedException("Tiled compute does not implement custom drawing commands: " + command.GetType().Name);
            }

            if (path?.Stroke is StrokeStyle2D logicalStroke)
            {
                // Stroke expansion precedes the affine transform. Bound logical
                // intermediates as well as the device hull rather than allowing
                // a tiny transform to hide overflow in directions and offsets.
                if ((double)logicalStroke.Width * Math.Max(1, logicalStroke.MiterLimit) > 1048576 ||
                    path.Curves.Any(curve => !Bounded(curve.P0) || !Bounded(curve.P1) || !Bounded(curve.P2) || !Bounded(curve.P3)))
                {
                    throw new NotSupportedException("Stroke logical intermediates exceed the supported float32 range.");
                }
            }

            if (path is not null && path.Curves.Count == 0)
            {
                continue;
            }

            NormalizedClip2D clip = context.Clips.Get(command.State.Clip, Matrix3x2.Identity, command.State.Clip.Elements);
            if (clip.Kind == NormalizedClipKind2D.Empty)
            {
                continue;
            }

            ScissorRect bounds = DrawingBounds2D.GetScissor(command, null, context.Target, null, false, false, clip.Bounds);
            if (bounds.Width == 0 || bounds.Height == 0)
            {
                continue;
            }

            var program = new DrawingProgram2D(scope, context.Target, context.Clips, recordLowering: true);

            // Never compile an original path here: the old compiler flattens paths.
            uint root = program.Compile(lowered);
            program.ValidateFiniteData();

            // Whole-table projection keeps even transparent, root-unreachable
            // resource registrations active; declare their borrowed identities.
            result.Views.UnionWith(program.Views);
            result.Samplers.UnionWith(program.Samplers);
            if (!result.Page.FitsAddition(program, root, clip, default, maximum, out _, out _))
            {
                throw new NotSupportedException("Tiled compute paint/clip input exceeds a buffer binding or geometry budget.");
            }

            int before = result.Page.Records.Count;
            result.Page.Add(lowered, program, root, bounds, clip);
            if (result.Page.Records.Count == before)
            {
                continue;
            }

            geometry.Add(path);
            if (path is not null)
            {
                total = total.Combine(path.Capacity);
            }

            total.Validate(
                context.MaxWorkingBytes,
                result.Page.Numeric.Count,
                checked(8192 + ((long)width * height * 16) + (commands.Length * 512L)));
        }

        long curveCount = 0, rawCount = 0, geometryCount = 0, tiles = 0, backdropRows = 0, candidates = 0;
        foreach (ComputeGeometryPlan2D? path in geometry)
        {
            if (path is null)
            {
                continue;
            }

            curveCount = checked(curveCount + path.Curves.Count);
            rawCount = checked(rawCount + (path.Stroke is null ? 0 : path.Capacity.Edges));
            geometryCount = checked(geometryCount + (path.Stroke is null ? path.Capacity.Edges : path.Capacity.Triangles * 3));
            tiles = checked(tiles + path.TileBounds.Count);
            backdropRows = checked(backdropRows + (path.Stroke is null ? path.TileBounds.Height : 0));
        }

        foreach (DrawRecord2D record in result.Page.Records)
        {
            candidates = checked(candidates + CandidateTiles(record.DeviceBounds, width, height));
        }

        if (checked(total.EdgeReferences + candidates) > 4194304)
        {
            throw new NotSupportedException("Tiled compute exceeds 4194304 edge and draw references.");
        }

        long colorOffset = checked((long)(DrawRecordPage2D.AlignedStart(checked((ulong)(rawCount + geometryCount) * 16), Math.Max(1, caps.StorageBufferOffsetAlignment), 16) / 16));
        long pixelsCount = checked((long)width * height);
        long targetTiles = checked(((width + 15L) / 16) * ((height + 15L) / 16));
        uint[] config = result.Configuration;
        config[0] = checked((uint)geometry.Count);
        config[1] = checked((uint)curveCount);
        config[2] = width;
        config[3] = height;
        config[4] = checked((uint)tiles);
        config[5] = checked((uint)targetTiles);
        config[6] = checked((uint)total.EdgeReferences);
        config[7] = checked((uint)candidates);
        config[8] = checked((uint)colorOffset);
        long work = checked(1 + geometry.Count);
        config[9] = Take(tiles);
        config[10] = Take(tiles);
        config[11] = Take(total.EdgeReferences);
        config[12] = Take(checked(tiles * 64));
        config[13] = Take(targetTiles);
        config[14] = Take(targetTiles);
        config[15] = Take(candidates);
        result.WorkCount = checked((uint)Math.Max(1, work));
        result.StorageCount = checked((uint)Math.Max(1, colorOffset + pixelsCount));
        result.BackdropRows = checked((uint)(backdropRows * 64));
        long inputCount = checked(4 + (geometry.Count * 8L) + (curveCount * 3));

        // Includes raw managed plans, List growth, upload+destination, packing
        // arrays, page streams, descriptors and the 64 sample-row backdrop.
        long pageBytes = checked(((result.Page.Records.Count * 96L) + (result.Page.Paints.Count * 16L) + (result.Page.Clips.Count * 16L) + (Math.Max(1, result.Page.Numeric.Count) * 16L) + (Math.Max(1, result.Page.Images.Count) * 128L) + (Math.Max(1, result.Page.Masks.Count) * 80L) + 80) * 4);
        long extraBytes = checked((inputCount * 64) + (work * 4) + ((colorOffset + pixelsCount) * 16) + pageBytes + 32768 + (geometry.Count * 512L));
        total.Validate(context.MaxWorkingBytes, result.Page.Numeric.Count, extraBytes);
        result.EstimatedBytes = checked(total.WorkingBytes + extraBytes);
        ArgumentTableBindingLimits limits = caps.ArgumentTableBindingLimits;
        uint textureBindings = Math.Max((uint)result.Views.Count, limits.MinimumTextureBindings);
        uint samplerBindings = Math.Max(checked((uint)result.Samplers.Count + 1), limits.MinimumSamplerBindings);
        ulong combined = limits.ReservedBindings + (7UL * limits.BufferBindingWeight) +
            ((ulong)textureBindings * limits.TextureBindingWeight) + ((ulong)samplerBindings * limits.SamplerBindingWeight);
        if (limits.BufferSlots < 7 || limits.BuffersPerDraw < 7 || textureBindings > limits.TexturesPerDraw ||
            textureBindings > limits.TextureSlots || samplerBindings > limits.SamplersPerDraw || samplerBindings > limits.SamplerSlots ||
            combined > limits.CombinedBindings)
        {
            throw new NotSupportedException("Tiled compute descriptor set exceeds device argument-table admission.");
        }

        CheckBinding(checked((inputCount + Math.Max(1, result.Page.Numeric.Count)) * 16));
        CheckBinding(work * 4);
        CheckBinding((colorOffset + pixelsCount) * 16);
        CheckBinding(Math.Max(1, result.Page.Records.Count) * 96L);
        foreach (long size in new[] { Math.Max(1, result.Page.Numeric.Count) * 16L, (result.Page.Paints.Count + result.Page.Clips.Count) * 16L, Math.Max(1, result.Page.Images.Count) * 128L, Math.Max(1, result.Page.Masks.Count) * 80L })
        {
            CheckBinding(size);
        }

        _ = Groups(Math.Max(result.StorageCount, result.WorkCount), caps);
        _ = Groups(curveCount, caps);
        _ = Groups(geometry.Count, caps);
        _ = Groups(tiles, caps);
        _ = Groups(result.BackdropRows, caps);
        _ = Groups(targetTiles, caps);
        for (int i = 0; i < 4; i++)
        {
            result.Input.Add(Bits(config[i * 4], config[(i * 4) + 1], config[(i * 4) + 2], config[(i * 4) + 3]));
        }

        long rawCursor = 0, geometryCursor = rawCount, tileCursor = 0, curveCursor = 0;
        for (int i = 0; i < geometry.Count; i++)
        {
            ComputeGeometryPlan2D? path = geometry[i];
            if (path is null)
            {
                for (int cell = 0; cell < 8; cell++)
                {
                    result.Input.Add(default);
                }

                continue;
            }

            ComputeTileBounds2D b = path.TileBounds;
            long primitives = path.Stroke is null ? path.Capacity.Edges : path.Capacity.Triangles;
            result.Input.Add(Bits((uint)curveCursor, (uint)path.Curves.Count, (uint)rawCursor, (uint)path.Capacity.Edges));
            result.Input.Add(Bits((uint)geometryCursor, (uint)primitives, (uint)tileCursor, (uint)b.Count));
            result.Input.Add(Bits(unchecked((uint)b.X), unchecked((uint)b.Y), (uint)b.Width, (uint)b.Height));
            Matrix3x2 m = path.Transform;
            result.Input.Add(new(m.M11, m.M21, m.M31, 0));
            result.Input.Add(new(m.M12, m.M22, m.M32, 0));
            StrokeStyle2D? stroke = path.Stroke;
            result.Input.Add(stroke is null ? default : new(stroke.Width, stroke.MiterLimit, (uint)stroke.Cap, (uint)stroke.Join));
            result.Input.Add(Bits(stroke is null ? 0u : 1u, (uint)path.RoundDivisions, (uint)path.FillRule, 0));
            result.Input.Add(default);
            rawCursor = checked(rawCursor + (stroke is null ? 0 : path.Capacity.Edges));
            geometryCursor = checked(geometryCursor + (stroke is null ? primitives : primitives * 3));
            tileCursor = checked(tileCursor + b.Count);
            curveCursor = checked(curveCursor + path.Curves.Count);
        }

        for (int i = 0; i < geometry.Count; i++)
        {
            if (geometry[i] is not ComputeGeometryPlan2D path)
            {
                continue;
            }

            foreach (ComputeCurveInput2D curve in path.Curves)
            {
                result.Input.Add(new(curve.P0, curve.P1.X, curve.P1.Y));
                result.Input.Add(new(curve.P2, curve.P3.X, curve.P3.Y));
                result.Input.Add(Bits((uint)curve.Verb, (uint)curve.SubdivisionCount, (uint)curve.EdgeOffset, (uint)i));
            }
        }

        return result;

        uint Take(long count)
        {
            uint start = checked((uint)work);
            work = checked(work + count);
            return start;
        }

        void CheckBinding(long bytes)
        {
            if (bytes < 0 || (ulong)bytes > maximum)
            {
                throw new NotSupportedException("Tiled compute allocation exceeds the device buffer binding limit.");
            }
        }
    }

    private static bool Bounded(Vector2 point) => MathF.Max(MathF.Abs(point.X), MathF.Abs(point.Y)) <= 1048576;

    private static Vector4 Bits(uint a, uint b, uint c, uint d) => new(BitConverter.UInt32BitsToSingle(a), BitConverter.UInt32BitsToSingle(b), BitConverter.UInt32BitsToSingle(c), BitConverter.UInt32BitsToSingle(d));
}
