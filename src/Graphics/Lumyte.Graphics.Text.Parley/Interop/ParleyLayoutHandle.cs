using Microsoft.Win32.SafeHandles;

namespace Lumyte.Graphics.Text.Parley.Interop;

internal sealed unsafe class ParleyLayoutHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public ParleyLayoutHandle(nint pointer)
        : base(ownsHandle: true)
    {
        SetHandle(pointer);
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.lp_layout_free((LpLayout*)handle);
        return true;
    }
}
