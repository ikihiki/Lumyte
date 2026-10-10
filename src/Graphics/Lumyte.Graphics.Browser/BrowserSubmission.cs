using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserSubmission(BrowserCommandBuffer[] commands, JSObject handle) : IGraphicsSubmission
{
    private SubmissionStatus _status = SubmissionStatus.Pending;

    public SubmissionStatus Status
    {
        get
        {
            if (_status == SubmissionStatus.Pending)
            {
                _status = (SubmissionStatus)BrowserInterop.GetSubmissionStatus(handle);
                if (_status != SubmissionStatus.Pending)
                {
                    foreach (BrowserCommandBuffer command in commands)
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
        if (Status == SubmissionStatus.Pending)
        {
            await BrowserInterop.WaitSubmissionAsync(handle).WaitAsync(cancellationToken);
        }

        if (Status == SubmissionStatus.Failed)
        {
            throw new InvalidOperationException(BrowserInterop.GetSubmissionError(handle));
        }
    }

    public void Dispose()
    {
        if (_status == SubmissionStatus.Disposed)
        {
            return;
        }

        handle.Dispose();
        _status = SubmissionStatus.Disposed;
    }
}
