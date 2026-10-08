using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class Submission
{
    private readonly WgpuDevice _owner;
    private readonly A.QueueWorkDoneRequest _request;
    private readonly HashSet<GpuResource> _resources;
    private readonly List<ShaderDataTransferState> _shaderDataTransfers;
    private bool _completed;
    private Exception? _error;

    internal Submission(WgpuDevice owner, A.QueueWorkDoneRequest request, HashSet<GpuResource> resources, List<ShaderDataTransferState> transfers)
    {
        _owner = owner;
        _request = request;
        _resources = resources;
        _shaderDataTransfers = transfers;
        owner.EncoderCount++;
    }

    public bool IsCompleted
    {
        get
        {
            lock (_owner.Gate)
            {
                if (_completed)
                {
                    return true;
                }

                _owner.Instance.ProcessEvents();
                if (!_request.IsComplete)
                {
                    return false;
                }

                if (!_request.IsSuccess)
                {
                    _error = new InvalidOperationException($"Queue completion failed: {_request.Status}");
                }

                try
                {
                    _owner.Check();
                }
                catch (Exception ex)
                {
                    _error = ex;
                }

                if (_error is null)
                {
                    foreach (ShaderDataTransferState transfer in _shaderDataTransfers)
                    {
                        if (transfer.Region.Valid)
                        {
                            transfer.Region.Ready = true;
                        }
                    }
                }
                else
                {
                    foreach (ShaderDataTransferState transfer in _shaderDataTransfers)
                    {
                        transfer.Region.Buffer.CancelShaderData(transfer.Region);
                    }
                }

                _shaderDataTransfers.Clear();
                _request.Dispose();
                foreach (GpuResource resource in _resources)
                {
                    resource.ReleaseLease();
                }

                _resources.Clear();
                _completed = true;
                _owner.EncoderCount--;
                return true;
            }
        }
    }

    public void Wait(CancellationToken cancellationToken = default)
    {
        while (!IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(1);
        }

        if (_error is not null)
        {
            throw _error;
        }
    }

    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        while (!IsCompleted)
        {
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }

        if (_error is not null)
        {
            throw _error;
        }
    }
}
