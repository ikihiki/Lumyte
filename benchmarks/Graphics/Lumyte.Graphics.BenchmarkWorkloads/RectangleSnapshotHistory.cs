namespace Lumyte.Graphics.BenchmarkWorkloads;

/// <summary>Optional past changed generations; static frames never create duplicate history.</summary>
public sealed class RectangleSnapshotHistory : IDisposable
{
    private readonly int _capacity;
    private readonly Queue<RectangleGeneration> _items = new();
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="RectangleSnapshotHistory"/> class.</summary>
    /// <param name="capacity">Past generations to retain.</param>
    public RectangleSnapshotHistory(int capacity)
    {
        if (capacity is not (0 or 1 or 3))
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    /// <summary>Gets the current oldest-to-newest generations. Do not dispose these borrowed owners.</summary>
    public IReadOnlyCollection<RectangleGeneration> Items => _items;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            while (_items.TryDequeue(out RectangleGeneration? item))
            {
                item.Dispose();
            }
        }
    }

    internal void Capture(RectangleFixture fixture, int frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_capacity == 0)
        {
            return;
        }

        _items.Enqueue(new(frame, fixture.Retained ? fixture.Scene.CaptureSnapshot() : null, fixture.Recording));
        if (_items.Count > _capacity)
        {
            _items.Dequeue().Dispose();
        }
    }
}
