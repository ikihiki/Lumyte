namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed class PipelineSnapshotBudget2D(long limit)
{
    private readonly object _gate = new();
    private readonly long _limit = limit;
    private long _bytes;

    internal Use Acquire() => new(this);

    internal sealed class Use(PipelineSnapshotBudget2D owner) : IDisposable
    {
        private long _bytes;
        private bool _returned;

        public void Dispose()
        {
            lock (owner._gate)
            {
                if (!_returned)
                {
                    owner._bytes = checked(owner._bytes - _bytes);
                    _returned = true;
                }
            }
        }

        internal void Reserve(long bytes)
        {
            lock (owner._gate)
            {
                ObjectDisposedException.ThrowIf(_returned, this);
                long next = checked(owner._bytes + bytes);
                if (bytes < 0 || next > owner._limit)
                {
                    throw new InvalidOperationException("Immutable source snapshot budget exceeded before allocation.");
                }

                owner._bytes = next;
                _bytes = checked(_bytes + bytes);
            }
        }
    }
}
