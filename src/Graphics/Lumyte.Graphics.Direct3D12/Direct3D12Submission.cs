using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12Submission(Direct3D12Device owner, Direct3D12CommandBuffer[] commands, nint fence) : IGraphicsSubmission
{
    private readonly object _gate = new();
    private SubmissionStatus _status;
    private Task? _completion;

    public SubmissionStatus Status
    {
        get
        {
            lock (_gate)
            {
                if (_status != SubmissionStatus.Pending)
                {
                    return _status;
                }

                ulong value = NativeMethods.FenceCompleted(fence);
                if (value == ulong.MaxValue)
                {
                    SetCompleted(false);
                    try
                    {
                        owner.Check(unchecked((int)0x887A0005));
                    }
                    catch (System.Runtime.InteropServices.COMException)
                    {
                        // Status reports failure without throwing the native device-removal error.
                    }
                }
                else if (value >= 1)
                {
                    SetCompleted(true);
                }

                return _status;
            }
        }
    }

    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task completion;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_status == SubmissionStatus.Disposed, this);
            completion = Status == SubmissionStatus.Pending
                ? _completion ??= new Direct3D12FenceWait(owner, fence).Completion
                : Task.CompletedTask;
        }

        // Cancellation affects this caller, never the native registration or
        // other callers sharing its completion task.
        await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_status == SubmissionStatus.Disposed, this);
            if (Status == SubmissionStatus.Failed)
            {
                throw new InvalidOperationException("Direct3D 12 submission failed because the device was removed.");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_status == SubmissionStatus.Disposed)
            {
                return;
            }

            NativeMethods.Release(fence);
            _status = SubmissionStatus.Disposed;
        }
    }

    private void SetCompleted(bool success)
    {
        _status = success ? SubmissionStatus.Completed : SubmissionStatus.Failed;
        owner.Validation?.CompleteSemaphores(this);
        foreach (Direct3D12CommandBuffer command in commands)
        {
            command.Complete(success);
        }
    }
}
