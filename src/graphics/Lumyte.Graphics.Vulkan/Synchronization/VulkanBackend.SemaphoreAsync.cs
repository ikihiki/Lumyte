namespace Lumyte.Graphics.Vulkan;

public sealed partial class VulkanBackend
{
    private sealed partial class SemaphoreRecord
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
