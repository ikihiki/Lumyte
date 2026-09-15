using System.Runtime.InteropServices;

namespace Lumyte.Graphics.RenderGraph.Conformance;

/// <summary>Real Win32 test window with its owning thread continuously pumping while asynchronous GPU work runs.</summary>
public sealed partial class WindowConformance
{
    private const uint Style = 0x00CF0000;
    public nint Handle { get; private set; }
    private WindowConformance()
    {
        Handle = CreateWindowExW(0, "STATIC", "Lumyte presentation conformance", Style | 0x10000000,
            unchecked((int)0x80000000), unchecked((int)0x80000000), 240, 180, 0, 0, 0, 0);
        if (Handle == 0)
        { throw new InvalidOperationException("Test window creation failed."); }
    }
    public (uint Width, uint Height) Size()
    {
        if (!GetClientRect(Handle, out var r))
        { throw new InvalidOperationException("GetClientRect failed."); }
        return ((uint)(r.Right - r.Left), (uint)(r.Bottom - r.Top));
    }
    public void Resize(int width, int height)
    {
        Rect r = new() { Right = width, Bottom = height };
        if (!AdjustWindowRectEx(ref r, Style, false, 0) || !SetWindowPos(Handle, 0, 0, 0, r.Right - r.Left, r.Bottom - r.Top, 0x16))
        { throw new InvalidOperationException("Window resize failed."); }
        Pump();
    }
    public static Task RunAsync(Action<WindowConformance> test)
    {
        if (!OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException("Window conformance requires Windows."); }
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            WindowConformance? window = null;
            Exception? failure = null;
            try
            { window = new(); test(window); }
            catch (Exception error) { failure = error; }
            finally { if (window is not null) { DestroyWindow(window.Handle); window.Handle = 0; } }
            if (failure is null)
            { result.TrySetResult(); }
            else
            { result.TrySetException(failure); }
        })
        { IsBackground = true, Name = "Lumyte presentation test window" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }
    public void Complete(Task task)
    {
        while (!task.IsCompleted)
        { Pump(); _ = MsgWaitForMultipleObjectsEx(0, 0, 10, 0x04FF, 0x04); }
        Pump();
        task.GetAwaiter().GetResult();
    }
    public T Complete<T>(Task<T> task) { Complete((Task)task); return task.GetAwaiter().GetResult(); }
    private static void Pump()
    {
        while (PeekMessageW(out var message, 0, 0, 0, 1))
        { _ = TranslateMessage(in message); _ = DispatchMessageW(in message); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Hwnd; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint CreateWindowExW(uint ex, string name, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetClientRect(nint hwnd, out Rect rectangle);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool AdjustWindowRectEx(ref Rect rectangle, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint ex);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool PeekMessageW(out Message message, nint hwnd, uint min, uint max, uint remove);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool TranslateMessage(in Message message);
    [LibraryImport("user32.dll")] private static partial nint DispatchMessageW(in Message message);
    [LibraryImport("user32.dll")] private static partial uint MsgWaitForMultipleObjectsEx(uint count, nint handles, uint milliseconds, uint mask, uint flags);
}
