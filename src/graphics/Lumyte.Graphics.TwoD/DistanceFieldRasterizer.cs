using System.Numerics;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.TwoD;

/// <summary>Generates generic coverage or signed-distance regions from vector paths on the GPU.</summary>
public sealed class DistanceFieldRasterizer : IDisposable
{
    private readonly IGpuBackend backend;
    private readonly DistanceFieldAtlas atlas;
    private GpuRasterPipelineHandle pipeline;
    private GpuShaderPackage? shaders;
    private bool initialized;
    private bool disposed;

    public DistanceFieldRasterizer(IGpuBackend backend, DistanceFieldAtlas atlas)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        this.atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
        if (!ReferenceEquals(backend, atlas.Backend))
        {
            throw new ArgumentException("Atlas and rasterizer must use the same backend.", nameof(atlas));
        }
    }

    public DistanceField Rasterize(
        PathGeometry path,
        uint width,
        uint height,
        DistanceFieldOptions options = default)
    {
        VerifyAlive();
        ArgumentNullException.ThrowIfNull(path);
        if (path.IsEmpty) { throw new ArgumentException("Path cannot be empty.", nameof(path)); }
        options = options == default ? new DistanceFieldOptions() : options.Validate();
        if (width < 2 || height < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Distance fields require at least two pixels per dimension.");
        }

        DistanceField field = atlas.Allocate(width, height, options.DistanceRange, options.Encoding);
        try
        {
            DistanceFieldEntry entry = atlas.Require(field);
            Vector4[] data = BuildGpuData(path, entry, atlas.Description, options);
            using OwnedBuffer buffer = OwnedBuffer.Create(backend, MemoryMarshal.AsBytes(data.AsSpan()));
            GpuBufferView view = backend.CreateBufferView(buffer.Buffer, default);
            GpuTextureView atlasView = backend.CreateTextureView(
                atlas.Texture,
                new(GpuFormat.R8Unorm));
            try
            {
                var table = new GpuResourceTable(0, 0, 1);
                table.SetBuffer(0, view.Id);
                AtlasRectangle region = entry.Region;
                GpuCommandBuffer commands = backend.MainQueue.StartCommandRecording()
                    .Barrier(GpuStage.PixelShader, GpuStage.ColorOutput)
                    .BeginRendering([
                        new(
                            atlasView,
                            initialized ? GpuAttachmentLoadOperation.Load : GpuAttachmentLoadOperation.Clear,
                            GpuAttachmentStoreOperation.Store,
                            new(0, 0, 0, 0)),
                    ])
                    .SetPipeline(GetPipeline())
                    .SetResourceTable(table)
                    .SetViewportAndScissor(
                        new(region.X, region.Y, region.Width, region.Height),
                        new(region.X, region.Y, region.Width, region.Height))
                    .Draw(6)
                    .EndRendering()
                    .Barrier(GpuStage.ColorOutput, GpuStage.PixelShader, GpuBarrierHazards.Descriptors);
                Submit(commands);
                initialized = true;
                return field;
            }
            finally
            {
                backend.DestroyTextureView(atlasView);
                backend.DestroyBufferView(view);
            }
        }
        catch
        {
            atlas.Release(field, default);
            atlas.Collect(default);
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        if (!pipeline.IsNull) { backend.DestroyRasterPipeline(pipeline); }
        pipeline = default;
        disposed = true;
    }

    private GpuRasterPipelineHandle GetPipeline()
    {
        if (!pipeline.IsNull) { return pipeline; }
        shaders ??= StandardShaders.LoadPhaseTwo();
        pipeline = backend.CreateRasterPipeline(
            new GpuRasterPipelineDescription([new(GpuFormat.R8Unorm)]),
            shaders,
            "distanceFieldRasterVertex",
            "distanceFieldRasterPixel",
            GpuShaderBindingConvention.AbiHash);
        return pipeline;
    }

    private static Vector4[] BuildGpuData(
        PathGeometry path,
        DistanceFieldEntry entry,
        GpuTextureDescription atlas,
        DistanceFieldOptions options)
    {
        float padding = MathF.Min(
            MathF.Ceiling(options.DistanceRange) + 1,
            MathF.Min(entry.Region.Width, entry.Region.Height) * 0.25f);
        Rect bounds = path.Bounds;
        float sourceWidth = MathF.Max(bounds.Width, 0.0001f);
        float sourceHeight = MathF.Max(bounds.Height, 0.0001f);
        var scale = new Vector2(
            MathF.Max(entry.Region.Width - padding * 2, 1) / sourceWidth,
            MathF.Max(entry.Region.Height - padding * 2, 1) / sourceHeight);
        List<FlattenedEdge> edges = Flatten(path, scale, bounds, padding);
        if (edges.Count == 0)
        {
            throw new ArgumentException("Path does not contain drawable edges.", nameof(path));
        }

        var result = new Vector4[4 + edges.Count];
        result[0] = new(entry.Region.X, entry.Region.Y, entry.Region.Width, entry.Region.Height);
        result[1] = new(atlas.Width, atlas.Height, edges.Count, options.DistanceRange);
        result[2] = new((float)options.FillRule, (float)options.Encoding, 0, 0);
        foreach ((FlattenedEdge edge, int index) in edges.Select((value, index) => (value, index)))
        {
            result[4 + index] = new(edge.Start.X, edge.Start.Y, edge.End.X, edge.End.Y);
        }
        return result;
    }

    private static List<FlattenedEdge> Flatten(
        PathGeometry path,
        Vector2 scale,
        Rect bounds,
        float padding)
    {
        var edges = new List<FlattenedEdge>();
        Vector2 current = default;
        Vector2 figureStart = default;
        bool hasCurrent = false;
        foreach (PathSegment segment in path.Segments)
        {
            switch (segment.Kind)
            {
                case PathSegmentKind.Move:
                    current = figureStart = Map(segment.Point);
                    hasCurrent = true;
                    break;
                case PathSegmentKind.Line:
                case PathSegmentKind.Close:
                    AddLine(Map(segment.Point));
                    break;
                case PathSegmentKind.Quadratic:
                    Vector2 quadraticEnd = Map(segment.Point);
                    FlattenQuadratic(current, Map(segment.Control0), quadraticEnd, edges, 0);
                    current = quadraticEnd;
                    break;
                case PathSegmentKind.Cubic:
                    Vector2 cubicEnd = Map(segment.Point);
                    FlattenCubic(current, Map(segment.Control0), Map(segment.Control1), cubicEnd, edges, 0);
                    current = cubicEnd;
                    break;
            }
        }
        return edges;

        Vector2 Map(Vector2 point) => new(
            padding + (point.X - bounds.X) * scale.X,
            padding + (point.Y - bounds.Y) * scale.Y);

        void AddLine(Vector2 end)
        {
            if (!hasCurrent) { throw new InvalidOperationException("Path segment has no active figure."); }
            if (Vector2.DistanceSquared(current, end) > 1e-8f)
            {
                edges.Add(new(current, end));
            }
            current = end;
            if (segmentIsClose(end)) { current = figureStart; }
        }

        bool segmentIsClose(Vector2 end) => end == figureStart;
    }

    private static void FlattenQuadratic(
        Vector2 start,
        Vector2 control,
        Vector2 end,
        List<FlattenedEdge> edges,
        int depth)
    {
        if (depth >= 10 || DistanceToLine(control, start, end) <= 0.25f)
        {
            edges.Add(new(start, end));
            return;
        }
        Vector2 first = (start + control) * 0.5f;
        Vector2 second = (control + end) * 0.5f;
        Vector2 middle = (first + second) * 0.5f;
        FlattenQuadratic(start, first, middle, edges, depth + 1);
        FlattenQuadratic(middle, second, end, edges, depth + 1);
    }

    private static void FlattenCubic(
        Vector2 start,
        Vector2 control0,
        Vector2 control1,
        Vector2 end,
        List<FlattenedEdge> edges,
        int depth)
    {
        float error = MathF.Max(
            DistanceToLine(control0, start, end),
            DistanceToLine(control1, start, end));
        if (depth >= 10 || error <= 0.25f)
        {
            edges.Add(new(start, end));
            return;
        }
        Vector2 p01 = (start + control0) * 0.5f;
        Vector2 p12 = (control0 + control1) * 0.5f;
        Vector2 p23 = (control1 + end) * 0.5f;
        Vector2 p012 = (p01 + p12) * 0.5f;
        Vector2 p123 = (p12 + p23) * 0.5f;
        Vector2 middle = (p012 + p123) * 0.5f;
        FlattenCubic(start, p01, p012, middle, edges, depth + 1);
        FlattenCubic(middle, p123, p23, end, edges, depth + 1);
    }

    private static float DistanceToLine(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 1e-8f) { return Vector2.Distance(point, start); }
        float amount = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0, 1);
        return Vector2.Distance(point, start + segment * amount);
    }

    private void Submit(GpuCommandBuffer commands)
    {
        using GpuSemaphore completion = backend.MainQueue.CreateSemaphore();
        backend.MainQueue.Submit([commands], completion, 1);
        backend.MainQueue.Wait(completion, 1);
    }

    private void VerifyAlive() => ObjectDisposedException.ThrowIf(disposed, this);
}
