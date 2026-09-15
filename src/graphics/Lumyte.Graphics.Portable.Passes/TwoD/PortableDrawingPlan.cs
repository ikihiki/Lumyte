using System.Numerics;

using Lumyte.Graphics.Text;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Portable.Passes;

internal sealed record PortableDrawingNode(uint Header, Draw2DImageSource? Image = null,
    IReadOnlyList<PortableDrawingNode>? Children = null, uint? BlurHorizontal = null, uint? BlurVertical = null,
    uint? Shadow = null, uint? ShadowHorizontal = null, uint? ShadowVertical = null, PortableDrawingNode? Mask = null,
    Draw2DImageSource? DistanceField = null, bool PreserveBackdrop = false);

internal sealed class PortableDrawingPlan
{
    internal const int HeaderLength = 16;
    internal required Vector4[] Data { get; init; }
    internal required IReadOnlyList<PortableDrawingNode> Nodes { get; init; }
    internal static PortableDrawingPlan Compile(Draw2DScene scene, Matrix3x2 transform, float deviceScale, IReadOnlyList<Vector4[]> clips)
    {
        var compiler = new Compiler(deviceScale);
        var nodes = compiler.Scene(scene, transform, clips);
        if (compiler.Data.Count == 0)
        { compiler.Data.Add(Vector4.Zero); }
        return new() { Data = compiler.Data.ToArray(), Nodes = nodes };
    }
    private sealed class Compiler(float deviceScale)
    {
        internal List<Vector4> Data { get; } = [];
        internal List<PortableDrawingNode> Scene(Draw2DScene scene, Matrix3x2 parent, IReadOnlyList<Vector4[]> parentClips)
        {
            var nodes = Commands(scene.Commands, 0, scene.Commands.Count, parent, 0);
            return parentClips.Count == 0 ? nodes : [ClipScope(nodes, parentClips)];
        }
        private List<PortableDrawingNode> Commands(IReadOnlyList<Draw2DCommand> commands, int start, int end, Matrix3x2 parent, int clipDepth)
        {
            var result = new List<PortableDrawingNode>();
            for (int index = start; index < end; index++)
            {
                Draw2DCommand command = commands[index];
                if (command.State.Clips.Count > clipDepth)
                {
                    Draw2DClip shared = command.State.Clips[clipDepth];
                    int groupEnd = index + 1;
                    while (groupEnd < end && commands[groupEnd].State.Clips.Count > clipDepth && commands[groupEnd].State.Clips[clipDepth] == shared)
                    { groupEnd++; }
                    if (groupEnd - index > 1)
                    {
                        var children = Commands(commands, index, groupEnd, parent, clipDepth + 1);
                        result.Add(ClipScope(children, [ClipEdges(shared, parent)]));
                        index = groupEnd - 1;
                        continue;
                    }
                }
                Matrix3x2 transform = command.State.Transform * parent;
                if (!Matrix3x2.Invert(transform, out _))
                { continue; }
                var clips = new List<Vector4[]>();
                for (int clipIndex = clipDepth; clipIndex < command.State.Clips.Count; clipIndex++)
                {
                    clips.Add(ClipEdges(command.State.Clips[clipIndex], parent));
                }
                switch (command)
                {
                    case Draw2DShapeCommand shape:
                        result.Add(Draw(PortableDrawingGeometry.Shape(shape, PortableDrawingGeometry.Scale(transform)), FillRule.NonZero, shape.Brush, transform, clips));
                        break;
                    case Draw2DPathCommand path:
                        var contours = PortableDrawingGeometry.Flatten(path.Path, PortableDrawingGeometry.Scale(transform));
                        if (path.Stroke is not null)
                        { contours = PortableDrawingGeometry.Stroke(contours, path.Stroke, PortableDrawingGeometry.Scale(transform)); }
                        result.Add(Draw(contours, path.Stroke is null ? path.FillRule : FillRule.NonZero, path.Brush, transform, clips));
                        break;
                    case Draw2DGeometryCommand geometry:
                        result.Add(DrawEdges(TriangleEdges(geometry.Geometry, transform), FillRule.NonZero, geometry.Brush, transform, clips));
                        break;
                    case Draw2DImageCommand image:
                        result.Add(Image(image.Source, image.Destination, image.SourceRectangle, image.Tint, image.Sampling, transform, clips));
                        break;
                    case Draw2DDistanceFieldCommand field:
                        var node = Draw(PortableDrawingGeometry.Rectangle(field.Destination), FillRule.NonZero, field.Brush, transform, clips);
                        uint h = node.Header;
                        var sampling = Data[(int)h + 9];
                        sampling.W = 1;
                        Data[(int)h + 9] = sampling;
                        Data[(int)h + 10] = new((float)field.Data.Kind, field.Data.DistanceRange, field.Destination.Width / field.Data.SourceSize.X * PortableDrawingGeometry.Scale(transform), 0);
                        Data[(int)h + 14] = RectangleVector(field.Destination);
                        Matrix3x2.Invert(transform, out var inverse);
                        Data[(int)h + 15] = new(Data.Count, 0, 0, 0);
                        Data.Add(new(inverse.M11, inverse.M12, inverse.M21, inverse.M22));
                        Data.Add(new(inverse.M31, inverse.M32, 0, 0));
                        result.Add(node with { DistanceField = new(field.Data.Image) });
                        break;
                    case Draw2DTextCommand text:
                        foreach (var run in text.Data.GlyphRuns)
                        {
                            foreach (var glyph in run.Glyphs)
                            {
                                Matrix3x2 m = glyph.Transform * Matrix3x2.CreateTranslation(text.Origin) * transform;
                                if (glyph.Glyph.ColorPaint is { } color)
                                { Glyph(result, color, text.Brush, m, clips); }
                                else if (glyph.Glyph.Outline is { } outline)
                                { result.Add(Draw(PortableDrawingGeometry.Flatten(outline.Path, PortableDrawingGeometry.Scale(m)), outline.FillRule, text.Brush, m, clips)); }
                            }
                        }
                        break;
                    case Draw2DSceneCommand child:
                        result.AddRange(Scene(child.Content, transform, clips));
                        break;
                    case Draw2DLayerCommand layer:
                        var children = Scene(layer.Content, transform, []);
                        result.Add(Layer(children, layer.Options, transform, clips));
                        break;
                }
            }
            return result;
        }
        private static Vector4[] ClipEdges(Draw2DClip clip, Matrix3x2 parent)
        {
            Matrix3x2 transform = clip.Transform * parent;
            var contours = clip.Path is not null ? PortableDrawingGeometry.Flatten(clip.Path, PortableDrawingGeometry.Scale(transform)) : PortableDrawingGeometry.Rectangle(clip.Rectangle!.Value);
            return WithRule(PortableDrawingGeometry.Edges(contours, transform), clip.FillRule);
        }
        private PortableDrawingNode ClipScope(IReadOnlyList<PortableDrawingNode> children, IReadOnlyList<Vector4[]> clips)
        {
            uint header = Header(Matrix3x2.Identity, clips);
            Data[(int)header + 11] = new(1, (float)CompositeMode.Source, 1, 0);
            return new(header, Children: children, PreserveBackdrop: true);
        }
        private PortableDrawingNode Layer(IReadOnlyList<PortableDrawingNode> children, Draw2DLayerOptions options, Matrix3x2 transform, IReadOnlyList<Vector4[]> clips)
        {
            var compositeClips = new List<Vector4[]>(clips);
            if (options.Bounds is { } bounds)
            { compositeClips.Add(WithRule(PortableDrawingGeometry.Edges(PortableDrawingGeometry.Rectangle(bounds), transform), FillRule.NonZero)); }
            uint header = Header(transform, compositeClips);
            Data[(int)header + 11] = new(1, (float)options.CompositeMode, options.Opacity, 0);
            PortableDrawingNode? mask = options.Mask is { } maskValue ? Image(maskValue.Image, maskValue.Destination,
                new(0, 0, maskValue.Image.Description.Width, maskValue.Image.Description.Height), Color.White, default, transform, []) : null;
            if (mask is not null)
            { Vector4 v = Data[(int)header + 12]; v.Z = 1; Data[(int)header + 12] = v; }
            uint? bh = null, bv = null, sh = null, sv = null, shadow = null;
            if (options.BlurRadius > 0)
            { bh = Blur(options.BlurRadius * deviceScale, true); bv = Blur(options.BlurRadius * deviceScale, false); }
            if (options.Shadow is { } s)
            {
                if (s.BlurRadius > 0)
                { sh = Blur(s.BlurRadius * deviceScale, true); sv = Blur(s.BlurRadius * deviceScale, false); }
                shadow = Header(Matrix3x2.Identity, compositeClips);
                Vector2 offset = Vector2.TransformNormal(s.Offset, transform);
                Data[(int)shadow + 2] = new(offset.X, offset.Y, 0, 0);
                Data[(int)shadow + 4] = s.Color.Premultiplied();
                Data[(int)shadow + 11] = new(3, (float)CompositeMode.SourceOver, options.Opacity, 0);
            }
            return new(header, Children: children, BlurHorizontal: bh, BlurVertical: bv, Shadow: shadow, ShadowHorizontal: sh, ShadowVertical: sv, Mask: mask);
        }
        private uint Blur(float radius, bool horizontal)
        { uint h = Header(Matrix3x2.Identity, []); Data[(int)h + 2] = new(radius / 3, 0, horizontal ? 1 : 0, horizontal ? 0 : 1); Data[(int)h + 11] = new(2, 3, 1, 0); return h; }
        private void Glyph(List<PortableDrawingNode> result, GlyphPaintNode node, Brush foreground, Matrix3x2 transform, List<Vector4[]> clips)
        {
            if (!Matrix3x2.Invert(transform, out _))
            { return; }
            switch (node)
            {
                case GlyphPaintNode.Outline outline:
                    var outlineClips = new List<Vector4[]>(clips) { WithRule(PortableDrawingGeometry.Edges(PortableDrawingGeometry.Flatten(outline.Path, PortableDrawingGeometry.Scale(transform)), transform), outline.FillRule) };
                    Glyph(result, outline.Paint, foreground, transform, outlineClips);
                    break;
                case GlyphPaintNode.Solid solid:
                    result.Add(GlyphFill(Brush.Solid(solid.Color), transform, clips));
                    break;
                case GlyphPaintNode.Foreground f:
                    var draw = GlyphFill(foreground, transform, clips);
                    Data[(int)draw.Header + 13] = new(f.Alpha, 0, 0, 0);
                    result.Add(draw);
                    break;
                case GlyphPaintNode.Gradient gradient:
                    result.Add(GlyphFill(gradient.Brush, transform, clips));
                    break;
                case GlyphPaintNode.Bitmap bitmap:
                    result.Add(Image(new(bitmap.Image), bitmap.Destination, bitmap.SourceRectangle, Color.White, default, transform, clips));
                    break;
                case GlyphPaintNode.Transform t:
                    Glyph(result, t.Child, foreground, t.Matrix * transform, clips);
                    break;
                case GlyphPaintNode.Clip c:
                    var childClips = new List<Vector4[]>(clips) { WithRule(PortableDrawingGeometry.Edges(PortableDrawingGeometry.Flatten(c.Path, PortableDrawingGeometry.Scale(transform)), transform), c.FillRule) };
                    Glyph(result, c.Child, foreground, transform, childClips);
                    break;
                case GlyphPaintNode.Layers layers:
                    if (clips.Count > 0 && layers.Children.Count > 1)
                    {
                        var children = new List<PortableDrawingNode>();
                        foreach (var child in layers.Children)
                        { Glyph(children, child, foreground, transform, []); }
                        result.Add(ClipScope(children, clips));
                        break;
                    }
                    foreach (var child in layers.Children)
                    { Glyph(result, child, foreground, transform, clips); }
                    break;
                case GlyphPaintNode.Composite composite:
                    var backdropNodes = new List<PortableDrawingNode>();
                    Glyph(backdropNodes, composite.Backdrop, foreground, transform, []);
                    var sourceNodes = new List<PortableDrawingNode>();
                    Glyph(sourceNodes, composite.Source, foreground, transform, []);
                    backdropNodes.Add(Layer(sourceNodes, new(CompositeMode: composite.Mode), transform, []));
                    result.Add(Layer(backdropNodes, new(), transform, clips));
                    break;
            }
        }
        private PortableDrawingNode GlyphFill(Brush brush, Matrix3x2 transform, IReadOnlyList<Vector4[]> clips)
        {
            // Paint is defined over the current clip. Transforming a paint changes its coordinates,
            // never the enclosing glyph clip; isolated composite children need the same rule.
            var node = DrawEdges([], FillRule.NonZero, brush, transform, clips);
            Data[(int)node.Header] = new(0, 0, 0, 1);
            return node;
        }
        private PortableDrawingNode Draw(List<PortableDrawingGeometry.Contour> contours, FillRule rule, Brush brush, Matrix3x2 transform, IReadOnlyList<Vector4[]> clips)
            => DrawEdges(PortableDrawingGeometry.Edges(contours, transform), rule, brush, transform, clips);
        private PortableDrawingNode DrawEdges(Vector4[] edges, FillRule rule, Brush brush, Matrix3x2 transform, IReadOnlyList<Vector4[]> clips)
        {
            uint h = Header(transform, clips), edgeStart = (uint)Data.Count;
            Data.AddRange(edges);
            Data[(int)h] = new(edgeStart, edges.Length, (float)rule, 0);
            Data[(int)h + 11] = new(0, 3, 1, 0);
            Draw2DImageSource? image = Paint(h, brush, transform);
            return new(h, image);
        }
        private PortableDrawingNode Image(Draw2DImageSource source, Rect destination, Rect sourceRectangle, Color tint, Draw2DImageSampling sampling, Matrix3x2 transform, IReadOnlyList<Vector4[]> clips)
        {
            var node = Draw(PortableDrawingGeometry.Rectangle(destination), FillRule.NonZero, Brush.Solid(tint), transform, clips);
            uint h = node.Header;
            Data[(int)h + 1] = new(4, 0, 0, 0);
            Data[(int)h + 7] = RectangleVector(sourceRectangle);
            Data[(int)h + 8] = RectangleVector(destination);
            Data[(int)h + 9] = new(sampling.Filter == Draw2DImageFilter.Linear ? 1 : 0, (float)sampling.ExtendX, (float)sampling.ExtendY, 0);
            SetImage(h, source);
            return node with { Image = source };
        }
        private Draw2DImageSource? Paint(uint h, Brush brush, Matrix3x2 transform)
        {
            if (brush is SolidBrush solid)
            { Data[(int)h + 4] = solid.Color.Premultiplied(); return null; }
            if (brush is ImageBrush image)
            {
                Data[(int)h + 1] = new(4, 0, 0, 0);
                Data[(int)h + 4] = Vector4.One;
                SetTransform(h, image.Transform * transform);
                Data[(int)h + 7] = new(0, 0, image.Source.Description.Width, image.Source.Description.Height);
                Data[(int)h + 8] = Data[(int)h + 7];
                Data[(int)h + 9] = new(image.Sampling.Filter == Draw2DImageFilter.Linear ? 1 : 0, (float)image.Sampling.ExtendX, (float)image.Sampling.ExtendY, 0);
                SetImage(h, image.Source);
                return image.Source;
            }
            var gradient = (GradientBrush)brush;
            int start = Data.Count;
            foreach (var stop in gradient.Stops)
            { Data.Add(new(stop.Offset, 0, 0, 0)); Data.Add(stop.Color.Premultiplied()); }
            if (brush is LinearGradientBrush linear)
            { Data[(int)h + 1] = new(1, start, gradient.Stops.Count, (float)gradient.ExtendMode); Data[(int)h + 2] = new(linear.Start.X, linear.Start.Y, linear.End.X, linear.End.Y); Data[(int)h + 3] = new(linear.Projection.X, linear.Projection.Y, 0, 0); }
            else if (brush is RadialGradientBrush radial)
            { Data[(int)h + 1] = new(2, start, gradient.Stops.Count, (float)gradient.ExtendMode); Data[(int)h + 2] = new(radial.Center0.X, radial.Center0.Y, radial.Center1.X, radial.Center1.Y); Data[(int)h + 3] = new(0, 0, radial.Radius0, radial.Radius1); }
            else if (brush is SweepGradientBrush sweep)
            { Data[(int)h + 1] = new(3, start, gradient.Stops.Count, (float)gradient.ExtendMode); Data[(int)h + 2] = new(sweep.Center.X, sweep.Center.Y, 0, 0); Data[(int)h + 3] = new(sweep.StartAngle, sweep.EndAngle, 0, 0); }
            return null;
        }
        private void SetImage(uint h, Draw2DImageSource source)
        {
            Vector4 settings = Data[(int)h + 6];
            settings.Z = (float)(source.Upload?.Encoding ?? Graphics.RenderGraph.GpuImageColorEncoding.Linear);
            settings.W = (float)(source.Upload?.AlphaMode ?? Graphics.RenderGraph.GpuImageAlphaMode.Premultiplied);
            Data[(int)h + 6] = settings;
        }
        private uint Header(Matrix3x2 transform, IReadOnlyList<Vector4[]> clips)
        {
            uint h = (uint)Data.Count;
            for (int i = 0; i < HeaderLength; i++)
            { Data.Add(Vector4.Zero); }
            SetTransform(h, transform);
            Data[(int)h + 13] = new(1, 0, 0, 0);
            if (clips.Count > 0)
            {
                int start = Data.Count;
                for (int i = 0; i < clips.Count; i++)
                { Data.Add(Vector4.Zero); }
                for (int i = 0; i < clips.Count; i++)
                { Vector4[] clip = clips[i]; int edgeStart = Data.Count; Data.AddRange(clip.AsSpan(1).ToArray()); Data[start + i] = new(edgeStart, clip.Length - 1, clip[0].X, 0); }
                Data[(int)h + 12] = new(start, clips.Count, 0, 0);
            }
            return h;
        }
        private void SetTransform(uint h, Matrix3x2 transform)
        {
            if (!Matrix3x2.Invert(transform, out var m))
            { m = Matrix3x2.Identity; }
            Data[(int)h + 5] = new(m.M11, m.M12, m.M21, m.M22);
            Data[(int)h + 6] = new(m.M31, m.M32, 0, 2);
        }
        private static Vector4 RectangleVector(Rect r) => new(r.X, r.Y, r.Width, r.Height);
        private static Vector4[] WithRule(Vector4[] edges, FillRule rule) => [new((float)rule, 0, 0, 0), .. edges];
        private static Vector4[] TriangleEdges(PolygonGeometry geometry, Matrix3x2 transform)
        {
            var edges = new HashSet<(Vector2 A, Vector2 B)>();
            var p = geometry.Vertices;
            for (int i = 0; i < p.Count; i += 3)
            {
                Vector2 a = Vector2.Transform(p[i], transform), b = Vector2.Transform(p[i + 1], transform), c = Vector2.Transform(p[i + 2], transform);
                if ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X) < 0)
                { (b, c) = (c, b); }
                Add(a, b);
                Add(b, c);
                Add(c, a);
            }
            return edges.Select(e => new Vector4(e.A.X, e.A.Y, e.B.X, e.B.Y)).ToArray();
            void Add(Vector2 a, Vector2 b)
            { if (!edges.Remove((b, a))) { edges.Add((a, b)); } }
        }
    }
}
