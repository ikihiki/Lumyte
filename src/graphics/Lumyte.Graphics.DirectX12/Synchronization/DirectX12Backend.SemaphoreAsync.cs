namespace Lumyte.Graphics.DirectX12;

public sealed partial class DirectX12Backend
{
    private sealed partial class NativeSemaphore
    {
        private readonly object asyncWaitGate = new();
        private int activeAsyncWaits;

        public override ValueTask WaitAsync(ulong value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (asyncWaitGate)
            {
                VerifyAvailable();
                activeAsyncWaits = checked(activeAsyncWaits + 1);
            }
            return ObserveAsync(value, cancellationToken);
        }

        private async ValueTask ObserveAsync(ulong value, CancellationToken cancellationToken)
        {
            try { await base.WaitAsync(value, cancellationToken).ConfigureAwait(false); }
            finally { lock (asyncWaitGate) { activeAsyncWaits--; } }
        }
    }
}
