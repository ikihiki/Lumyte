using System.Runtime.Versioning;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Windows.Sample;

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
        using IWindow window = platform.CreateWindow(new WindowDescriptor { Title = "Lumyte single window" });
        window.Show();
        platform.PumpEvents();
        while (window.State.Lifetime == WindowLifetime.Open && !platform.IsQuitRequested)
        {
            platform.PumpEvents();
            Thread.Sleep(10);
        }

        if (window.State.Lifetime == WindowLifetime.Open)
        {
            window.Close();
        }

        platform.PumpEvents();
    }
}
