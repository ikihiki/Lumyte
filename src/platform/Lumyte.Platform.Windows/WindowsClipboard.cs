using System.ComponentModel;
using System.Runtime.InteropServices;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Lumyte.Platform.Windows;

public sealed class WindowsClipboard : IClipboard
{
    private readonly Func<HWND> getOwner;

    internal WindowsClipboard(Func<HWND> getOwner)
    {
        this.getOwner = getOwner;
    }

    public unsafe string? GetText()
    {
        if (!PInvoke.IsClipboardFormatAvailable((uint)CLIPBOARD_FORMAT.CF_UNICODETEXT))
        {
            return null;
        }

        Open();
        try
        {
            HANDLE clipboardData = PInvoke.GetClipboardData((uint)CLIPBOARD_FORMAT.CF_UNICODETEXT);
            if (clipboardData.IsNull)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            HGLOBAL memory = new(clipboardData.Value);
            void* address = PInvoke.GlobalLock(memory);
            if (address is null)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            try
            {
                return Marshal.PtrToStringUni((nint)address);
            }
            finally
            {
                _ = PInvoke.GlobalUnlock(memory);
            }
        }
        finally
        {
            _ = PInvoke.CloseClipboard();
        }
    }

    public unsafe void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        nuint byteCount = checked((nuint)((text.Length + 1) * sizeof(char)));
        HGLOBAL memory = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, byteCount);
        if (memory.IsNull)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        bool transferred = false;
        try
        {
            void* address = PInvoke.GlobalLock(memory);
            if (address is null)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            try
            {
                fixed (char* source = text)
                {
                    Buffer.MemoryCopy(
                        source,
                        address,
                        checked((long)byteCount),
                        checked(text.Length * sizeof(char)));
                }

                ((char*)address)[text.Length] = '\0';
            }
            finally
            {
                _ = PInvoke.GlobalUnlock(memory);
            }

            Open();
            try
            {
                if (!PInvoke.EmptyClipboard())
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                HANDLE clipboardHandle = new(memory.Value);
                HANDLE result = PInvoke.SetClipboardData(
                    (uint)CLIPBOARD_FORMAT.CF_UNICODETEXT,
                    clipboardHandle);
                if (result.IsNull)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                transferred = true;
            }
            finally
            {
                _ = PInvoke.CloseClipboard();
            }
        }
        finally
        {
            if (!transferred)
            {
                _ = PInvoke.GlobalFree(memory);
            }
        }
    }

    private void Open()
    {
        if (!PInvoke.OpenClipboard(getOwner()))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }
}
