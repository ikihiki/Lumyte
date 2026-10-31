using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Direct3D12;

internal static partial class NativeMethods
{
    private const string Library = "lumyte_graphics_d3d12";

    [LibraryImport(Library, EntryPoint = "device_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int DeviceCreate(uint adapter, int warp, int debug, int partial, string sdkPath, out nint device);

    [LibraryImport(Library, EntryPoint = "device_sdk_version")]
    internal static partial uint DeviceSdkVersion(nint device);

    [LibraryImport(Library, EntryPoint = "device_destroy")]
    internal static partial void DeviceDestroy(nint device);

    [LibraryImport(Library, EntryPoint = "device_removed")]
    internal static partial int DeviceRemoved(nint device);

    [LibraryImport(Library, EntryPoint = "object_release")]
    internal static partial void Release(nint handle);

    [LibraryImport(Library, EntryPoint = "buffer_create")]
    internal static partial int BufferCreate(nint device, ulong size, int memory, int writable, out nint buffer);

    [LibraryImport(Library, EntryPoint = "buffer_map")]
    internal static partial int BufferMap(nint buffer, ulong size, int read, out nint pointer);

    [LibraryImport(Library, EntryPoint = "buffer_unmap")]
    internal static partial void BufferUnmap(nint buffer, ulong size, int write);

    [LibraryImport(Library, EntryPoint = "commands_create")]
    internal static partial int CommandsCreate(nint device, out nint commands);

    [LibraryImport(Library, EntryPoint = "commands_destroy")]
    internal static partial void CommandsDestroy(nint commands);

    [LibraryImport(Library, EntryPoint = "commands_finish")]
    internal static partial int CommandsFinish(nint commands);

    [LibraryImport(Library, EntryPoint = "commands_copy")]
    internal static partial void CommandsCopy(nint commands, nint source, ulong sourceOffset, nint destination, ulong destinationOffset, ulong size);

    [LibraryImport(Library, EntryPoint = "fence_create")]
    internal static partial int FenceCreate(nint device, out nint fence);

    [LibraryImport(Library, EntryPoint = "commands_execute")]
    internal static unsafe partial int CommandsExecute(nint device, nint* commands, uint count);

    [LibraryImport(Library, EntryPoint = "queue_signal")]
    internal static partial int QueueSignal(nint device, nint fence);

    [LibraryImport(Library, EntryPoint = "fence_completed")]
    internal static partial ulong FenceCompleted(nint fence);

    [LibraryImport(Library, EntryPoint = "queue_signal_value")]
    internal static partial int QueueSignalValue(nint device, nint fence, ulong value);

    [LibraryImport(Library, EntryPoint = "queue_wait_value")]
    internal static partial int QueueWaitValue(nint device, nint fence, ulong value);
}
