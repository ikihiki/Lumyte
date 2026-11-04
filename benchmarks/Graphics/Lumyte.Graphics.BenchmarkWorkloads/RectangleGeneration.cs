using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.BenchmarkWorkloads;

/// <summary>A real frozen public Scene snapshot or the equivalent immutable Canvas recording.</summary>
public sealed class RectangleGeneration : IDisposable
{
    private int _owners = 1;
    private bool _released;

    internal RectangleGeneration(int frame, DrawingSnapshot2D? snapshot, DrawList2D? recording)
    {
        Frame = frame;
        Snapshot = snapshot;
        Recording = recording;
    }

    /// <summary>Gets the source logical frame.</summary>
    public int Frame { get; }

    /// <summary>Gets the public frozen Scene plan, or null for Canvas.</summary>
    public DrawingSnapshot2D? Snapshot { get; }

    /// <summary>Gets the immutable Canvas history, or null for Scene.</summary>
    public DrawList2D? Recording { get; }

    /// <summary>Pins this generation through GPU completion even if the rolling history evicts it.</summary>
    /// <returns>An independently disposable completion owner.</returns>
    public IDisposable Hold()
    {
        ObjectDisposedException.ThrowIf(_owners == 0, this);
        _owners++;
        return new CompletionOwner(this);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_released)
        {
            _released = true;
            Release();
        }
    }

    private void Release()
    {
        if (--_owners == 0)
        {
            Snapshot?.Dispose();
        }
    }

    private sealed class CompletionOwner(RectangleGeneration generation) : IDisposable
    {
        private RectangleGeneration? _generation = generation;

        public void Dispose()
        {
            _generation?.Release();
            _generation = null;
        }
    }
}
