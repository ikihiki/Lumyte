using Lumyte.Graphics.Abstractions;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuSubmission : IGraphicsSubmission
{
    private readonly WgpuDevice _owner;
    private readonly WgpuCommandBuffer[] _commands;
    private readonly A.QueueWorkDoneRequest _request;
    private readonly Func<bool> _isComplete;
    private SubmissionStatus _status = SubmissionStatus.Pending;

    internal WgpuSubmission(WgpuDevice owner, WgpuCommandBuffer[] commands)
    {
        (_owner, _commands) = (owner, commands);
        _request = owner.NativeDevice.Queue.BeginOnSubmittedWorkDone();
        _isComplete = () => _request.IsComplete;
        owner.TrackRequest(_isComplete);
    }

    public SubmissionStatus Status
    {
        get
        {
            if (_status == SubmissionStatus.Pending)
            {
                _owner.ProcessEvents();
                if (_request.IsComplete || _owner.Status == GraphicsDeviceStatus.Lost)
                {
                    _status = _owner.Status == GraphicsDeviceStatus.Ready && _request.IsSuccess ? SubmissionStatus.Completed : SubmissionStatus.Failed;
                    foreach (WgpuCommandBuffer command in _commands)
                    {
                        command.Complete(_status == SubmissionStatus.Completed);
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
            if (_owner.Loss is { } loss)
            {
                throw new GraphicsDeviceLostException(loss);
            }

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

        _owner.ValidateDisposal();
        if (!_request.IsComplete)
        {
            throw new InvalidOperationException("Confirm native request retirement before releasing its callback storage.");
        }

        _owner.UntrackRequest(_isComplete);
        _request.Dispose();
        _status = SubmissionStatus.Disposed;
    }
}
