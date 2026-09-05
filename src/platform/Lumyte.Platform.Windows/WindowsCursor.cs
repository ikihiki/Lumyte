using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Lumyte.Platform.Windows;

internal static partial class WindowsCursor
{
    private const uint RidInput = 0x10000003;
    private const uint RidevRemove = 0x00000001;

    internal static void SetVisible(bool visible)
    {
        while ((ShowCursor(visible) >= 0) != visible)
        { }
    }

    internal static void Release()
    {
        SetVisible(true);
        SetConfinement(0);
        EnableRawInput(0, false);
    }

    internal static void SetConfinement(nint window)
    {
        if (window == 0)
        { ClipCursor(0); return; }
        if (!GetClientRect(window, out Rect rectangle))
        {
            throw new Win32Exception();
        }

        var topLeft = new Point(rectangle.Left, rectangle.Top);
        var bottomRight = new Point(rectangle.Right, rectangle.Bottom);
        if (!ClientToScreen(window, ref topLeft) || !ClientToScreen(window, ref bottomRight))
        {
            throw new Win32Exception();
        }

        rectangle = new(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        if (!ClipCursor(ref rectangle))
        {
            throw new Win32Exception();
        }
    }

    internal static void EnableRawInput(nint window, bool enabled)
    {
        RawInputDevice device = new() { UsagePage = 1, Usage = 2, Flags = enabled ? 0u : RidevRemove, Target = enabled ? window : 0 };
        if (!RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            throw new Win32Exception();
        }
    }

    internal static bool TryReadDelta(nint rawInput, out int x, out int y)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        if (GetRawInputData(rawInput, RidInput, 0, ref size, headerSize) == uint.MaxValue || size == 0)
        { x = y = 0; return false; }
        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(rawInput, RidInput, buffer, ref size, headerSize) == uint.MaxValue)
            { x = y = 0; return false; }
            RawInput input = Marshal.PtrToStructure<RawInput>(buffer);
            x = input.Mouse.LastX;
            y = input.Mouse.LastY;
            return input.Header.Type == 0;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point(int x, int y) { public int X = x; public int Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect(int left, int top, int right, int bottom) { public int Left = left; public int Top = top; public int Right = right; public int Bottom = bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct RawInputDevice { public ushort UsagePage; public ushort Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] private struct RawInputHeader { public uint Type; public uint Size; public nint Device; public nint WParam; }
    [StructLayout(LayoutKind.Sequential)] private struct RawMouse { public ushort Flags; public uint Buttons; public uint RawButtons; public int LastX; public int LastY; public uint ExtraInformation; }
    [StructLayout(LayoutKind.Sequential)] private struct RawInput { public RawInputHeader Header; public RawMouse Mouse; }

    [LibraryImport("user32.dll")] private static partial int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool show);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool ClipCursor(nint rectangle);
    [LibraryImport("user32.dll", EntryPoint = "ClipCursor")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool ClipCursor(ref Rect rectangle);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetClientRect(nint window, out Rect rectangle);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool ClientToScreen(nint window, ref Point point);
    [LibraryImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static partial bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint deviceCount, uint size);
    [LibraryImport("user32.dll", SetLastError = true)] private static partial uint GetRawInputData(nint rawInput, uint command, nint data, ref uint size, uint headerSize);
}
