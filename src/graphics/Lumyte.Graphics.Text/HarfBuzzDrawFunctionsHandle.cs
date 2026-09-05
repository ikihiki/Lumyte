using Microsoft.Win32.SafeHandles;

namespace Lumyte.Graphics.Text;

internal sealed class HarfBuzzDrawFunctionsHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal HarfBuzzDrawFunctionsHandle(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        HarfBuzzNative.DestroyDrawFunctions(handle);
        return true;
    }
}
