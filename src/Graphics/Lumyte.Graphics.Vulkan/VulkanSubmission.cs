using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed class VulkanSubmission : IGraphicsSubmission
{
    private readonly VulkanDevice _owner;
    private readonly VulkanCommandBuffer[] _commands;
    private readonly Fence _fence;
    private readonly object _gate = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SubmissionStatus _status = SubmissionStatus.Pending;
    private bool _nativeWait = true;
    private bool _failed;
    private Result? _nativeResult;

    internal VulkanSubmission(VulkanDevice owner, VulkanCommandBuffer[] commands, Fence fence)
    {
        (_owner, _commands, _fence) = (owner, commands, fence);

        // Register at Submit time, in native queue order, never in caller wait
        // order: a later blocked fence must not hold up an earlier submission.
        owner.EnqueueCompletion(() => WaitNative(_completion), IsNativeReady);
    }

    public SubmissionStatus Status
    {
        get
        {
            lock (_gate)
            {
                if (_status == SubmissionStatus.Pending)
                {
                    Result result = _owner.Status == GraphicsDeviceStatus.Lost ? Result.ErrorDeviceLost : _owner.Api.GetFenceStatus(_owner.NativeDevice, _fence);
                    if (result != Result.NotReady)
                    {
                        Complete(result);
                    }
                }

                return _status;
            }
        }
    }

    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_status == SubmissionStatus.Disposed, this);
        }

        await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_status != SubmissionStatus.Disposed)
            {
                Complete(_owner.Status == GraphicsDeviceStatus.Lost ? Result.ErrorDeviceLost : _nativeResult!.Value);
            }

            if (_failed || _nativeResult == Result.ErrorDeviceLost)
            {
                throw new GraphicsDeviceLostException(_owner.Loss!);
            }
        }
    }

    public unsafe void Dispose()
    {
        lock (_gate)
        {
            if (_status == SubmissionStatus.Disposed)
            {
                return;
            }

            if (Status == SubmissionStatus.Pending)
            {
                throw new InvalidOperationException("Complete the submission before disposal.");
            }

            // A concurrent Status query may observe the signaled fence just before
            // the native wait returns. Keep its handle alive until that return.
            if (!_nativeWait)
            {
                _owner.Api.DestroyFence(_owner.NativeDevice, _fence, null);
            }

            _status = SubmissionStatus.Disposed;
        }
    }

    private bool IsNativeReady()
    {
        lock (_gate)
        {
            if (_status == SubmissionStatus.Disposed && !_nativeWait)
            {
                return true;
            }

            Result result = _owner.Api.GetFenceStatus(_owner.NativeDevice, _fence);
            return result is Result.Success or Result.ErrorDeviceLost;
        }
    }

    private unsafe Action WaitNative(TaskCompletionSource completion)
    {
        Result result;
        try
        {
            Fence nativeFence = _fence;
            result = _owner.Api.WaitForFences(_owner.NativeDevice, 1, &nativeFence, true, ulong.MaxValue);
        }
        catch (Exception error)
        {
            return () => Publish(completion, null, error);
        }

        return () => Publish(completion, result, null);
    }

    private unsafe void Publish(TaskCompletionSource completion, Result? result, Exception? error)
    {
        try
        {
            lock (_gate)
            {
                _nativeWait = false;
                _nativeResult = result;
                if (result == Result.ErrorDeviceLost)
                {
                    _owner.ReportCompletionLoss("Vulkan fence wait failed: device lost.");
                }

                if (_status == SubmissionStatus.Disposed)
                {
                    _owner.Api.DestroyFence(_owner.NativeDevice, _fence, null);
                }

                if (error is not null)
                {
                    completion.TrySetException(error);
                }
                else
                {
                    completion.TrySetResult();
                }
            }
        }
        catch (Exception failure)
        {
            completion.TrySetException(failure);
        }
    }

    private void Complete(Result result)
    {
        if (result == Result.ErrorDeviceLost)
        {
            _owner.ReportCompletionLoss("Vulkan fence wait/query failed: device lost.");
        }
        else if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan fence wait/query failed: {result}.");
        }

        if (_status != SubmissionStatus.Pending)
        {
            return;
        }

        _failed = result == Result.ErrorDeviceLost;
        _status = _failed ? SubmissionStatus.Failed : SubmissionStatus.Completed;
        foreach (VulkanCommandBuffer command in _commands)
        {
            command.Complete(result == Result.Success);
        }
    }
}
