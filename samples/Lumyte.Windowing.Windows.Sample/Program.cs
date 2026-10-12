using System.Numerics;
using System.Runtime.Versioning;
using Lumyte.Platform.Windows;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Windowing.Windows.Sample;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            Console.WriteLine("This sample requires Windows 10 version 1809 or later.");
            return;
        }

        Run();
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private static void Run()
    {
        using var platform = new WindowsPlatform();
        using var windows = new WindowSystem(platform);

        WindowNode mainWindow = windows.CreateNativeWindow(
            new WindowDescriptor { Title = "Lumyte Windowing", ClientSize = new Vector2(800, 600) },
            WindowRole.Normal);
        WindowNode toolWindow = windows.CreateNativeWindow(
            new WindowDescriptor
            {
                Title = "Tool window",
                Kind = WindowKind.Tool,
                Owner = mainWindow.NativeWindow,
                ClientSize = new Vector2(320, 240),
            },
            WindowRole.Tool,
            mainWindow);
        WindowNode contextMenu = windows.CreateHostedWindow(new HostedWindowDescriptor
        {
            Parent = mainWindow,
            Bounds = new WindowRect(24, 24, 240, 160),
            Role = WindowRole.ContextMenu,
            Activation = WindowActivation.PreserveFocus,
        });

        windows.Changed += node =>
            Console.WriteLine($"{node.Kind} {node.Role}: visible={node.IsEffectivelyVisible}, bounds={node.Bounds}");

        mainWindow.Show();
        toolWindow.Show();
        contextMenu.Show();

        while (windows.Windows.Count > 0 && !platform.IsQuitRequested)
        {
            platform.PumpEvents();
            windows.Update();
            Thread.Sleep(10);
        }

        if (windows.Windows.Contains(mainWindow))
        {
            mainWindow.Close();
            while (windows.Windows.Count > 0)
            {
                platform.PumpEvents();
                windows.Update();
            }
        }
    }
}
