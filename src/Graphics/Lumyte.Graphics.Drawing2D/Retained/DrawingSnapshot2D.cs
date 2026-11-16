namespace Lumyte.Graphics.Drawing2D;

/// <summary>An explicit frozen placement plan, independent of later edits and scene disposal.</summary>
/// <remarks>Immutable commands and pages remain shared. Borrowed external GPU resources retain their existing caller-owned lifetime.</remarks>
public sealed class DrawingSnapshot2D : IDisposable, IDrawingSceneSource2D
{
    private readonly ulong _revision;
    private readonly IReadOnlyList<DrawingSceneEntry2D> _entries;
    private readonly IDisposable _resources;
    private bool _disposed;

    internal DrawingSnapshot2D(ulong revision, DrawingSceneEntry2D[] entries)
    {
        _revision = revision;
        _entries = Array.AsReadOnly(entries);
        _resources = ImageResourceRegistry2D.Retain(entries.Select(entry => entry.Content));
    }

    /// <summary>Gets the frozen scene revision.</summary>
    public ulong Revision
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _revision;
        }
    }

    /// <inheritdoc/>
    IReadOnlyList<DrawingSceneEntry2D> IDrawingSceneSource2D.Entries
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _entries;
        }
    }

    /// <summary>Ends consumption of this snapshot; does not release caller-owned GPU resources.</summary>
    public void Dispose()
    {
        _disposed = true;
        _resources.Dispose();
    }

    internal IDisposable RetainResources()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ImageResourceRegistry2D.Retain(_entries.Select(entry => entry.Content));
    }
}
