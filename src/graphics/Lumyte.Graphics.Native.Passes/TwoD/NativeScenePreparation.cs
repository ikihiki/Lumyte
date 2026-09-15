using System.Numerics;

using Lumyte.Graphics.Text;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Native.Passes;

public sealed partial class NativeDraw2DPass
{
    private IReadOnlyList<Node> PrepareScene(Draw2DScene scene, uint width, uint height)
    {
        List<Node> result = [];
        Visit(scene, Matrix3x2.CreateScale(scene.DeviceScale), [], result);
        return result;

        void Visit(Draw2DScene content, Matrix3x2 parent, IReadOnlyList<Draw2DClip> inherited, List<Node> output)
        {
            if (inherited.Count != 0 && content.Commands.Count > 1)
            {
                List<Node> children = [];
                VisitCommands(content.Commands, parent, [], 0, content.Commands.Count, 0, children);
                output.Add(Clip(children, inherited));
                return;
            }
            VisitCommands(content.Commands, parent, inherited, 0, content.Commands.Count, 0, output);
        }

        void VisitCommands(IReadOnlyList<Draw2DCommand> commands, Matrix3x2 parent, IReadOnlyList<Draw2DClip> inherited,
            int begin, int end, int skipClips, List<Node> output)
        {
            for (int index = begin; index < end; index++)
            {
                Draw2DCommand command = commands[index];
                if (command.State.Clips.Count > skipClips)
                {
                    Draw2DClip common = command.State.Clips[skipClips];
                    int next = index + 1;
                    while (next < end && commands[next].State.Clips.Count > skipClips && commands[next].State.Clips[skipClips] == common)
                    { next++; }
                    if (next - index > 1)
                    {
                        List<Node> children = [];
                        VisitCommands(commands, parent, inherited, index, next, skipClips + 1, children);
                        output.Add(Clip(children, [common with { Transform = common.Transform * parent }]));
                        index = next - 1;
                        continue;
                    }
                }
                Matrix3x2 matrix = command.State.Transform * parent;
                if (!Invertible(matrix))
                { continue; }
                Draw2DClip[] clips = [.. inherited, .. command.State.Clips.Skip(skipClips).Select(clip => clip with { Transform = clip.Transform * parent })];
                var key = new PreparationKey(command, matrix, clips, width, height, scene.DeviceScale);
                if (preparedNodes.TryGetValue(key, out IReadOnlyList<Node>? cached))
                { output.AddRange(cached); continue; }
                List<Node> generated = [];
                switch (command)
                {
                    case Draw2DSceneCommand child:
                        Visit(child.Content, matrix, clips, generated);
                        break;
                    case Draw2DLayerCommand layer:
                        List<Node> children = [];
                        Visit(layer.Content, matrix, [], children);
                        generated.Add(Layer(children, layer.Options, matrix, clips));
                        break;
                    case Draw2DTextCommand text:
                        foreach (var glyph in text.Data.GlyphRuns.SelectMany(run => run.Glyphs))
                        {
                            Matrix3x2 glyphMatrix = glyph.Transform * Matrix3x2.CreateTranslation(text.Origin) * matrix;
                            if (!Invertible(glyphMatrix))
                            { continue; }
                            if (glyph.Glyph.ColorPaint is { } paint)
                            { Glyph(paint, text.Brush, glyph.Glyph.Bounds, glyphMatrix, clips, generated); }
                            else if (glyph.Glyph.Outline is { } outline)
                            { generated.Add(new DrawNode(NativeDrawPreparation.Prepare(new Draw2DPathCommand(outline.Path, text.Brush, outline.FillRule, null, Draw2DState.Identity), glyphMatrix, clips))); }
                        }
                        break;
                    default:
                        generated.Add(new DrawNode(NativeDrawPreparation.Prepare(command, matrix, clips)));
                        break;
                }
                if (preparedNodes.Count >= 2048)
                { preparedNodes.Clear(); }
                preparedNodes.Add(key, generated);
                output.AddRange(generated);
            }
        }

        ClipNode Clip(List<Node> children, IReadOnlyList<Draw2DClip> clips)
        {
            var draw = Composite(new(CompositeMode: CompositeMode.Source), Matrix3x2.Identity, clips, null);
            return new(children, draw);
        }

        LayerNode Layer(List<Node> children, Draw2DLayerOptions options, Matrix3x2 matrix, IReadOnlyList<Draw2DClip> clips)
        {
            IReadOnlyList<Draw2DClip> finalClips = options.Bounds is { } bounds
                ? [.. clips, new(bounds, null, FillRule.NonZero, matrix)] : clips;
            NativePreparedDraw composite = Composite(options, matrix, finalClips, null);
            NativePreparedDraw? shadow = options.Shadow is null ? null : Composite(options, matrix, finalClips, options.Shadow);
            return new(children, options, composite, shadow, scene.DeviceScale);
        }

        NativePreparedDraw Composite(Draw2DLayerOptions options, Matrix3x2 matrix, IReadOnlyList<Draw2DClip> clips, ShadowOptions? shadow)
        {
            Rect target = new(0, 0, width, height);
            var result = NativeDrawPreparation.Prepare(new Draw2DShapeCommand(Draw2DShapeKind.Rectangle, target, default,
                Brush.Solid(Color.White), Draw2DState.Identity), Matrix3x2.Identity, clips,
                shadow is null ? options.CompositeMode : CompositeMode.SourceOver, options.Opacity);
            Vector4[] data = result.Data;
            data[9] = data[9] with { Z = 1 };
            data[10] = data[11] = NativeDrawPreparation.RectVector(target);
            data[13] = new(1, 0, 0, 1);
            if (shadow is not null)
            {
                Vector2 offset = Vector2.TransformNormal(shadow.Offset, matrix);
                data[11] += new Vector4(offset.X, offset.Y, 0, 0);
                data[12] = shadow.Color.Premultiplied();
                data[14] = data[14] with { Y = 1 };
            }
            if (shadow is null && options.Mask is { } mask)
            {
                data[14] = data[14] with { W = 1 };
                data[18] = NativeDrawPreparation.RectVector(mask.Destination);
                Matrix3x2.Invert(matrix, out Matrix3x2 inverse);
                data[15] = new(inverse.M11, inverse.M12, inverse.M21, inverse.M22);
                data[16] = new(inverse.M31, inverse.M32, 0, 0);
                return result with { Mask = mask.Image };
            }
            return result;
        }

        void Glyph(GlyphPaintNode node, Brush foreground, Rect bounds, Matrix3x2 matrix,
            IReadOnlyList<Draw2DClip> clips, List<Node> output)
        {
            if (!Invertible(matrix))
            { return; }
            switch (node)
            {
                case GlyphPaintNode.Transform transform:
                    Glyph(transform.Child, foreground, bounds, transform.Matrix * matrix, clips, output);
                    break;
                case GlyphPaintNode.Clip clip:
                    Glyph(clip.Child, foreground, bounds, matrix, [.. clips, new(null, clip.Path, clip.FillRule, matrix)], output);
                    break;
                case GlyphPaintNode.Outline outline:
                    Glyph(outline.Paint, foreground, bounds, matrix, [.. clips, new(null, outline.Path, outline.FillRule, matrix)], output);
                    break;
                case GlyphPaintNode.Layers layers:
                    if (clips.Count != 0 && layers.Children.Count > 1)
                    {
                        List<Node> children = [];
                        foreach (var child in layers.Children)
                        { Glyph(child, foreground, bounds, matrix, [], children); }
                        output.Add(Clip(children, clips));
                    }
                    else
                    {
                        foreach (var child in layers.Children)
                        { Glyph(child, foreground, bounds, matrix, clips, output); }
                    }
                    break;
                case GlyphPaintNode.Composite composite:
                    List<Node> isolated = [], source = [];
                    Glyph(composite.Backdrop, foreground, bounds, matrix, [], isolated);
                    Glyph(composite.Source, foreground, bounds, matrix, [], source);
                    isolated.Add(Layer(source, new(CompositeMode: composite.Mode), matrix, []));
                    output.Add(Layer(isolated, new(), matrix, clips));
                    break;
                case GlyphPaintNode.Bitmap bitmap:
                    output.Add(new DrawNode(NativeDrawPreparation.Prepare(new Draw2DImageCommand(new(bitmap.Image), bitmap.Destination,
                        bitmap.SourceRectangle, Color.White, default, Draw2DState.Identity), matrix, clips)));
                    break;
                default:
                    Brush brush = node switch
                    {
                        GlyphPaintNode.Solid solid => Brush.Solid(solid.Color),
                        GlyphPaintNode.Gradient gradient => gradient.Brush,
                        GlyphPaintNode.Foreground => foreground,
                        _ => throw new ArgumentException("Unknown glyph paint node.", nameof(node)),
                    };
                    float alpha = node is GlyphPaintNode.Foreground fg ? fg.Alpha : 1;
                    Matrix3x2.Invert(matrix, out Matrix3x2 inverse);
                    Vector2[] corners = NativePathPreparation.Rectangle(new(0, 0, width, height))
                        .Select(point => Vector2.Transform(point, inverse)).ToArray();
                    float left = corners.Min(point => point.X), top = corners.Min(point => point.Y);
                    Rect paintBounds = new(left - 1, top - 1, corners.Max(point => point.X) - left + 2, corners.Max(point => point.Y) - top + 2);
                    output.Add(new DrawNode(NativeDrawPreparation.Prepare(new Draw2DShapeCommand(Draw2DShapeKind.Rectangle,
                        paintBounds, default, brush, Draw2DState.Identity), matrix, clips, opacity: alpha)));
                    break;
            }
        }
    }

    private static bool Invertible(Matrix3x2 matrix)
        => Matrix3x2.Invert(matrix, out Matrix3x2 inverse) && float.IsFinite(inverse.M11) && float.IsFinite(inverse.M12)
            && float.IsFinite(inverse.M21) && float.IsFinite(inverse.M22) && float.IsFinite(inverse.M31) && float.IsFinite(inverse.M32);

    private sealed class PreparationKey(Draw2DCommand command, Matrix3x2 transform, IReadOnlyList<Draw2DClip> clips,
        uint width, uint height, float deviceScale) : IEquatable<PreparationKey>
    {
        private readonly Draw2DCommand command = command;
        private readonly Matrix3x2 transform = transform;
        private readonly IReadOnlyList<Draw2DClip> clips = clips;
        private readonly uint width = width, height = height;
        private readonly float deviceScale = deviceScale;
        public bool Equals(PreparationKey? other) => other is not null && ReferenceEquals(command, other.command)
            && transform == other.transform && width == other.width && height == other.height
            && deviceScale == other.deviceScale && clips.SequenceEqual(other.clips);
        public override bool Equals(object? other) => other is PreparationKey key && Equals(key);
        public override int GetHashCode()
        {
            HashCode hash = new();
            hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(command));
            hash.Add(transform);
            hash.Add(width);
            hash.Add(height);
            hash.Add(deviceScale);
            foreach (var clip in clips)
            { hash.Add(clip); }
            return hash.ToHashCode();
        }
    }
}
