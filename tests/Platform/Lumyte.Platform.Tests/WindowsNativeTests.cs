using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using Lumyte.Platform.Windows;

namespace Lumyte.Platform.Tests;

/// <summary>Verifies native Windows resources on an STA thread when Windows is available.</summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class WindowsNativeTests
{
    /// <summary>Checks HWND ownership and deferred native closure.</summary>
    [WindowsFact]
    public void NativeWindowRetainsHandleUntilSurfaceOwnerReleasesIt() => RunSta(() =>
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowDescriptor());
        WindowsWindow native = Assert.IsType<WindowsWindow>(window);
        Assert.NotEqual(0, native.Hwnd);
        Assert.NotEqual(0, native.HInstance);
        window.Show();
        platform.PumpEvents();
        Assert.True(window.State.IsVisible);
        using IDisposable retained = window.RetainNativeTarget();
        var results = new List<WindowRequestResult>();
        window.RequestCompleted += results.Add;
        WindowRequestId close = window.Close();
        platform.PumpEvents();
        Assert.Equal(WindowLifetime.Closing, window.State.Lifetime);
        Assert.False(window.State.IsVisible);
        Assert.False(window.State.IsActive);
        Assert.NotEqual(0, native.Hwnd);
        Assert.Empty(results);
        Assert.Throws<InvalidOperationException>(window.Dispose);
        retained.Dispose();
        platform.PumpEvents();
        Assert.Equal(WindowLifetime.Closed, window.State.Lifetime);
        Assert.Equal(close, Assert.Single(results).Request);
        Assert.Equal(WindowRequestStatus.Applied, results[0].Status);
        Assert.Throws<InvalidOperationException>(() => native.Hwnd);
    });

    /// <summary>Checks creation preserves the requested logical client size at the target DPI.</summary>
    [WindowsFact]
    public void InitialClientSizeUsesTheWindowDpi() => RunSta(() =>
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowDescriptor { ClientSize = new System.Numerics.Vector2(640, 360) });
        Assert.InRange(window.State.ClientSize.X, 639, 641);
        Assert.InRange(window.State.ClientSize.Y, 359, 361);
    });

    /// <summary>Checks a source initialized after activation reports its current focus.</summary>
    [WindowsFact]
    public void InputSourceStartsWithCurrentFocus() => RunSta(() =>
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowDescriptor());
        window.Show();
        platform.PumpEvents();
        window.Activate();
        platform.PumpEvents();
        using IInputSource source = platform.CreateInputSource();
        var registry = new DeviceRegistry();
        source.Initialize(registry);
        bool expected = window.State.IsActive;
        Assert.All(registry.Devices, device => Assert.Contains(device.DrainEvents(), data => data is FocusData focus && focus.IsFocused == expected));
        source.Shutdown();
        foreach (IInputDevice device in registry.Devices)
        {
            device.Dispose();
        }
    });

    /// <summary>Checks native owners cannot close before native children.</summary>
    [WindowsFact]
    public void OwnerCloseIsDeniedWhileChildIsAlive() => RunSta(() =>
    {
        using var platform = new WindowsPlatform();
        using IWindow owner = platform.CreateWindow(new WindowDescriptor());
        using IWindow child = platform.CreateWindow(new WindowDescriptor
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
        Assert.Equal(WindowLifetime.Open, owner.State.Lifetime);
        child.Close();
        platform.PumpEvents();
        owner.Close();
        platform.PumpEvents();
        Assert.Equal(WindowLifetime.Closed, owner.State.Lifetime);
    });

    [SupportedOSPlatform("windows10.0.17763")]
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(20)))
        {
            throw new TimeoutException("Native STA test did not finish.");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class DeviceRegistry : IInputDeviceRegistry
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
