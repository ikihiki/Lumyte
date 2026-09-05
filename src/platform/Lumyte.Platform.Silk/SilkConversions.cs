using Lumyte.Platform;

namespace Lumyte.Platform.SilkNet;

internal static class SilkConversions
{
    public static Silk.NET.Windowing.WindowState ToSilk(WindowState state) => state switch
    {
        WindowState.Normal => Silk.NET.Windowing.WindowState.Normal,
        WindowState.Minimized => Silk.NET.Windowing.WindowState.Minimized,
        WindowState.Maximized => Silk.NET.Windowing.WindowState.Maximized,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static WindowState FromSilk(Silk.NET.Windowing.WindowState state) => state switch
    {
        Silk.NET.Windowing.WindowState.Minimized => WindowState.Minimized,
        Silk.NET.Windowing.WindowState.Maximized => WindowState.Maximized,
        _ => WindowState.Normal,
    };
}
