using System.Numerics;
using System.Text.Json;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Incremental.Benchmarks;

internal static class ComparisonChecks
{
    internal static void Run()
    {
        int cases = 0;
        foreach (int count in new[] { 17, 1000 })
        {
            foreach (int chunk in new[] { 1, 16, 64 })
            {
                foreach (string pattern in new[] { "static", "single", "clustered", "scattered", "all" })
                {
                    foreach (string kind in new[] { "rectangles", "clipped", "mixed" })
                    {
                        var legacy = new ComparisonCase(count, pattern, kind, chunk, false, false);
                        var retained = new ComparisonCase(count, pattern, kind, chunk, true, false);
                        var compatibility = new ComparisonCase(count, pattern, kind, chunk, true, true);
                        RetainedListPrototype old = retained.Retained;
                        DrawList2D previous = Flatten(old);
                        for (int frame = 0; frame < 12; frame++)
                        {
                            DrawList2D flat = legacy.LegacyFrame();
                            retained.RetainedFrame();
                            DrawList2D compatible = compatibility.RetainedAndFlattenFrame();
                            Equal(flat, retained.MaterializeForCheck());
                            Equal(flat, compatible);
                            Equal(previous, Flatten(old));
                            if (pattern == "static" && !ReferenceEquals(old, retained.Retained))
                            {
                                throw new InvalidOperationException("No-op root changed.");
                            }
                        }

                        cases++;
                    }
                }
            }
        }

        foreach (string kind in new[] { "rectangles", "clipped", "mixed" })
        {
            var legacy = new ComparisonCase(1000, "single", kind, 64, false, false);
            var retained = new ComparisonCase(1000, "single", kind, 64, true, true);
            Console.WriteLine(JsonSerializer.Serialize(new { check = "ordered-type-clip-runs", kind, legacyRuns = Runs(legacy.LegacyFrame()), flattenRuns = Runs(retained.RetainedAndFlattenFrame()) }));
        }

        Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", cases, framesPerCase = 12, oldSnapshotRetained = true, scope = "CPU commands/state/order equivalence, not GPU pixels" }));
    }

    private static DrawList2D Flatten(RetainedListPrototype root)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        root.Visit(canvas.Replay);
        return canvas.End();
    }

    private static int Runs(DrawList2D list)
    {
        int count = 0;
        DrawingCommand2D? previous = null;
        foreach (DrawingCommand2D command in list.Commands)
        {
            if (previous is null || previous.GetType() != command.GetType() || !ReferenceEquals(previous.State.Clip, command.State.Clip))
            {
                count++;
            }

            previous = command;
        }

        return count;
    }

    private static void Equal(DrawList2D expected, DrawList2D actual)
    {
        if (expected.Commands.Count != actual.Commands.Count)
        {
            throw new InvalidOperationException("Command count changed.");
        }

        var neutral = new DrawingState2D(Matrix3x2.Identity, ClipStack2D.Empty);
        for (int i = 0; i < expected.Commands.Count; i++)
        {
            DrawingCommand2D a = expected.Commands[i];
            DrawingCommand2D b = actual.Commands[i];
            if (!(a with { State = neutral }).Equals(b with { State = neutral }) || a.State.Transform != b.State.Transform)
            {
                throw new InvalidOperationException("Painter content/order or transform changed.");
            }

            if (a.State.Clip.Elements.Count != b.State.Clip.Elements.Count)
            {
                throw new InvalidOperationException("Clip count changed.");
            }

            for (int c = 0; c < a.State.Clip.Elements.Count; c++)
            {
                ClipElement2D x = a.State.Clip.Elements[c];
                ClipElement2D y = b.State.Clip.Elements[c];
                if (x.Transform != y.Transform || x.Rule != y.Rule || !x.Path.Segments.SequenceEqual(y.Path.Segments))
                {
                    throw new InvalidOperationException("Clip semantics changed.");
                }
            }
        }
    }
}
