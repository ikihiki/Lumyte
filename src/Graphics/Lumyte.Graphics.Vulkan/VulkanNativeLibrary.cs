using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

// Silk's default asset resolver roots Assembly.Location/CodeBase and DependencyContext,
// which are not supported by strict NativeAOT. Use the OS loader and explicit app-base
// candidates instead; retain Silk's device/instance proc-address fallback semantics.
internal sealed class VulkanNativeLibrary : INativeContext
{
    private readonly bool _ownsHandle;
    private nint _handle;

    private VulkanNativeLibrary()
    {
        if (OperatingSystem.IsIOS() || OperatingSystem.IsTvOS())
        {
            // Static MoltenVK exposes __Internal symbols from the main program.
            // That handle is borrowed and must never be released with Free.
            _handle = NativeLibrary.GetMainProgramHandle();
            return;
        }

        string[] names = OperatingSystem.IsWindows() ? ["vulkan-1.dll"] :
            OperatingSystem.IsMacOS() ? ["libvulkan.1.dylib", "libvulkan.dylib", "libMoltenVK.dylib"] :
            ["libvulkan.so.1", "libvulkan.so"];
        foreach (string name in names)
        {
            if (NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, name), out _handle) ||
                NativeLibrary.TryLoad(name, out _handle))
            {
                _ownsHandle = true;
                return;
            }
        }

        throw new DllNotFoundException("Could not load the platform Vulkan library from the application directory or OS loader search path.");
    }

    public nint GetProcAddress(string proc, int? slot = null)
        => TryGetProcAddress(proc, out nint address, slot) ? address : throw new EntryPointNotFoundException(proc);

    public bool TryGetProcAddress(string proc, out nint address, int? slot = null)
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        return NativeLibrary.TryGetExport(_handle, proc, out address);
    }

    public void Dispose()
    {
        nint handle = Interlocked.Exchange(ref _handle, 0);
        if (handle != 0 && _ownsHandle)
        {
            NativeLibrary.Free(handle);
        }
    }

    internal static Vk CreateApi()
    {
        var library = new VulkanNativeLibrary();
        var context = new MultiNativeContext(library, null!);
        try
        {
            var api = new Vk(context);
            context.Contexts[1] = new LamdaNativeContext(name =>
            {
                if (name.EndsWith("ProcAddr", StringComparison.Ordinal))
                {
                    return 0;
                }

                nint address = api.GetDeviceProcAddr(api.CurrentDevice.GetValueOrDefault(), name);
                return address != 0 ? address : api.GetInstanceProcAddr(api.CurrentInstance.GetValueOrDefault(), name);
            });
            return api;
        }
        catch
        {
            // MultiNativeContext has an uninitialized fallback slot until Vk exists.
            library.Dispose();
            throw;
        }
    }
}
