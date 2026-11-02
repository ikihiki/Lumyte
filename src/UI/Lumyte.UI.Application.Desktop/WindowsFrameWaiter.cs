using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Lumyte.UI.Application.Desktop;

/// <summary>Waits for either UI work or a precise frame deadline without spinning.</summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed class WindowsFrameWaiter : IDisposable
{
    private readonly TimerHandle _timer = new();
    private readonly WaitHandle[] _handles = new WaitHandle[2];
    private readonly Action<WaitHandle, TimeSpan> _wait;

    internal WindowsFrameWaiter()
    {
        _handles[1] = _timer;
        _wait = WaitCore;
    }

    internal Action<WaitHandle, TimeSpan> Wait => _wait;

    public void Dispose() => _timer.Dispose();

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(nint attributes, nint name, uint flags, uint access);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimerEx(SafeWaitHandle timer, ref long dueTime, int period, nint callback, nint argument, nint reason, uint tolerableDelay);

    private void WaitCore(WaitHandle wakeup, TimeSpan timeout)
    {
        long due = -Math.Max(1, timeout.Ticks);
        if (_timer.SafeWaitHandle.IsInvalid || !SetWaitableTimerEx(_timer.SafeWaitHandle, ref due, 0, 0, 0, 0, 0))
        {
            wakeup.WaitOne(TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(timeout.TotalMilliseconds))));
            return;
        }

        _handles[0] = wakeup;
        WaitHandle.WaitAny(_handles);
    }

    private sealed class TimerHandle : WaitHandle
    {
        internal TimerHandle()
        {
            // CREATE_WAITABLE_TIMER_HIGH_RESOLUTION; SYNCHRONIZE | TIMER_MODIFY_STATE.
            SafeWaitHandle = CreateWaitableTimerExW(0, 0, 2, 0x00100002);
        }
    }
}
