using System.Runtime.InteropServices;
using Windows.Win32;

namespace Lumyte.Platform.Windows.TextInput.Interop;

internal sealed unsafe class ComPtr<T> : SafeHandle
    where T : unmanaged, IComIID
{
    internal ComPtr(nint pointer)
        : base(0, true)
    {
        SetHandle(pointer);
    }

    public override bool IsInvalid => handle == 0;

    internal T* Pointer
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsClosed, this);
            return (T*)handle;
        }
    }

    protected override bool ReleaseHandle()
    {
        ((IUnknown*)handle)->Release();
        return true;
    }
}
