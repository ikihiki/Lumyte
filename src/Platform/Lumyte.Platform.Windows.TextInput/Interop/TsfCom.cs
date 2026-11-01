using System.Runtime.InteropServices;
using Windows.Win32;

namespace Lumyte.Platform.Windows.TextInput.Interop;

internal static unsafe class TsfCom
{
    private static readonly TsfComWrappers _wrappers = new();

    internal static Guid GuidOf<T>()
        where T : unmanaged, IComIID => T.Guid;

    internal static ComPtr<T> Own<T>(nint pointer)
        where T : unmanaged, IComIID
    {
        if (pointer == 0)
        {
            throw new COMException("The TSF interface pointer is null.");
        }

        return new(pointer);
    }

    internal static ComPtr<T> Query<T>(nint pointer)
        where T : unmanaged, IComIID => Own<T>(QueryPointer<T>(pointer));

    internal static nint Borrow<T>(ComPtr<T> value)
        where T : unmanaged, IComIID
    {
        ((IUnknown*)value.Pointer)->AddRef();
        return (nint)value.Pointer;
    }

    internal static nint Borrow<T>(object value)
        where T : unmanaged, IComIID
    {
        nint unknown = _wrappers.GetOrCreateComInterfaceForObject(value, CreateComInterfaceFlags.None);
        try
        {
            return QueryPointer<T>(unknown);
        }
        finally
        {
            Return(unknown);
        }
    }

    internal static bool SameIdentity(nint first, nint second)
    {
        nint a = QueryPointer<IUnknown>(first);
        try
        {
            nint b = QueryPointer<IUnknown>(second);
            try
            {
                return a == b;
            }
            finally
            {
                Return(b);
            }
        }
        finally
        {
            Return(a);
        }
    }

    internal static void Return(nint pointer)
    {
        if (pointer != 0)
        {
            ((IUnknown*)pointer)->Release();
        }
    }

    internal static void Release(IDisposable? value) => value?.Dispose();

    private static nint QueryPointer<T>(nint pointer)
        where T : unmanaged, IComIID
    {
        Guid iid = T.Guid;
        void* result;
        Marshal.ThrowExceptionForHR(((IUnknown*)pointer)->QueryInterface(&iid, &result));
        return (nint)result;
    }
}
