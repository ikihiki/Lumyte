using Lumyte.Graphics.Abstractions;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuSubmission(WgpuDevice owner, WgpuCommandBuffer[] commands) : IGraphicsSubmission
{
    private readonly A.QueueWorkDoneRequest _request = owner.NativeDevice.Queue.BeginOnSubmittedWorkDone();
    private SubmissionStatus _status = SubmissionStatus.Pending;

    public SubmissionStatus Status
    {
        get
        {
            if (_status == SubmissionStatus.Pending)
            {
                owner.NativeDevice.ProcessEvents();
                if (_request.IsComplete)
                {
                    _status = _request.IsSuccess ? SubmissionStatus.Completed : SubmissionStatus.Failed;
                    foreach (WgpuCommandBuffer command in commands)
                    {
                        command.Complete(_request.IsSuccess);
                    }
                }
            }

            return _status;
        }
    }

    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_status == SubmissionStatus.Disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        while (Status == SubmissionStatus.Pending)
        {
            await Task.Delay(1, cancellationToken);
        }

        if (_status == SubmissionStatus.Failed)
        {
            throw new InvalidOperationException($"WebGPU submission failed: {_request.Status}.");
        }
    }

    public void Dispose()
    {
        if (_status == SubmissionStatus.Disposed)
        {
            return;
        }

        if (Status == SubmissionStatus.Pending)
        {
            throw new InvalidOperationException("Complete the submission before disposal.");
        }

        _request.Dispose();
        _status = SubmissionStatus.Disposed;
        owner.ReleaseSubmission();
    }
}
