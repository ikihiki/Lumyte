using System.Numerics;
using System.Text.Json;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Incremental.Benchmarks;

internal static class DirectRectangleChecks
{
    internal static void Run()
    {
        int cases = 0;
        foreach (int count in new[] { 63, 64, 65, 1000, 1024, 1025, 10000 })
        {
            foreach (string pattern in new[] { "static", "single", "clustered", "scattered", "all" })
            {
                var legacy = new ComparisonCase(count, pattern, "rectangles", 1, false, false);
                var flatten = new ComparisonCase(count, pattern, "rectangles", 1, true, true);
                var direct = new DirectRectangleWorkload(count, pattern);
                DirectRectangleScene old = direct.Scene;
                FillRectCommand2D[] snapshot = Enumerable.Range(0, count).Select(old.GetCommand).ToArray();
                for (int frame = 0; frame < 132; frame++)
                {
                    DrawList2D reference = legacy.LegacyFrame();
                    DrawList2D compatible = flatten.RetainedAndFlattenFrame();
                    DirectRectangleScene current = direct.Update();
                    Equal(reference, current);
                    Equal(compatible, current);
                    for (int i = 0; i < count; i++)
                    {
                        Require(ReferenceEquals(snapshot[i], old.GetCommand(i)), "An old scene changed.");
                    }

                    Require(direct.PackedCommands == (pattern == "static" ? 0 : pattern == "all" ? count : pattern == "single" ? 1 : Math.Max(1, count / 100)), "Operation-local pack count differs.");
                    if (pattern == "static")
                    {
                        Require(ReferenceEquals(old, current) && direct.CopiedCpuBytes == 0, "Static scene was copied.");
                    }
                }

                cases++;
            }
        }

        CheckOwnershipAndRejection();
        Console.WriteLine(JsonSerializer.Serialize(new { check = "direct-cpu", status = "passed", cases, framesPerCase = 132, oldSnapshots = true, scope = "commands, ordering, transforms, paint, immutable input; no GPU claim" }));
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    internal static void Reject<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void Equal(DrawList2D expected, DirectRectangleScene actual)
    {
        Require(expected.Commands.Count == actual.Count, "Count differs.");
        for (int i = 0; i < actual.Count; i++)
        {
            Require(
                expected.Commands[i] is FillRectCommand2D a && a.Rect == actual.GetCommand(i).Rect && a.Paint.Root.Equals(actual.GetCommand(i).Paint.Root)
                && a.State.Transform == actual.GetCommand(i).State.Transform && a.State.Clip.Elements.Count == actual.GetCommand(i).State.Clip.Elements.Count,
                "Direct command differs from reference.");
        }
    }

    private static void CheckOwnershipAndRejection()
    {
        var workload = new DirectRectangleWorkload(1025, "single");
        FillRectCommand2D[] input = Enumerable.Range(0, 1025).Select(workload.Scene.GetCommand).ToArray();
        FillRectCommand2D original = input[64];
        var scene = DirectRectangleScene.Create(input);
        input[64] = input[64] with { Rect = new(900, 900, 3, 3) };
        Require(ReferenceEquals(scene.GetCommand(64), original), "Create retained mutable input.");
        var edits = new (int Index, FillRectCommand2D Value)[] { (63, input[64]), (64, input[64]), (1023, input[64]), (1024, input[64]) };
        DirectRectangleScene changed = scene.Update(edits);
        edits[0] = (0, original);
        Require(ReferenceEquals(changed.GetCommand(63), input[64]) && ReferenceEquals(scene.GetCommand(64), original), "Update retained mutable input or modified its ancestor.");
        FillRectCommand2D invalid = original with { State = new(Matrix3x2.CreateTranslation(1, 0), ClipStack2D.Empty) };
        Reject<NotSupportedException>(() => scene.Update([(0, input[64]), (64, invalid)]));
        Reject<NotSupportedException>(() => DirectRectangleScene.Create([invalid]));
        Reject<NotSupportedException>(() => DirectRectangleScene.Create([original], 512, 1024));
        Reject<ArgumentException>(() => scene.Update([(64, original), (64, original)]));
        Reject<ArgumentException>(() => scene.Update([(64, original), (63, original)]));
        Reject<ArgumentException>(() => scene.Update([(1025, original)]));
        Reject<ArgumentException>(() => scene.Update([(-1, original)]));
        Require(ReferenceEquals(scene.GetCommand(64), original), "Rejected update mutated original scene.");
        Require(ReferenceEquals(scene, scene.Update([])), "Empty update changed root.");
    }
}
