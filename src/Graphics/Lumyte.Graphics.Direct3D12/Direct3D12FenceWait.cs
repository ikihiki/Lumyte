namespace Lumyte.Graphics.Direct3D12;

// The native registration cannot be canceled. Own a separate COM reference and
// keep the event open until signaled, even if every managed caller cancels or
// the submission is disposed. Device removal also signals D3D12 fence events.
internal sealed class Direct3D12FenceWait
{
    private readonly object _gate = new();
    private readonly EventWaitHandle _event = new(false, EventResetMode.ManualReset);
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly nint _fence;
    private RegisteredWaitHandle? _registration;
    private bool _released;

    internal Direct3D12FenceWait(Direct3D12Device owner, nint fence)
    {
        _fence = fence;
        NativeMethods.AddRef(fence);
        lock (_gate)
        {
            try
            {
                // Allocate the managed registration first. Once D3D accepts the
                // event, nothing that can fail remains in the setup path.
                _registration = ThreadPool.RegisterWaitForSingleObject(_event, Signaled, null, Timeout.Infinite, executeOnlyOnce: true);
                owner.Check(NativeMethods.FenceSetEvent(fence, _event.SafeWaitHandle));
            }
            catch
            {
                Release();
                throw;
            }
        }
    }

    internal Task Completion => _completion.Task;

    private void Release()
    {
        _released = true;
        _registration?.Unregister(null);
        _event.Dispose();
        NativeMethods.Release(_fence);
    }

    private void Signaled(object? state, bool timedOut)
    {
        lock (_gate)
        {
            if (!_released)
            {
                Release();
                _completion.TrySetResult();
            }
        }
    }
}
