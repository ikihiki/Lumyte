using System.Runtime.InteropServices;

namespace Lumyte.Platform.Linux;

internal static class Xlib
{
    internal delegate int ErrorHandler(nint display, ref ProtocolError error);

    [DllImport("libX11.so.6", EntryPoint = "XSetErrorHandler")]
    internal static extern nint SetErrorHandler(ErrorHandler handler);

    [DllImport("libX11.so.6", EntryPoint = "XSetErrorHandler")]
    internal static extern nint RestoreErrorHandler(nint handler);

    [DllImport("libX11.so.6")]
    internal static extern int XSync(nint display, int discard);

    [DllImport("libX11.so.6")]
    internal static extern nint XOpenDisplay(nint name);

    [DllImport("libX11.so.6")]
    internal static extern int XkbSetDetectableAutoRepeat(nint display, int detectable, out int supported);

    [DllImport("libX11.so.6")]
    internal static extern int XCloseDisplay(nint display);

    [DllImport("libX11.so.6")]
    internal static extern nuint XDefaultRootWindow(nint display);

    [DllImport("libX11.so.6")]
    internal static extern nuint XCreateSimpleWindow(nint display, nuint parent, int x, int y, uint width, uint height, uint border, nuint borderPixel, nuint backgroundPixel);

    [DllImport("libX11.so.6")]
    internal static extern int XSelectInput(nint display, nuint window, nint mask);

    [DllImport("libX11.so.6")]
    internal static extern int XChangeWindowAttributes(nint display, nuint window, nuint mask, ref ChangeAttributes attributes);

    [DllImport("libX11.so.6")]
    internal static extern int XDestroyWindow(nint display, nuint window);

    [DllImport("libX11.so.6")]
    internal static extern int XMapWindow(nint display, nuint window);

    [DllImport("libX11.so.6")]
    internal static extern int XUnmapWindow(nint display, nuint window);

    [DllImport("libX11.so.6")]
    internal static extern int XRaiseWindow(nint display, nuint window);

    [DllImport("libX11.so.6")]
    internal static extern int XResizeWindow(nint display, nuint window, uint width, uint height);

    [DllImport("libX11.so.6")]
    internal static extern int XPending(nint display);

    [DllImport("libX11.so.6")]
    internal static extern int XNextEvent(nint display, out Event nativeEvent);

    [DllImport("libX11.so.6")]
    internal static extern int XFlush(nint display);

    [DllImport("libX11.so.6")]
    internal static extern int XGetWindowAttributes(nint display, nuint window, out Attributes attributes);

    [DllImport("libX11.so.6")]
    internal static extern int XSetTransientForHint(nint display, nuint window, nuint owner);

    [DllImport("libX11.so.6", CharSet = CharSet.Ansi)]
    internal static extern nuint XInternAtom(nint display, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int onlyExisting);

    [DllImport("libX11.so.6")]
    internal static extern int XSetWMProtocols(nint display, nuint window, ref nuint protocols, int count);

    [DllImport("libX11.so.6", CharSet = CharSet.Ansi)]
    internal static extern int XStoreName(nint display, nuint window, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

    [DllImport("libX11.so.6")]
    internal static extern int XChangeProperty(nint display, nuint window, nuint property, nuint type, int format, int mode, byte[] data, int count);

    [DllImport("libX11.so.6")]
    internal static extern int XGetInputFocus(nint display, out nuint focus, out int revert);

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProtocolError
    {
        internal int Type;
        internal nint Display;
        internal nuint Resource;
        internal nuint Serial;
        internal byte Code;
        internal byte Request;
        internal byte Minor;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ChangeAttributes
    {
        internal nuint BackgroundPixmap;
        internal nuint BackgroundPixel;
        internal nuint BorderPixmap;
        internal nuint BorderPixel;
        internal int BitGravity;
        internal int WindowGravity;
        internal int BackingStore;
        internal nuint BackingPlanes;
        internal nuint BackingPixel;
        internal int SaveUnder;
        internal nint EventMask;
        internal nint DoNotPropagateMask;
        internal int OverrideRedirect;
        internal nuint Colormap;
        internal nuint Cursor;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Attributes
    {
        internal int X;
        internal int Y;
        internal int Width;
        internal int Height;
        internal int BorderWidth;
        internal int Depth;
        internal nint Visual;
        internal nuint Root;
        internal int Class;
        internal int BitGravity;
        internal int WindowGravity;
        internal int BackingStore;
        internal nuint BackingPlanes;
        internal nuint BackingPixel;
        internal int SaveUnder;
        internal nuint Colormap;
        internal int MapInstalled;
        internal int MapState;
        internal nint AllEventMasks;
        internal nint YourEventMask;
        internal nint DoNotPropagateMask;
        internal int OverrideRedirect;
        internal nint Screen;
    }

    // XEvent is a union padded to 24 native longs. The shared header and client data
    // are enough for lifecycle notifications; its trailing storage must be retained.
    [StructLayout(LayoutKind.Sequential)]
    internal struct Event
    {
        internal int Type;
        internal nuint Serial;
        internal int SendEvent;
        internal nint Display;
        internal nuint Window;
        internal nint Data0;
        internal nint Data1;
        internal nint Data2;
        internal nint Data3;
        internal nint Data4;
        internal nint Data5;
        internal nint Data6;
        internal nint Data7;
        internal nint Data8;
        internal nint Data9;
        internal nint Data10;
        internal nint Data11;
        internal nint Data12;
        internal nint Data13;
        internal nint Data14;
        internal nint Data15;
        internal nint Data16;
        internal nint Data17;
        internal nint Data18;
    }
}
