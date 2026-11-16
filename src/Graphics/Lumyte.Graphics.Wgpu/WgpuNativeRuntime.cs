using System.Reflection;
using System.Runtime.InteropServices;
using Ahjo.Wgpu.Native;

namespace Lumyte.Graphics.Wgpu;

/// <summary>Isolates the corrected, ABI-compatible native dependency from the upstream runtime asset.</summary>
internal static class WgpuNativeRuntime
{
    private static readonly Lazy<nint> _library = new(() => NativeLibrary.Load("lumyte_wgpu_native", typeof(WGPU).Assembly, null));
    private static readonly bool _installed = Install();

    internal static void EnsureInstalled() => _ = _installed;

    private static bool Install()
    {
        NativeLibrary.SetDllImportResolver(typeof(WGPU).Assembly, Resolve);
        return true;
    }

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != "wgpu_native")
        {
            return 0;
        }

        // Never fall back to Ahjo 0.7.0's leaking unsubmitted-command implementation.
        return _library.Value;
    }
}
