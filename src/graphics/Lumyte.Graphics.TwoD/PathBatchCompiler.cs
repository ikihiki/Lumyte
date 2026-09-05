using System.Numerics;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.TwoD;

/// <summary>Packs preserved path commands for the portable GPU preparation kernel.</summary>
internal static class PathBatchCompiler
{
    private const int InputHeaderVectors = 12;
    private const int OutputHeaderVectors = 10;
    private const int MaximumCurveSubdivision = 64;
    private const float ScreenTileSize = 16;
    private const int MaximumOutputBytes = 128 * 1024 * 1024;

    public static PathBatchData Compile(RecordedCommand command, GpuTextureDescription target)
    {
        PathGeometry path = command.Path
            ?? throw new InvalidOperationException("Path command has no geometry.");
        if (command.Stroke is { Dashes.Length: > 0 })
        {
            throw new NotSupportedException(
                "Dashed path expansion is not part of the phase-three compute kernel yet.");
        }

        float scale = MaximumScale(command.Transform);
        float tolerance = 0.25f / MathF.Max(scale, 0.0001f);
        Rect bounds = command.Bounds;
        float tileSize = ScreenTileSize / MathF.Max(scale, 0.0001f);
        uint columns = checked((uint)Math.Clamp(Math.Ceiling(bounds.Width / tileSize), 1, 256));
        uint rows = checked((uint)Math.Clamp(Math.Ceiling(bounds.Height / tileSize), 1, 256));
        tileSize = MathF.Max(
            MathF.Max(bounds.Width / columns, bounds.Height / rows),
            0.0001f);

        int mainCapacity = EdgeCapacity(path);
        PathGeometry? clipPath = command.PathClip?.Geometry;
        int clipCapacity = clipPath is null ? 0 : EdgeCapacity(clipPath);
        int tileCount = checked((int)(columns * rows));
        int edgeStart = OutputHeaderVectors;
        int clipStart = checked(edgeStart + mainCapacity);
        int tableStart = checked(clipStart + clipCapacity);
        int indexStart = checked(tableStart + tileCount);
        int outputVectorCount = checked(indexStart + mainCapacity * tileCount);
        int outputBytes = checked(outputVectorCount * Marshal.SizeOf<Vector4>());
        if (outputBytes > MaximumOutputBytes)
        {
            throw new NotSupportedException(
                "The path exceeds the 128 MiB phase-three preparation limit.");
        }

        int mainStart = InputHeaderVectors;
        int clipStartInput = checked(mainStart + path.Segments.Count * 2);
        var input = new Vector4[checked(clipStartInput + (clipPath?.Segments.Count ?? 0) * 2)];
        input[0] = command.Brush.Color.Premultiplied();
        input[1] = command.Brush.SecondaryColor.Premultiplied();
        input[2] = new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        Matrix3x2 transform = command.Transform;
        input[3] = new(transform.M11, transform.M12, transform.M21, transform.M22);
        input[4] = new(transform.M31, transform.M32, target.Width, target.Height);
        input[5] = new(
            path.Segments.Count,
            (float)command.FillRule,
            command.Stroke?.Width ?? 0,
            (float)command.Brush.Kind);
        input[6] = new(
            command.Brush.Start.X,
            command.Brush.Start.Y,
            command.Brush.End.X,
            command.Brush.End.Y);
        input[7] = new(columns, rows, tileSize, tableStart);
        input[8] = new(
            indexStart,
            clipStart,
            clipPath?.Segments.Count ?? 0,
            (float)(command.PathClip?.FillRule ?? FillRule.NonZero));
        input[9] = new(bounds.X, bounds.Y, edgeStart, mainStart);
        input[10] = new(mainCapacity, clipCapacity, clipStartInput, tolerance);
        input[11] = new(
            command.Stroke?.MiterLimit ?? 0,
            (float)(command.Stroke?.Join ?? StrokeJoin.Miter),
            (float)(command.Stroke?.Cap ?? StrokeCap.Butt),
            tileCount);
        WriteSegments(input, mainStart, path.Segments, Matrix3x2.Identity);
        if (clipPath is not null)
        {
            WriteSegments(input, clipStartInput, clipPath.Segments, command.PathClip!.Value.Transform);
        }

        return new(
            MemoryMarshal.AsBytes(input.AsSpan()).ToArray(),
            checked((ulong)outputBytes),
            checked((uint)tileCount),
            checked((uint)mainCapacity));
    }

    private static void WriteSegments(
        Span<Vector4> destination,
        int start,
        IReadOnlyList<PathSegment> segments,
        Matrix3x2 transform)
    {
        for (int index = 0; index < segments.Count; index++)
        {
            PathSegment segment = segments[index];
            Vector2 point = Vector2.Transform(segment.Point, transform);
            Vector2 control0 = Vector2.Transform(segment.Control0, transform);
            Vector2 control1 = Vector2.Transform(segment.Control1, transform);
            destination[start + index * 2] = new((float)segment.Kind, point.X, point.Y, 0);
            destination[start + index * 2 + 1] = new(
                control0.X,
                control0.Y,
                control1.X,
                control1.Y);
        }
    }

    private static int EdgeCapacity(PathGeometry path)
    {
        int result = 0;
        foreach (PathSegment segment in path.Segments)
        {
            result = checked(result + segment.Kind switch
            {
                PathSegmentKind.Move => 0,
                PathSegmentKind.Line or PathSegmentKind.Close => 1,
                PathSegmentKind.Quadratic or PathSegmentKind.Cubic => MaximumCurveSubdivision,
                _ => throw new ArgumentOutOfRangeException(nameof(path)),
            });
        }
        return Math.Max(result, 1);
    }

    private static float MaximumScale(Matrix3x2 transform)
        => MathF.Max(
            MathF.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12),
            MathF.Sqrt(transform.M21 * transform.M21 + transform.M22 * transform.M22));
}
