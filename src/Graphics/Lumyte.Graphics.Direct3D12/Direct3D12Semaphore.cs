using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12Semaphore : IGraphicsSemaphore
{
    private nint _native;

    internal Direct3D12Semaphore(Direct3D12Device owner)
    {
        Owner = owner;
        owner.QueueDriver.CreateFence(owner, out _native);
    }

    internal Direct3D12Device Owner { get; }

    internal ulong SignalValue { get; private set; } = 1;

    internal ulong WaitValue { get; private set; } = 1;

    internal nint Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_native == 0, this);
            return _native;
        }
    }

    public void Dispose()
    {
        Owner.Validation?.Release(this);
        Owner.QueueDriver.Release(_native);
        _native = 0;
    }

    internal void Signal()
    {
        ValidateSignal();
        Owner.QueueDriver.Signal(Owner, Native, SignalValue);
        SignalValue++;
    }

    internal void Wait()
    {
        ValidateWait();
        Owner.QueueDriver.Wait(Owner, Native, WaitValue);
        WaitValue++;
    }

    internal void ValidateSignal()
    {
        _ = Native;
        if (SignalValue != WaitValue || SignalValue == ulong.MaxValue)
        {
            throw new InvalidOperationException("A binary signal must follow consumption of the preceding signal.");
        }
    }

    internal void ValidateWait()
    {
        _ = Native;
        if (WaitValue >= SignalValue)
        {
            throw new InvalidOperationException("A binary wait requires a previously issued, unconsumed signal.");
        }
    }
}
