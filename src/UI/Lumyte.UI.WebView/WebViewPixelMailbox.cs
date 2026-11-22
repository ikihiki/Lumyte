namespace Lumyte.UI.WebView;

internal sealed class WebViewPixelMailbox(WebViewPixelBudget budget) : IDisposable
{
    private readonly object _gate = new();
    private PixelLease? _latest;
    private long _generation;
    private bool _closed;

    public void Dispose()
    {
        lock (_gate)
        {
            _closed = true;
            _latest?.Dispose();
            _latest = null;
        }
    }

    internal void Resize(long generation)
    {
        lock (_gate)
        {
            _generation = generation;
            _latest?.Dispose();
            _latest = null;
        }
    }

    internal bool Publish(ReadOnlySpan<byte> pixels, int width, int height, long generation)
    {
        int bytes = checked(checked(width * height) * 4);
        if (width <= 0 || height <= 0 || pixels.Length != bytes)
        {
            throw new ArgumentOutOfRangeException(nameof(pixels));
        }

        lock (_gate)
        {
            if (_closed || generation != _generation)
            {
                return false;
            }

            _latest?.Dispose();
            _latest = null;
            if (!budget.TryReserve(bytes))
            {
                return false;
            }

            try
            {
                _latest = new(budget, pixels.ToArray(), width, height, generation);
                return true;
            }
            catch
            {
                budget.Release(bytes);
                throw;
            }
        }
    }

    internal bool IsCurrent(long generation)
    {
        lock (_gate)
        {
            return !_closed && _generation == generation;
        }
    }

    internal void Return(PixelLease pixels)
    {
        lock (_gate)
        {
            if (!_closed && pixels.Generation == _generation && _latest is null)
            {
                _latest = pixels;
            }
            else
            {
                pixels.Dispose();
            }
        }
    }

    internal PixelLease? Take()
    {
        lock (_gate)
        {
            PixelLease? result = _latest;
            _latest = null;
            return result;
        }
    }

    internal sealed class PixelLease(WebViewPixelBudget owner, byte[] bytes, int width, int height, long generation) : IDisposable
    {
        private int _disposed;

        internal byte[] Bytes { get; } = bytes;

        internal int Width { get; } = width;

        internal int Height { get; } = height;

        internal long Generation { get; } = generation;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release(Bytes.Length);
            }
        }
    }
}
