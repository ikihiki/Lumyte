namespace Lumyte.Graphics.Drawing2D;

// Deliberately conservative preflight: no arrays, normalization or path expansion occur here.
internal static class DrawingWorkingBudget2D
{
    internal static void Validate(DrawList2D content, DrawingTargetInfo2D target, long maximum)
    {
        if (checked(4096L + (content.Count * 512L)) > maximum)
        {
            throw new NotSupportedException("Command materialization exceeds the preallocation working budget.");
        }

        long bytes = 4096;
        foreach (DrawingCommand2D command in Commands(content))
        {
            bytes = checked(bytes + 8192);
            if (command is FillPathCommand2D or StrokePathCommand2D or DrawLineCommand2D)
            {
                bytes = checked(bytes + (32L * 1024 * 1024));
            }

            if (command is DrawMeshCommand2D mesh)
            {
                bytes = checked(bytes + (mesh.Mesh.Indices.Count * 128L));
            }

            if (command.State.Clip.Elements.Count != 0)
            {
                bytes = checked(bytes + (32L * 1024 * 1024) + (Math.Min(1048576L, (long)target.Description.Width * target.Description.Height) * 8));
            }

            PaintNode2D? paint = command switch
            {
                FillRectCommand2D c => c.Paint.Root,
                FillRoundedRectCommand2D c => c.Paint.Root,
                FillCircleCommand2D c => c.Paint.Root,
                FillPathCommand2D c => c.Paint.Root,
                StrokePathCommand2D c => c.Paint.Root,
                DrawLineCommand2D c => c.Paint.Root,
                DrawCoverageMaskCommand2D c => c.Paint.Root,
                DrawDistanceFieldCommand2D c => c.Paint.Root,
                DrawMeshCommand2D c => c.Paint.Root,
                DrawPaintGraphCommand2D c => c.Graph.Root,
                _ => null,
            };
            if (paint is not null)
            {
                CountPaint(paint, 0);
            }

            Check();
        }

        void Check()
        {
            if (bytes > maximum)
            {
                throw new NotSupportedException("Input preparation exceeds its conservative preallocation working budget.");
            }
        }

        void CountPaint(PaintNode2D node, int depth)
        {
            if (depth > 128)
            {
                throw new NotSupportedException("Paint exceeds the preparation depth budget.");
            }

            bytes = checked(bytes + 4096);
            Check();
            switch (node)
            {
                case LinearPaintNode2D c:
                    bytes = checked(bytes + (c.Stops.Count * 128L));
                    break;
                case RadialPaintNode2D c:
                    bytes = checked(bytes + (c.Stops.Count * 128L));
                    break;
                case SweepPaintNode2D c:
                    bytes = checked(bytes + (c.Stops.Count * 128L));
                    break;
                case CompositePaintNode2D c:
                    CountPaint(c.Source, depth + 1);
                    CountPaint(c.Backdrop, depth + 1);
                    break;
                case LayersPaintNode2D c:
                    foreach (PaintNode2D child in c.Children)
                    {
                        CountPaint(child, depth + 1);
                    }

                    break;
                case OpacityPaintNode2D c:
                    CountPaint(c.Child, depth + 1);
                    break;
                case TransformPaintNode2D c:
                    CountPaint(c.Child, depth + 1);
                    break;
                case DistanceFieldPaintNode2D c:
                    CountPaint(c.Child, depth + 1);
                    break;
                case ClipPaintNode2D c:
                    bytes = checked(bytes + (32L * 1024 * 1024) + (Math.Min(1048576L, (long)target.Description.Width * target.Description.Height) * 8));
                    CountPaint(c.Child, depth + 1);
                    break;
            }

            Check();
        }
    }

    private static IEnumerable<DrawingCommand2D> Commands(DrawList2D content)
    {
        if (content.HasRetainedPages)
        {
            foreach (DrawList2D page in content.RetainedPages.Pages)
            {
                foreach (DrawingCommand2D command in page.Commands)
                {
                    yield return command;
                }
            }
        }
        else
        {
            foreach (DrawingCommand2D command in content.Commands)
            {
                yield return command;
            }
        }
    }
}
