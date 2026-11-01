using Windows.Win32;

namespace Lumyte.Platform.Windows.TextInput.Interop;

internal static unsafe class TsfNative
{
    internal static int CoInitializeEx(nint reserved, uint flags) => PInvoke.CoInitializeEx((void*)reserved, (COINIT)flags);

    internal static void CoUninitialize() => PInvoke.CoUninitialize();

    internal static int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint result)
    {
        Guid type = clsid;
        Guid contract = iid;
        void* instance = null;
        int status = PInvoke.CoCreateInstance(&type, (IUnknown*)outer, (CLSCTX)context, &contract, &instance);
        result = (nint)instance;
        return status;
    }

    internal static short GetKeyState(int key) => PInvoke.GetKeyState(key);

    internal static int ClientToScreen(nint window, ref TsfPoint point)
    {
        fixed (TsfPoint* at = &point)
        {
            return PInvoke.ClientToScreen(new HWND(window), at);
        }
    }
}
