using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Wgpu;
using Lumyte.Platform.Abstractions;
using Lumyte.Platform.Linux;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.Platform.Windows;
using Lumyte.Platform.Windows.TextInput;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.UI.Application.Desktop;

internal sealed class DesktopDriver : UiPlatformApplicationDriver
{
    internal override bool NativeWindows => true;

    internal override bool IsQuitRequested => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) && Platform is WindowsPlatform { IsQuitRequested: true };

    protected override ValueTask<(IPlatform Platform, ITextInputService? Text)> CreatePlatformAsync(bool textInput, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            var platform = new WindowsPlatform();
            try
            {
                return ValueTask.FromResult<(IPlatform, ITextInputService?)>((platform, null));
            }
            catch
            {
                platform.Dispose();
                throw;
            }
        }

        if (OperatingSystem.IsLinux())
        {
            return ValueTask.FromResult<(IPlatform, ITextInputService?)>((new LinuxPlatform(), null));
        }

        throw new UiApplicationException("platform-unavailable");
    }

    protected override ValueTask<ITextInputService?> CreateTextServiceAsync(IPlatform platform, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ITextInputService? text = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) && platform is WindowsPlatform windows ? new WindowsTextInputService(windows) : null;
        return ValueTask.FromResult(text);
    }

    protected override IGraphicsSurface CreateGraphicsSurface(IGraphicDevice device, IWindow window)
    {
        var graphics = (WgpuDevice)device;
        return window switch
        {
            WindowsWindow native when OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) => graphics.CreateSurface(Ahjo.Wgpu.SurfaceSource.WindowsHwnd(native.HInstance, native.Hwnd)),
            LinuxWindow native when OperatingSystem.IsLinux() => graphics.CreateSurface(Ahjo.Wgpu.SurfaceSource.XlibWindow(native.XDisplay, native.XWindow)),
            _ => throw new UiApplicationException("platform-unavailable"),
        };
    }

    protected override void PumpGraphicsEvents(IGraphicDevice? device)
    {
        if (device is WgpuDevice graphics)
        {
            graphics.ProcessEvents();
        }
    }
}
