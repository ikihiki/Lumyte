using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Wgpu;
using Lumyte.Input;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Abstractions;
using Lumyte.Platform.Linux;
using Lumyte.Platform.Windows;
using Lumyte.UI.SampleSupport;
using Lumyte.Windowing.Abstractions;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Sample;

internal static class NativeHost
{
    internal static int Run()
    {
        int result = 1;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RunCore();
                result = 0;
            }
            catch (Exception error)
            {
                failure = error;
            }
        });
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            thread.SetApartmentState(ApartmentState.STA);
        }

        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            Console.Error.WriteLine(failure);
        }

        return result;
    }

    private static void RunCore()
    {
        using IPlatform platform = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) ? new WindowsPlatform() : OperatingSystem.IsLinux() ? new LinuxPlatform() : throw new PlatformNotSupportedException();
        using IWindow window = platform.CreateWindow(new() { Title = "Lumyte UI — click or Tab / Space", ClientSize = new(480, 320) });
        using IInputSource source = platform.CreateInputSource();
        var map = (IWindowInputSource)source;
        using var input = new InputSystem([source]);
        int clicks = 0;
        using var ui = new UiSurface(Panel()[Stack(spacing: 16)[Rectangle(width: 300, height: 40, fill: new Color2D(0.2f, 0.5f, 0.9f, 1)), Button(onClick: () => window.SetTitle($"Clicks: {++clicks}"))[Rectangle(width: 200, height: 100, fill: new Color2D(0.7f, 0.3f, 0.1f, 1))]]]);
        var router = new UiHostInputRouter(map, map);
        router.Attach(window, ui);
        if (!map.TryGetInputWindowId(window.Id, out InputWindowId origin))
        {
            throw new InvalidOperationException("The native window has no acquisition identity.");
        }

        var pending = new Queue<InputRecord>();
        input.Recorded += pending.Enqueue;
        window.Show();
        platform.PumpEvents();
        using IDisposable retained = window.RetainNativeTarget();
        using var device = WgpuDevice.Create();
        Ahjo.Wgpu.SurfaceSource native = window switch
        {
            WindowsWindow target when OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) => Ahjo.Wgpu.SurfaceSource.WindowsHwnd(target.HInstance, target.Hwnd),
            LinuxWindow target when OperatingSystem.IsLinux() => Ahjo.Wgpu.SurfaceSource.XlibWindow(target.XDisplay, target.XWindow),
            _ => throw new PlatformNotSupportedException(),
        };
        using IGraphicsSurface graphics = device.CreateSurface(native);
        using var presenter = new UiHostPresenter(device);
        IGraphicsSwapchain? swapchain = null;
        try
        {
            while (window.State.Lifetime == WindowLifetime.Open)
            {
                platform.PumpEvents();
                if (!UiHostDrawing.Drawable(window.State))
                {
                    router.Reset(origin);

                    input.Update();
                    pending.Clear();
                    Thread.Sleep(10);
                    continue;
                }

                ui.SetViewport(window.State.ClientSize);
                ui.UpdateLayout();
                input.Update();

                // Recorded runs inside InputSystem.Update. Deliver only after that transaction returns.
                while (pending.TryDequeue(out InputRecord record))
                {
                    router.Dispatch(record);
                }

                var canvas = new Canvas2D();
                canvas.Begin();
                UiHostDrawing.Record(canvas, window, ui);
                DrawList2D list = canvas.End();
                SwapchainDesc configuration = UiHostPresenter.Configuration(graphics, window.State.PixelSize.Width, window.State.PixelSize.Height);
                if (swapchain is null)
                {
                    swapchain = graphics.CreateSwapchain(configuration);
                }
                else if (swapchain.Configuration != configuration)
                {
                    swapchain.Reconfigure(configuration);
                }

                presenter.DrawAsync(swapchain, list).GetAwaiter().GetResult();
            }
        }
        catch
        {
            input.StopSource(map);
            throw;
        }
        finally
        {
            router.Detach(ui);
            swapchain?.Dispose();
        }

        graphics.Dispose();
        retained.Dispose();
        platform.PumpEvents();
    }
}
