namespace Lumyte.Graphics.Drawing2D;

/// <summary>A short-lived read guard. Keep it alive until asynchronous renderer preparation finishes.</summary>
/// <remarks>All scene mutations are rejected until every borrow is disposed. Do not dispose concurrently with consumption.</remarks>
public sealed class DrawingSceneReadLease2D : IDisposable, IDrawingSceneSource2D
{
    private readonly DrawingScene2D _scene;
    private readonly ulong _revision;
    private readonly IReadOnlyList<DrawingSceneEntry2D> _entries;
    private bool _disposed;

    internal DrawingSceneReadLease2D(DrawingScene2D scene, ulong revision, IReadOnlyList<DrawingSceneEntry2D> entries)
    {
        _scene = scene;
        _revision = revision;
        _entries = entries;
    }

    /// <summary>Gets the captured scene revision while the lease is alive.</summary>
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

    /// <summary>Releases the read guard. Previously obtained views must no longer be consumed.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _scene.ReleaseBorrow();
        }
    }
}
