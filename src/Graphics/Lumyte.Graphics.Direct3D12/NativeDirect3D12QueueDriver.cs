namespace Lumyte.Graphics.Direct3D12;

internal sealed unsafe class NativeDirect3D12QueueDriver : IDirect3D12QueueDriver
{
    internal static NativeDirect3D12QueueDriver Instance { get; } = new();

    /// <inheritdoc />
    public void CreateFence(Direct3D12Device owner, out nint fence) => owner.Check(NativeMethods.FenceCreate(owner.QueueNative, out fence));

    /// <inheritdoc />
    public void Wait(Direct3D12Device owner, nint fence, ulong value) => owner.Check(NativeMethods.QueueWaitValue(owner.Native, fence, value));

    /// <inheritdoc />
    public void Signal(Direct3D12Device owner, nint fence, ulong value) => owner.Check(NativeMethods.QueueSignalValue(owner.Native, fence, value));

    /// <inheritdoc />
    public void Execute(Direct3D12Device owner, ReadOnlySpan<nint> commands)
    {
        fixed (nint* data = commands)
        {
            owner.Check(NativeMethods.CommandsExecute(owner.Native, data, (uint)commands.Length));
        }
    }

    /// <inheritdoc />
    public void SignalCompletion(Direct3D12Device owner, nint fence) => owner.Check(NativeMethods.QueueSignal(owner.Native, fence));

    /// <inheritdoc />
    public ulong Completed(nint fence) => NativeMethods.FenceCompleted(fence);

    /// <inheritdoc />
    public int DeviceRemoved(Direct3D12Device owner) => NativeMethods.DeviceRemoved(owner.Native);

    /// <inheritdoc />
    public Task WaitForCompletion(Direct3D12Device owner, nint fence) => new Direct3D12FenceWait(owner, fence).Completion;

    /// <inheritdoc />
    public void Release(nint fence) => NativeMethods.Release(fence);
}
