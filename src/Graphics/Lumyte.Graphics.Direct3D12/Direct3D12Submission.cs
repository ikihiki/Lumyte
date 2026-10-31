using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12Submission(Direct3D12Device owner, Direct3D12CommandBuffer[] commands, nint fence) : IGraphicsSubmission
{
    private SubmissionStatus _status;

    public SubmissionStatus Status
    {
        get
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

    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (Status == SubmissionStatus.Pending)
        {
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }

        ObjectDisposedException.ThrowIf(_status == SubmissionStatus.Disposed, this);
        if (_status == SubmissionStatus.Failed)
        {
            throw new InvalidOperationException("Direct3D 12 submission failed because the device was removed.");
        }
    }

    public void Dispose()
    {
        if (_status == SubmissionStatus.Disposed)
        {
            return;
        }

        NativeMethods.Release(fence);
        _status = SubmissionStatus.Disposed;
    }

    private void SetCompleted(bool success)
    {
        _status = success ? SubmissionStatus.Completed : SubmissionStatus.Failed;
        foreach (Direct3D12CommandBuffer command in commands)
        {
            command.Complete(success);
        }
    }
}
