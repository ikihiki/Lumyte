using System.Numerics;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.TwoD;

/// <summary>Packs preserved path commands for the portable GPU preparation kernel.</summary>
internal static class PathBatchCompiler
{
    // Input and prepared buffers share these header slots. Keeping all variable-length
    // data behind one header lets every backend bind a single structured buffer.
    private const int HeaderVectors = 16;
    private const int MaximumCurveSubdivision = 64;
    private const float TargetPixelTolerance = 0.25f;
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
        float tolerance = TargetPixelTolerance / MathF.Max(scale, 0.0001f);
        Rect bounds = command.Bounds;
        float tileSize = ScreenTileSize / MathF.Max(scale, 0.0001f);
        uint columns = checked((uint)Math.Clamp(Math.Ceiling(bounds.Width / tileSize), 1, 256));
        uint rows = checked((uint)Math.Clamp(Math.Ceiling(bounds.Height / tileSize), 1, 256));
        tileSize = MathF.Max(
            MathF.Max(bounds.Width / columns, bounds.Height / rows),
            0.0001f);

        List<ClipSource> clips = CollectClips(command);
        int mainCapacity = EdgeCapacity(path.Segments);
        int clipCapacity = 0;
        foreach (ClipSource clip in clips)
        {
            clipCapacity = checked(clipCapacity + EdgeCapacity(clip.Segments));
        }

        int stopCount = command.Brush.GradientStops.Count;
        int tileCount = checked((int)(columns * rows));

        int mainEdgeStart = HeaderVectors;
        int clipDescriptorOutputStart = checked(mainEdgeStart + mainCapacity);
        int clipEdgeStart = checked(clipDescriptorOutputStart + clips.Count);
        int gradientOutputStart = checked(clipEdgeStart + clipCapacity);
        int tableStart = checked(gradientOutputStart + stopCount * 2);
        int indexStart = checked(tableStart + tileCount);
        int outputVectorCount = checked(indexStart + mainCapacity * tileCount);
        int outputBytes = checked(outputVectorCount * Marshal.SizeOf<Vector4>());
        if (outputBytes > MaximumOutputBytes)
        {
            throw new NotSupportedException(
                "The path exceeds the 128 MiB phase-three preparation limit.");
        }

        int mainInputStart = HeaderVectors;
        int clipDescriptorInputStart = checked(mainInputStart + path.Segments.Count * 2);
        int clipSegmentInputStart = checked(clipDescriptorInputStart + clips.Count * 2);
        int gradientInputStart = clipSegmentInputStart;
        foreach (ClipSource clip in clips)
        {
            gradientInputStart = checked(gradientInputStart + clip.Segments.Count * 2);
        }
        int inputVectorCount = checked(gradientInputStart + stopCount * 2);
        var input = new Vector4[inputVectorCount];

        Matrix3x2 transform = command.Transform;
        input[0] = new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        input[1] = new(transform.M11, transform.M12, transform.M21, transform.M22);
        input[2] = new(transform.M31, transform.M32, target.Width, target.Height);
        input[3] = new(
            path.Segments.Count,
            (float)command.FillRule,
            command.Stroke?.Width ?? 0,
            (float)command.Brush.Kind);
        input[4] = new(
            command.Brush.Point0.X,
            command.Brush.Point0.Y,
            command.Brush.Point1.X,
            command.Brush.Point1.Y);
        input[5] = new(
            command.Brush.Point2.X,
            command.Brush.Point2.Y,
            command.Brush.Radius0,
            command.Brush.Radius1);
        input[6] = new(
            command.Brush.StartAngle,
            command.Brush.EndAngle,
            (float)command.Brush.ExtendMode,
            stopCount);
        input[7] = new(columns, rows, tileSize, tableStart);
        input[8] = new(indexStart, mainEdgeStart, mainCapacity, tileCount);
        input[9] = new(mainInputStart, tolerance, clips.Count, clipDescriptorInputStart);
        input[10] = new(clipDescriptorOutputStart, gradientOutputStart, gradientInputStart, clipEdgeStart);
        input[11] = command.Brush.Color.Premultiplied();

        WriteSegments(input, mainInputStart, path.Segments, Matrix3x2.Identity);

        int nextClipInput = clipSegmentInputStart;
        int nextClipOutput = clipEdgeStart;
        for (int index = 0; index < clips.Count; index++)
        {
            ClipSource clip = clips[index];
            int capacity = EdgeCapacity(clip.Segments);
            int descriptor = checked(clipDescriptorInputStart + index * 2);
            input[descriptor] = new(
                nextClipInput,
                clip.Segments.Count,
                capacity,
                (float)clip.FillRule);
            input[descriptor + 1] = new(
                nextClipOutput,
                clip.TargetSpace ? 1 : 0,
                clip.TargetSpace ? TargetPixelTolerance : tolerance,
                0);
            WriteSegments(input, nextClipInput, clip.Segments, clip.Transform);
            nextClipInput = checked(nextClipInput + clip.Segments.Count * 2);
            nextClipOutput = checked(nextClipOutput + capacity);
        }

        for (int index = 0; index < stopCount; index++)
        {
            GradientStop stop = command.Brush.GradientStops[index];
            Vector4 color = stop.Color.Premultiplied();
            input[gradientInputStart + index * 2] = new(stop.Offset, color.X, color.Y, color.Z);
            input[gradientInputStart + index * 2 + 1] = new(color.W, 0, 0, 0);
        }

        return new(
            MemoryMarshal.AsBytes(input.AsSpan()).ToArray(),
            checked((ulong)outputBytes),
            checked((uint)tileCount),
            checked((uint)mainCapacity));
    }

    private static List<ClipSource> CollectClips(RecordedCommand command)
    {
        var result = new List<ClipSource>();
        if (command.PathClip is { } direct)
        {
            result.Add(new(
                direct.Geometry.Segments,
                direct.Transform,
                direct.FillRule,
                TargetSpace: false));
        }

        var scoped = new Stack<RecordedClip>();
        for (RecordedClipStack? current = command.ClipStack; current is not null; current = current.Parent)
        {
            scoped.Push(current.Clip);
        }
        while (scoped.TryPop(out RecordedClip clip))
        {
            if (clip.Kind == RecordedClipKind.Path)
            {
                result.Add(new(
                    clip.Path!.Segments,
                    clip.Transform,
                    clip.FillRule,
                    TargetSpace: true));
            }
            else
            {
                result.Add(new(
                    RectangleSegments(clip.Rectangle),
                    clip.Transform,
                    FillRule.NonZero,
                    TargetSpace: true));
            }
        }
        return result;
    }

    private static IReadOnlyList<PathSegment> RectangleSegments(Rect rectangle)
        =>
        [
            new(PathSegmentKind.Move, new(rectangle.X, rectangle.Y)),
            new(PathSegmentKind.Line, new(rectangle.Right, rectangle.Y)),
            new(PathSegmentKind.Line, new(rectangle.Right, rectangle.Bottom)),
            new(PathSegmentKind.Line, new(rectangle.X, rectangle.Bottom)),
            new(PathSegmentKind.Close, new(rectangle.X, rectangle.Y)),
        ];

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

    private static int EdgeCapacity(IReadOnlyList<PathSegment> segments)
    {
        int result = 0;
        foreach (PathSegment segment in segments)
        {
            result = checked(result + segment.Kind switch
            {
                PathSegmentKind.Move => 0,
                PathSegmentKind.Line or PathSegmentKind.Close => 1,
                PathSegmentKind.Quadratic or PathSegmentKind.Cubic => MaximumCurveSubdivision,
                _ => throw new ArgumentOutOfRangeException(nameof(segments)),
            });
        }
        return Math.Max(result, 1);
    }

    private static float MaximumScale(Matrix3x2 transform)
        => MathF.Max(
            MathF.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12),
            MathF.Sqrt(transform.M21 * transform.M21 + transform.M22 * transform.M22));

    private readonly record struct ClipSource(
        IReadOnlyList<PathSegment> Segments,
        Matrix3x2 Transform,
        FillRule FillRule,
        bool TargetSpace);
}
