using Microsoft.Win32.SafeHandles;

namespace Lumyte.Graphics.Text.Parley.Interop;

internal sealed unsafe class ParleyEngineHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public ParleyEngineHandle(nint pointer)
        : base(ownsHandle: true)
    {
        SetHandle(pointer);
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.lp_engine_free((LpEngine*)handle);
        return true;
    }
}
