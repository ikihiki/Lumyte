using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Lumyte.Platform.Windows;

public sealed class WindowsDisplay : IDisplay
{
    private WindowsDisplay(
        nint handle,
        string name,
        Rectangle bounds,
        Rectangle workArea,
        float scaleFactor,
        bool isPrimary)
    {
        Handle = handle;
        Name = name;
        Bounds = bounds;
        WorkArea = workArea;
        ScaleFactor = scaleFactor;
        IsPrimary = isPrimary;
    }

    public nint Handle { get; }

    public string Name { get; }

    public Rectangle Bounds { get; }

    public Rectangle WorkArea { get; }

    public float ScaleFactor { get; }

    public bool IsPrimary { get; }

    internal static unsafe IReadOnlyList<WindowsDisplay> Enumerate()
    {
        List<WindowsDisplay> displays = [];
        MONITORENUMPROC callback = (monitor, _, _, _) =>
        {
            displays.Add(Create(monitor));
            return true;
        };

        if (!PInvoke.EnumDisplayMonitors(default, null, callback, default))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return displays;
    }

    private static unsafe WindowsDisplay Create(HMONITOR monitor)
    {
        MONITORINFOEXW information = new()
        {
            monitorInfo =
            {
                cbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>(),
            },
        };

        if (!PInvoke.GetMonitorInfo(monitor, ref information.monitorInfo))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        HRESULT dpiResult = PInvoke.GetDpiForMonitor(
            monitor,
            MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI,
            out uint dpiX,
            out _);
        if (dpiResult.Value < 0)
        {
            dpiX = 96;
        }

        MONITORINFO monitorInfo = information.monitorInfo;
        return new(
            (nint)monitor.Value,
            information.szDevice.ToString(),
            ToRectangle(monitorInfo.rcMonitor),
            ToRectangle(monitorInfo.rcWork),
            dpiX / 96f,
            (monitorInfo.dwFlags & 1) != 0);
    }

    private static Rectangle ToRectangle(RECT rectangle) => Rectangle.FromLTRB(
        rectangle.left,
        rectangle.top,
        rectangle.right,
        rectangle.bottom);
}
