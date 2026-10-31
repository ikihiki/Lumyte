using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Direct3D12;

internal static partial class NativeMethods
{
    [LibraryImport(Library, EntryPoint = "window_valid")]
    internal static partial int WindowValid(nint window);

    [LibraryImport(Library, EntryPoint = "tearing_supported")]
    internal static partial int TearingSupported();

    [LibraryImport(Library, EntryPoint = "swapchain_create")]
    internal static partial int SwapchainCreate(nint device, nint window, uint width, uint height, uint format, int immediate, out nint swapchain);

    [LibraryImport(Library, EntryPoint = "swapchain_destroy")]
    internal static partial void SwapchainDestroy(nint swapchain);

    [LibraryImport(Library, EntryPoint = "swapchain_resize")]
    internal static partial int SwapchainResize(nint swapchain, uint width, uint height, uint format, int immediate);

    [LibraryImport(Library, EntryPoint = "swapchain_acquire")]
    internal static partial int SwapchainAcquire(nint swapchain, out nint texture);

    [LibraryImport(Library, EntryPoint = "swapchain_present")]
    internal static partial int SwapchainPresent(nint swapchain);
}
