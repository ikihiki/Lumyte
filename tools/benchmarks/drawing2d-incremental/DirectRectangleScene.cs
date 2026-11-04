using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Incremental.Benchmarks;

// Benchmark-only, fixed topology. No command replay or full-scene packed buffer.
internal sealed class DirectRectangleScene
{
    internal const int ChunkSize = 64;
    internal const int VectorsPerRectangle = 6;
    private readonly Page[] _pages;

    // Private ownership-taking constructor must not expose mutable array inputs.
#pragma warning disable IDE0290
    private DirectRectangleScene(Page[] pages, int count, int packedCommands, long copiedCpuBytes)
    {
        _pages = pages;
        Count = count;
        LastPackedCommands = packedCommands;
        CopiedCpuBytes = copiedCpuBytes;
    }
#pragma warning restore IDE0290

    internal int Count { get; }

    internal int PageCount => _pages.Length;

    // Operation-local metrics, not cumulative: Create packs Count; Update packs replacements.
    // Empty Update returns this, so callers must report zero for that operation themselves.
    internal int LastPackedCommands { get; }

    // Explicit array-copy bytes only (root references, copied command refs and packed payload).
    // Excludes freshly packed output, allocation zeroing and temporary input snapshot writes.
    internal long CopiedCpuBytes { get; }

    internal ReadOnlySpan<Page> Pages => _pages;

    internal static DirectRectangleScene Create(IReadOnlyList<FillRectCommand2D> commands, uint width = 1024, uint height = 1024)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (width != 1024 || height != 1024)
        {
            throw new NotSupportedException("Only a 1024 by 1024 target with pixel scale one is supported.");
        }

        int count = commands.Count;
        var pages = new Page[checked((int)(((long)count + ChunkSize - 1) / ChunkSize))];
        for (int pageIndex = 0; pageIndex < pages.Length; pageIndex++)
        {
            int start = pageIndex * ChunkSize;
            int length = Math.Min(ChunkSize, count - start);
            var records = new FillRectCommand2D[length];
            var payload = new Vector4[length * VectorsPerRectangle];
            for (int i = 0; i < length; i++)
            {
                FillRectCommand2D command = commands[start + i];
                Validate(command);
                records[i] = command;
                Pack(command, payload.AsSpan(i * VectorsPerRectangle, VectorsPerRectangle));
            }

            pages[pageIndex] = new(records, payload);
        }

        return new(pages, count, count, 0);
    }

    internal DirectRectangleScene Update(IReadOnlyList<(int Index, FillRectCommand2D Value)> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
        {
            return this;
        }

        // Snapshot and validate the entire request before deriving any replacement pages.
        var snapshot = new (int Index, FillRectCommand2D Value)[changes.Count];
        int previous = -1;
        for (int i = 0; i < snapshot.Length; i++)
        {
            (int index, FillRectCommand2D value) = changes[i];
            if (index <= previous || (uint)index >= (uint)Count)
            {
                throw new ArgumentException("Updates must have ascending unique in-range indices.", nameof(changes));
            }

            Validate(value);
            snapshot[i] = (index, value);
            previous = index;
        }

        bool replaceAll = snapshot.Length == Count;
        Page[] pages = replaceAll ? new Page[_pages.Length] : (Page[])_pages.Clone();
        long copied = replaceAll ? 0 : (long)pages.Length * IntPtr.Size;
        int cursor = 0;
        while (cursor < snapshot.Length)
        {
            int pageIndex = snapshot[cursor].Index / ChunkSize;
            Page old = _pages[pageIndex];
            FillRectCommand2D[] records = replaceAll ? new FillRectCommand2D[old.Count] : old.CopyCommands();
            Vector4[] payload = replaceAll ? new Vector4[old.Count * VectorsPerRectangle] : old.CopyPayload();
            if (!replaceAll)
            {
                copied += ((long)records.Length * IntPtr.Size) + ((long)payload.Length * 16);
            }

            do
            {
                (int index, FillRectCommand2D value) = snapshot[cursor++];
                int local = index % ChunkSize;
                records[local] = value;
                Pack(value, payload.AsSpan(local * VectorsPerRectangle, VectorsPerRectangle));
            }
            while (cursor < snapshot.Length && snapshot[cursor].Index / ChunkSize == pageIndex);
            pages[pageIndex] = new(records, payload);
        }

        return new(pages, Count, snapshot.Length, copied);
    }

    internal FillRectCommand2D GetCommand(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
        return _pages[index / ChunkSize].GetCommand(index % ChunkSize);
    }

    private static void Validate(FillRectCommand2D command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.State);
        ArgumentNullException.ThrowIfNull(command.Paint);
        ArgumentNullException.ThrowIfNull(command.State.Clip);
        if (command.State.Transform != Matrix3x2.Identity || command.State.Clip.Elements.Count != 0 || command.Paint.Root is not SolidPaintNode2D)
        {
            throw new NotSupportedException("Only ordered unclipped identity-transform solid rectangles are supported.");
        }

        ModelValidation2D.Rectangle(command.Rect, nameof(command));
    }

    private static void Pack(FillRectCommand2D command, Span<Vector4> values)
    {
        Rect2D rect = command.Rect;
        var low = new Vector2(rect.X, rect.Y);
        var high = new Vector2(rect.X + rect.Width, rect.Y + rect.Height);
        var minimum = Vector2.Max(Vector2.Zero, low - Vector2.One);
        var maximum = Vector2.Min(new(1024, 1024), high + Vector2.One);
        float x = Math.Clamp(MathF.Floor(minimum.X), 0, 1024);
        float y = Math.Clamp(MathF.Floor(minimum.Y), 0, 1024);
        float right = Math.Clamp(MathF.Ceiling(maximum.X), x, 1024);
        float bottom = Math.Clamp(MathF.Ceiling(maximum.Y), y, 1024);
        values[0] = new(x, y, right - x, bottom - y);
        values[1] = new(rect.X, rect.Y, rect.Width, rect.Height);
        values[2] = new(1, 0, 0, 0);
        values[3] = new(0, 1, 0, 0);
        values[4] = DrawingProgram2D.Linear(((SolidPaintNode2D)command.Paint.Root).Color);
        values[5] = new(0, 0, 1, 0); // No clips: shader never dereferences the clip offset.
    }

    internal sealed class Page(FillRectCommand2D[] commands, Vector4[] payload)
    {
        private readonly FillRectCommand2D[] _commands = commands;
        private readonly Vector4[] _payload = payload;

        internal int Count => _commands.Length;

        internal ReadOnlySpan<Vector4> Payload => _payload;

        internal FillRectCommand2D GetCommand(int index) => _commands[index];

        internal FillRectCommand2D[] CopyCommands() => (FillRectCommand2D[])_commands.Clone();

        internal Vector4[] CopyPayload() => (Vector4[])_payload.Clone();
    }
}
