using System.Collections;
using System.Reflection;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Incremental.Benchmarks;

/// <summary>Untimed deterministic CPU checks; throws on failure, requires no GPU.</summary>
internal static class PrototypeChecks
{
    public static void RunChecks()
    {
        var canvas = new Canvas2D();
        int count = (RetainedListPrototype.ChunkSize * 3) + 5;
        var source = new DrawList2D[count];
        for (int i = 0; i < count; i++)
        {
            source[i] = Record(canvas, i);
        }

        var originalPayloads = (DrawList2D[])source.Clone();
        var old = RetainedListPrototype.Create(source);
        DrawList2D replacement = Record(canvas, count + 1);
        source[0] = replacement;
        Assert(ReferenceEquals(old[0], originalPayloads[0]), "Create must copy caller-owned collection storage.");

        RetainedListPrototype sparse = old.ReplaceMany(new[] { (1, replacement), (2, replacement), (RetainedListPrototype.ChunkSize + 1, replacement) });
        AssertSnapshot(old, originalPayloads);
        Assert(sparse.Count == count, "Replacement must retain count.");
        for (int i = 0; i < count; i++)
        {
            bool changed = i == 1 || i == 2 || i == RetainedListPrototype.ChunkSize + 1;
            Assert(ReferenceEquals(sparse[i], changed ? replacement : originalPayloads[i]), "Sparse replacement/order mismatch.");
        }

        Assert(!old.SharesChunkWith(sparse, 1), "Dirty chunk must be copied.");
        Assert(!old.SharesChunkWith(sparse, RetainedListPrototype.ChunkSize), "Second dirty chunk must be copied.");
        Assert(old.SharesChunkWith(sparse, RetainedListPrototype.ChunkSize * 2), "Unchanged chunk must be shared.");
        Assert(old.SharesChunkWith(sparse, count - 1), "Unchanged partial final chunk must be shared.");

        RetainedListPrototype boundary = old.ReplaceMany(new[] { (0, replacement), (count - 1, replacement) });
        Assert(ReferenceEquals(boundary[0], replacement) && ReferenceEquals(boundary[count - 1], replacement), "First/last replacement failed.");
        Assert(boundary.SharesChunkWith(old, RetainedListPrototype.ChunkSize), "Boundary edit must share middle chunks.");

        var denseChanges = new (int Index, DrawList2D Value)[count];
        var sameChanges = new (int Index, DrawList2D Value)[count];
        for (int i = 0; i < count; i++)
        {
            denseChanges[i] = (i, replacement);
            sameChanges[i] = (i, old[i]);
        }

        RetainedListPrototype dense = old.ReplaceMany(denseChanges);
        int visited = 0;
        dense.Visit(payload =>
        {
            Assert(ReferenceEquals(payload, replacement), "Dense replacement failed.");
            visited++;
        });
        Assert(visited == count, "Dense visit count mismatch.");
        Assert(ReferenceEquals(old.ReplaceMany(sameChanges), old), "Reference no-op must preserve snapshot identity.");
        Assert(ReferenceEquals(old.ReplaceMany(Array.Empty<(int, DrawList2D)>()), old), "Empty update must preserve identity.");
        AssertSnapshot(old, originalPayloads);

        // A further branch of edits cannot change either retained predecessor.
        _ = sparse.ReplaceMany(new[] { (1, originalPayloads[1]), (count - 1, replacement) });
        Assert(ReferenceEquals(sparse[1], replacement), "Later snapshot changed its predecessor.");
        AssertSnapshot(old, originalPayloads);

        Expect<ArgumentOutOfRangeException>(() => old.ReplaceMany(new[] { (-1, replacement) }));
        Expect<ArgumentOutOfRangeException>(() => old.ReplaceMany(new[] { (0, replacement), (count, replacement) }));
        Expect<ArgumentException>(() => old.ReplaceMany(new[] { (1, replacement), (1, replacement) }));
        Expect<ArgumentException>(() => old.ReplaceMany(new[] { (2, replacement), (1, replacement) }));
        Expect<ArgumentNullException>(() => old.ReplaceMany(new[] { (0, replacement), (1, (DrawList2D)null!) }));
        Expect<ArgumentNullException>(() => old.ReplaceMany(null!));
        Expect<ArgumentNullException>(() => RetainedListPrototype.Create(null!));
        Expect<ArgumentNullException>(() => RetainedListPrototype.Create(new[] { replacement, (DrawList2D)null! }));
        Expect<ArgumentOutOfRangeException>(() => { _ = old[-1]; });
        Expect<ArgumentOutOfRangeException>(() => { _ = old[count]; });
        Expect<ArgumentNullException>(() => old.Visit(null!));
        AssertSnapshot(old, originalPayloads);

        var empty = RetainedListPrototype.Create(Array.Empty<DrawList2D>());
        Assert(empty.Count == 0, "Empty count mismatch.");
        empty.Visit(_ => throw new InvalidOperationException("Empty list visited a payload."));
        Assert(ReferenceEquals(empty.ReplaceMany(Array.Empty<(int, DrawList2D)>()), empty), "Empty list no-op failed.");
        Expect<ArgumentOutOfRangeException>(() => empty.ReplaceMany(new[] { (0, replacement) }));

        // Untimed reflection inspects actual CPU command records, not merely fragment
        // identity. Commands is internal; no production visibility changes are needed.
        // The same Canvas was repeatedly Begin/End-reused after recording this payload.
        CheckRecordedRectangle(originalPayloads[0], 0);
        CheckRecordedRectangle(originalPayloads[count - 1], count - 1);
        CheckRecordedRectangle(replacement, count + 1);
    }

    private static DrawList2D Record(Canvas2D canvas, int marker)
    {
        canvas.Begin();
        canvas.FillRect(new Rect2D(marker, 2, 3, 4), new Color2D(0.25f, 0.5f, 0.75f, 1));
        return canvas.End();
    }

    private static void AssertSnapshot(RetainedListPrototype snapshot, IReadOnlyList<DrawList2D> expected)
    {
        Assert(snapshot.Count == expected.Count, "Snapshot count changed.");
        int index = 0;
        snapshot.Visit(payload =>
        {
            Assert(ReferenceEquals(payload, expected[index]), "Visit painter order changed.");
            Assert(ReferenceEquals(snapshot[index], expected[index]), "Indexed snapshot payload changed.");
            index++;
        });
        Assert(index == expected.Count, "Visit omitted payloads.");
    }

    private static void CheckRecordedRectangle(DrawList2D payload, int marker)
    {
        PropertyInfo property = typeof(DrawList2D).GetProperty("Commands", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DrawList2D command inspection contract changed.");
        var commands = (IList)(property.GetValue(payload) ?? throw new InvalidOperationException("Missing command storage."));
        Assert(commands.Count == 1, "Canvas reuse changed recorded command count.");
        object command = commands[0] ?? throw new InvalidOperationException("Missing rectangle command.");
        object? rectangle = command.GetType().GetProperty("Rect")?.GetValue(command);
        Assert(rectangle is Rect2D rect && rect.Equals(new Rect2D(marker, 2, 3, 4)), "Canvas reuse changed recorded rectangle values.");
    }

    private static void Expect<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
