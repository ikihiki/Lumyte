using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed class VulkanSubmission(VulkanDevice owner, VulkanCommandBuffer[] commands, Fence fence) : IGraphicsSubmission
{
    private SubmissionStatus _status = SubmissionStatus.Pending;

    public SubmissionStatus Status
    {
        get
        {
            if (_status == SubmissionStatus.Pending)
            {
                Result result = owner.Api.GetFenceStatus(owner.NativeDevice, fence);
                if (result == Result.Success || result == Result.ErrorDeviceLost)
                {
                    _status = result == Result.Success ? SubmissionStatus.Completed : SubmissionStatus.Failed;
                    foreach (VulkanCommandBuffer command in commands)
                    {
                        command.Complete(result == Result.Success);
                    }
                }
                else if (result != Result.NotReady)
                {
                    throw new InvalidOperationException($"Vulkan fence query failed: {result}.");
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
            throw new InvalidOperationException("Vulkan submission failed: device lost.");
        }
    }

    public unsafe void Dispose()
    {
        if (_status == SubmissionStatus.Disposed)
        {
            return;
        }

        if (Status == SubmissionStatus.Pending)
        {
            throw new InvalidOperationException("Complete the submission before disposal.");
        }

        owner.Api.DestroyFence(owner.NativeDevice, fence, null);
        _status = SubmissionStatus.Disposed;
        owner.ReleaseSubmission();
    }
}
