using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    public NativeGpuSemaphore CreateSemaphore(ulong initialValue = 0)
    {
        VerifyAvailable();
        ComPtr<ID3D12Fence> fence = default;
        try
        {
            Check(device.CreateFence<ID3D12Fence>(initialValue, FenceFlags.None, out fence), "CreateFence");
            return new NativeSemaphore(this, fence);
        }
        catch { fence.Dispose(); throw; }
    }

    private NativeSemaphore RequireSemaphore(NativeGpuSemaphore semaphore, string parameter)
    {
        if (semaphore is not NativeSemaphore native || !ReferenceEquals(native.Owner, this))
        { throw new ArgumentException("The semaphore belongs to another device.", parameter); }
        ObjectDisposedException.ThrowIf(native.Disposed, semaphore);
        return native;
    }

    // Testable native-result boundary shared by CPU timeline operations. An actual
    // removed/hung/reset result is already device-wide; ordinary allocation errors are not.
    internal void CheckDeviceResult(int result, string operation)
    {
        if (result is unchecked((int)0x887A0005) or unchecked((int)0x887A0006)
            or unchecked((int)0x887A0007))
        { deviceLoss = operation; }
        Check(result, operation);
    }

    private void LoseDevice(string message, int? result = null)
    {
        deviceLoss = message;
        // Work and GPU waits already accepted by any queue cannot be rolled back.
        // Remove the device before allocating diagnostic text.
        device10.RemoveDevice();
        throw new GpuDeviceLostException(result is int error ? $"{message} HRESULT 0x{error:X8}." : message);
    }

    private sealed partial class NativeSemaphore(DirectX12Backend owner, ComPtr<ID3D12Fence> fence) : NativeGpuSemaphore
    {
        public DirectX12Backend Owner { get; } = owner;
        public ComPtr<ID3D12Fence> Fence = fence;
        public bool Disposed { get; private set; }

        private void VerifyAvailable()
        {
            Owner.VerifyAvailable();
            ObjectDisposedException.ThrowIf(Disposed, this);
        }

        public override bool IsComplete(ulong value)
        {
            VerifyAvailable();
            ulong completed = Fence.GetCompletedValue();
            if (completed == ulong.MaxValue) { Owner.LoseDevice("Direct3D 12 reported a removed device."); }
            return completed >= value;
        }

        public override void WaitCpu(ulong value)
        {
            VerifyAvailable();
            // The native runtime blocks this caller only. Queue retirement remains on
            // queue operations, so another thread may submit while this wait is pending.
            Owner.CheckDeviceResult(Fence.SetEventOnCompletion(value, (void*)null), "SetEventOnCompletion");
            if (Fence.GetCompletedValue() == ulong.MaxValue)
            { Owner.LoseDevice("Direct3D 12 reported a removed device while waiting."); }
        }

        public override void SignalCpu(ulong value)
        {
            VerifyAvailable();
            Owner.CheckDeviceResult(Fence.Signal(value), "ID3D12Fence.Signal");
        }

        public override void Dispose()
        {
            lock (asyncWaitGate)
            {
                if (Disposed) { return; }
                if (activeAsyncWaits != 0)
                { throw new InvalidOperationException("The semaphore still has an active asynchronous CPU wait."); }
                Disposed = true;
                Fence.Dispose();
            }
        }
    }
}
