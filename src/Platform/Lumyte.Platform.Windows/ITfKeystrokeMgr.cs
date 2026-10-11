using System.Runtime.InteropServices;

namespace Lumyte.Platform.Windows;

/// <summary>Windows SDK ITfKeystrokeMgr ABI declaration.</summary>
[ComImport]
[Guid("AA80E7F0-2021-11D2-93E0-0060B067B86E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfKeystrokeMgr
{
    /// <summary>Calls the native AdviseKeyEventSink operation.</summary>
    /// <param name="client">The client argument.</param>
    /// <param name="sink">The sink argument.</param>
    /// <param name="foreground">The foreground argument.</param>
    void AdviseKeyEventSink(uint client, nint sink, [MarshalAs(UnmanagedType.Bool)] bool foreground);

    /// <summary>Calls the native UnadviseKeyEventSink operation.</summary>
    /// <param name="client">The client argument.</param>
    void UnadviseKeyEventSink(uint client);

    /// <summary>Calls the native GetForeground operation.</summary>
    /// <param name="classId">The classId argument.</param>
    void GetForeground(out Guid classId);

    /// <summary>Calls the native TestKeyDown operation.</summary>
    /// <param name="wParam">The wParam argument.</param>
    /// <param name="lParam">The lParam argument.</param>
    /// <param name="eaten">The eaten argument.</param>
    void TestKeyDown(nuint wParam, nint lParam, [MarshalAs(UnmanagedType.Bool)] out bool eaten);

    /// <summary>Calls the native TestKeyUp operation.</summary>
    /// <param name="wParam">The wParam argument.</param>
    /// <param name="lParam">The lParam argument.</param>
    /// <param name="eaten">The eaten argument.</param>
    void TestKeyUp(nuint wParam, nint lParam, [MarshalAs(UnmanagedType.Bool)] out bool eaten);

    /// <summary>Calls the native KeyDown operation.</summary>
    /// <param name="wParam">The wParam argument.</param>
    /// <param name="lParam">The lParam argument.</param>
    /// <param name="eaten">The eaten argument.</param>
    void KeyDown(nuint wParam, nint lParam, [MarshalAs(UnmanagedType.Bool)] out bool eaten);

    /// <summary>Calls the native KeyUp operation.</summary>
    /// <param name="wParam">The wParam argument.</param>
    /// <param name="lParam">The lParam argument.</param>
    /// <param name="eaten">The eaten argument.</param>
    void KeyUp(nuint wParam, nint lParam, [MarshalAs(UnmanagedType.Bool)] out bool eaten);
}
