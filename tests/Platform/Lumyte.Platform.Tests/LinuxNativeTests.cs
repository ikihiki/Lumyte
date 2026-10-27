using System.Numerics;
using System.Runtime.Versioning;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Linux;
using Lumyte.Windowing.Abstractions;
using Xunit;

namespace Lumyte.Platform.Tests;

/// <summary>Verifies real X11 windows on a display or Xvfb.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxNativeTests
{
    /// <summary>Checks popup creation rejects missing owners and activating policies before allocation.</summary>
    [LinuxFact]
    public void PopupRequiresOwnerAndPreservedFocus()
    {
        using var platform = new LinuxPlatform();
        using IWindow owner = platform.CreateWindow(new WindowDescriptor());
        Assert.Throws<ArgumentException>(() => platform.CreateWindow(new WindowDescriptor { Kind = WindowKind.Popup, Activation = WindowActivation.PreserveFocus }));
        Assert.Throws<ArgumentException>(() => platform.CreateWindow(new WindowDescriptor { Kind = WindowKind.Popup, Owner = owner }));
        using IWindow popup = platform.CreateWindow(new WindowDescriptor { Kind = WindowKind.Popup, Owner = owner, Activation = WindowActivation.PreserveFocus });
        Assert.Equal(owner.Id, popup.State.Owner);
    }

    /// <summary>Checks observed dimensions and native retention across deferred closure.</summary>
    [LinuxFact]
    public void X11WindowShowsResizesAndClosesAfterRetentionRelease()
    {
        using var platform = new LinuxPlatform();
        using IWindow window = platform.CreateWindow(new WindowDescriptor());
        LinuxWindow native = Assert.IsType<LinuxWindow>(window);
        Assert.NotEqual(0, native.XDisplay);
        Assert.NotEqual((nuint)0, native.XWindow);
        window.Show();
        PumpUntil(platform, () => window.State.IsVisible);
        window.Resize(new Vector2(400, 300));
        PumpUntil(platform, () => window.State.PixelSize == new WindowPixelSize(400, 300));
        using IDisposable retained = window.RetainNativeTarget();
        var results = new List<WindowRequestResult>();
        window.RequestCompleted += results.Add;
        WindowRequestId close = window.Close();
        platform.PumpEvents();
        Assert.Equal(WindowLifetime.Closing, window.State.Lifetime);
        Assert.Throws<InvalidOperationException>(window.Dispose);
        retained.Dispose();
        PumpUntil(platform, () => window.State.Lifetime == WindowLifetime.Closed);
        Assert.Single(results, r => r.Request == close && r.Status == WindowRequestStatus.Applied);
    }

    /// <summary>Checks native child ownership and optional input source registration.</summary>
    [LinuxFact]
    public void X11NativeOwnersAndInputAcquisitionUsePlatformContracts()
    {
        using var platform = new LinuxPlatform();
        using IWindow owner = platform.CreateWindow(new WindowDescriptor());
        using IWindow popup = platform.CreateWindow(new WindowDescriptor
        {
            Owner = owner,
            Kind = WindowKind.Popup,
            Activation = WindowActivation.PreserveFocus,
        });
        var results = new List<WindowRequestResult>();
        owner.RequestCompleted += results.Add;
        owner.Close();
        platform.PumpEvents();
        Assert.Equal(WindowRequestStatus.Denied, Assert.Single(results).Status);
        using IInputSource source = platform.CreateInputSource();
        var registry = new Registry();
        source.Initialize(registry);
        source.Update();
        Assert.Equal(new[] { InputDeviceKind.Keyboard, InputDeviceKind.Mouse }, registry.Devices.Select(d => d.Descriptor.Kind));
        Assert.Throws<InvalidOperationException>(() => platform.CreateInputSource());
        source.Shutdown();
        foreach (IInputDevice device in registry.Devices)
        {
            device.Dispose();
        }
    }

    private static void PumpUntil(LinuxPlatform platform, Func<bool> ready)
    {
        long deadline = Environment.TickCount64 + 3000;
        while (!ready() && Environment.TickCount64 < deadline)
        {
            platform.PumpEvents();
            Thread.Sleep(1);
        }

        Assert.True(ready());
    }

    private sealed class Registry : IInputDeviceRegistry
    {
        public List<IInputDevice> Devices { get; } = [];

        public InputDeviceId RegisterDevice(IInputDevice device)
        {
            Devices.Add(device);
            return new InputDeviceId((ulong)Devices.Count);
        }

        public void UnregisterDevice(InputDeviceId device)
        {
        }
    }
}
