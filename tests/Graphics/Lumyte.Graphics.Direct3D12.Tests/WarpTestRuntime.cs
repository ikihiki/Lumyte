using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Explicitly loads the pinned development-only WARP before DXGI adapter discovery.</summary>
internal static class WarpTestRuntime
{
    // Keep one module reference for the test process. It must outlive every WARP device.
    private static readonly Lazy<nint> _module = new(Load);

    internal static void Enable()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The pinned WARP test control requires Windows.");
        }

        _ = _module.Value;
    }

    private static nint Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "d3d10warp.dll");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The pinned Microsoft.Direct3D.WARP 1.0.22 test driver is missing.", path);
        }

        nint module = NativeLibrary.Load(path);
        Console.WriteLine($"WARP test driver: {path}, version {FileVersionInfo.GetVersionInfo(path).FileVersion}");
        return module;
    }
}
