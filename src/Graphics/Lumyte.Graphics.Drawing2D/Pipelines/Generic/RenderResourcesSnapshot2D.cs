using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

// Deliberately host-only. Resource IDs on the wire index these independent immutable lists.
internal sealed class RenderResourcesSnapshot2D : IImmutableSchemaMetadata
{
    internal RenderResourcesSnapshot2D(IReadOnlyList<DrawingCommand2D> commands, Action<long>? reserve = null)
    {
        var visited = new HashSet<PaintNode2D>(ReferenceEqualityComparer.Instance);
        var views = new List<IGraphicsTextureView>();
        var samplers = new List<IGraphicsSampler>();
        void View(IGraphicsTextureView v)
        {
            if (!views.Any(x => ReferenceEquals(x, v)))
            {
                reserve?.Invoke(64);
                views.Add(v);
            }
        }

        void Sampler(IGraphicsSampler s)
        {
            if (!samplers.Any(x => ReferenceEquals(x, s)))
            {
                reserve?.Invoke(64);
                samplers.Add(s);
            }
        }

        void Paint(PaintNode2D p, int depth)
        {
            if (visited.Contains(p))
            {
                return;
            }

            reserve?.Invoke(128);
            visited.Add(p);
            if (depth > 64)
            {
                throw new NotSupportedException("Paint resource traversal exceeds 64 levels.");
            }

            switch (p)
            {
                case ImagePaintNode2D x: View(x.Pattern.View); break;
                case DistanceFieldPaintNode2D x: View(x.Pattern.View); Paint(x.Child, depth + 1); break;
                case ClipPaintNode2D x: Paint(x.Child, depth + 1); break;
                case TransformPaintNode2D x: Paint(x.Child, depth + 1); break;
                case OpacityPaintNode2D x: Paint(x.Child, depth + 1); break;
                case CompositePaintNode2D x: Paint(x.Source, depth + 1); Paint(x.Backdrop, depth + 1); break;
                case LayersPaintNode2D x:
                    foreach (PaintNode2D child in x.Children)
                    {
                        Paint(child, depth + 1);
                    }

                    break;
            }
        }

        foreach (DrawingCommand2D c in commands)
        {
            PaintGraph2D? p = c switch
            {
                FillRectCommand2D x => x.Paint,
                FillRoundedRectCommand2D x => x.Paint,
                FillCircleCommand2D x => x.Paint,
                DrawLineCommand2D x => x.Paint,
                FillPathCommand2D x => x.Paint,
                StrokePathCommand2D x => x.Paint,
                DrawCoverageMaskCommand2D x => x.Paint,
                DrawDistanceFieldCommand2D x => x.Paint,
                DrawMeshCommand2D x => x.Paint,
                DrawPaintGraphCommand2D x => x.Graph,
                _ => null,
            };
            if (p is not null)
            {
                Paint(p.Root, 0);
            }

            switch (c)
            {
                case DrawImageCommand2D x: View(x.Image.View); Sampler(x.Image.Sampler); break;
                case DrawCoverageMaskCommand2D x: View(x.Mask.View); Sampler(x.Mask.Sampler); break;
                case DrawDistanceFieldCommand2D x: View(x.Field.View); Sampler(x.Field.Sampler); break;
            }
        }

        Views = views.AsReadOnly();
        Samplers = samplers.AsReadOnly();
    }

    internal IReadOnlyList<IGraphicsTextureView> Views { get; }

    internal IReadOnlyList<IGraphicsSampler> Samplers { get; }

    internal uint ViewId(IGraphicsTextureView v) => checked((uint)Enumerable.Range(0, Views.Count).First(i => ReferenceEquals(Views[i], v)));

    internal uint SamplerId(IGraphicsSampler s) => checked((uint)Enumerable.Range(0, Samplers.Count).First(i => ReferenceEquals(Samplers[i], s)));
}
