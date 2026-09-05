using Microsoft.Win32.SafeHandles;

namespace Lumyte.Graphics.Text;

internal sealed class HarfBuzzPaintFunctionsHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal HarfBuzzPaintFunctionsHandle(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        HarfBuzzNative.DestroyPaintFunctions(handle);
        return true;
    }
}
