using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12Submission(Direct3D12Device owner, Direct3D12CommandBuffer[] commands, nint fence, bool completionIssued = true, Direct3D12Semaphore[]? waits = null, Direct3D12Semaphore[]? signals = null) : IGraphicsSubmission
{
    private readonly object _gate = new();
    private readonly Direct3D12Semaphore[] _waits = waits ?? [];
    private readonly Direct3D12Semaphore[] _signals = signals ?? [];
    private SubmissionStatus _status;
    private Task? _completion;
    private bool _completionIssued = completionIssued;
    private bool _hasNativeUse = completionIssued;
    private bool _commandsIssued = completionIssued;

    public SubmissionStatus Status
    {
        get
        {
            lock (_gate)
            {
                if (_status != SubmissionStatus.Pending || !_completionIssued)
                {
                    return _status;
                }

                ulong value = owner.QueueDriver.Completed(fence);
                if (value == ulong.MaxValue)
                {
                    SetCompleted(false);
                    try
                    {
                        owner.Check(unchecked((int)0x887A0005));
                    }
                    catch (System.Runtime.InteropServices.COMException)
                    {
                        // Status reports failure without throwing native device-removal errors.
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

    internal bool HasNativeUse => _hasNativeUse;

    internal Direct3D12Submission? NextFailed { get; set; }

    internal Exception? RecoveryError { get; private set; }

    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task completion;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_status == SubmissionStatus.Disposed, this);
            if (!_completionIssued)
            {
                throw new InvalidOperationException("No native completion marker has been issued for this partial submission.");
            }

            completion = Status == SubmissionStatus.Pending
                ? _completion ??= owner.QueueDriver.WaitForCompletion(owner, fence)
                : Task.CompletedTask;
        }

        // Cancellation affects this caller, never the shared native registration.
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

            if (owner.Validation != null && Status == SubmissionStatus.Pending)
            {
                throw new InvalidOperationException("Validation: wait for actual submission completion before disposal.");
            }

            owner.QueueDriver.Release(fence);
            _status = SubmissionStatus.Disposed;
        }
    }

    internal void MarkNativeUse() => _hasNativeUse = true;

    internal void MarkCommandsIssued()
    {
        _hasNativeUse = true;
        _commandsIssued = true;
    }

    internal void EnsureCompletionSignal()
    {
        lock (_gate)
        {
            if (_completionIssued)
            {
                return;
            }

            owner.QueueDriver.SignalCompletion(owner, fence);
            _completionIssued = true;
            _hasNativeUse = true;
        }
    }

    internal void TrySignalRecoveryCompletion()
    {
        try
        {
            EnsureCompletionSignal();
        }
        catch (Exception error)
        {
            // Preserve the original queue failure. The retained owner keeps every
            // issued dependency until retry or confirmed device retirement.
            RecoveryError = error;
        }
    }

    internal void AbandonUnissued()
    {
        lock (_gate)
        {
            if (_hasNativeUse)
            {
                throw new InvalidOperationException("Cannot abandon native-issued submission ownership.");
            }

            if (_status != SubmissionStatus.Disposed)
            {
                owner.QueueDriver.Release(fence);
                _status = SubmissionStatus.Disposed;
            }
        }
    }

    internal bool TryAbandonUnissued()
    {
        try
        {
            AbandonUnissued();
            return true;
        }
        catch (Exception error)
        {
            RecoveryError = error;
            return false;
        }
    }

    internal void RetireAfterDeviceRemoval()
    {
        lock (_gate)
        {
            if (_status == SubmissionStatus.Disposed)
            {
                return;
            }

            SetCompleted(false);
            owner.QueueDriver.Release(fence);
            _status = SubmissionStatus.Disposed;
        }
    }

    private void SetCompleted(bool success)
    {
        _status = success ? SubmissionStatus.Completed : SubmissionStatus.Failed;
        owner.Validation?.CompleteSemaphores(this);
        GC.KeepAlive(_waits);
        GC.KeepAlive(_signals);
        foreach (Direct3D12CommandBuffer command in commands)
        {
            if (_commandsIssued)
            {
                command.Complete(success);
            }
        }
    }
}
