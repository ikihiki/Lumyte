namespace Lumyte.UI.Application;

internal sealed class UiVisualBudget(UiMotionOptions options)
{
    private readonly object _gate = new();
    private int _active;
    private long _pixels;
    private long _bytes;

    internal IDisposable? BeginTransition()
    {
        lock (_gate)
        {
            if (_active >= options.MaximumActiveTransitions)
            {
                return null;
            }

            _active++;
            return new Reservation(() =>
            {
                lock (_gate)
                {
                    _active--;
                }
            });
        }
    }

    internal IDisposable? Reserve(uint width, uint height)
    {
        ulong rawPixels = (ulong)width * height;
        if (rawPixels > long.MaxValue / 8)
        {
            return null;
        }

        long pixels = (long)rawPixels;
        long bytes = checked(pixels * 8);
        lock (_gate)
        {
            if (pixels > options.MaximumSnapshotPixels - _pixels || bytes > options.MaximumSnapshotBytes - _bytes)
            {
                return null;
            }

            _pixels += pixels;
            _bytes += bytes;
            return new Reservation(() =>
            {
                lock (_gate)
                {
                    _pixels -= pixels;
                    _bytes -= bytes;
                }
            });
        }
    }

    private sealed class Reservation(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
