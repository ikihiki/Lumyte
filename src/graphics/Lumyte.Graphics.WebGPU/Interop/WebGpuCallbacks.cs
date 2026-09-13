using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Lumyte.Graphics.Portable;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

/// <summary>Callbacks only publish managed results. Native calls and cleanup run outside the callback stack.</summary>
internal static unsafe class WebGpuCallbacks
{
    internal sealed class Result<T>
    {
        private readonly TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private nint root;
        internal Task<T> Task => completion.Task;
        internal void* Allocate() => (void*)(root = GCHandle.ToIntPtr(GCHandle.Alloc(this)));
        internal void Release()
        {
            nint handle = Interlocked.Exchange(ref root, 0);
            if (handle != 0) { GCHandle.FromIntPtr(handle).Free(); }
        }
        internal void Complete(T value) => completion.TrySetResult(value);
        internal void Fail(Exception error) => completion.TrySetException(error);
    }

    private static string Text(F.StringViewFFI message)
        => message.Data == null ? string.Empty
            : Encoding.UTF8.GetString(message.Length == nuint.MaxValue
                ? MemoryMarshal.CreateReadOnlySpanFromNullTerminated(message.Data)
                : new ReadOnlySpan<byte>(message.Data, checked((int)message.Length)));

    private static void Finish<T>(void* userdata, Func<T> read)
    {
        GCHandle handle = GCHandle.FromIntPtr((nint)userdata);
        var result = (Result<T>)handle.Target!;
        try { result.Complete(read()); }
        catch (Exception error) { result.Fail(error); }
        finally { result.Release(); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void Adapter(N.RequestAdapterStatus status, F.AdapterHandle adapter,
        F.StringViewFFI message, void* userdata, void* unused)
        => Finish(userdata, () => (status, adapter, Text(message)));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void Device(N.RequestDeviceStatus status, F.DeviceHandle device,
        F.StringViewFFI message, void* userdata, void* unused)
        => Finish(userdata, () => (status, device, Text(message)));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void Scope(N.PopErrorScopeStatus status, N.ErrorType type,
        F.StringViewFFI message, void* userdata, void* unused)
        => Finish<IReadOnlyList<GpuDiagnostic>>(userdata, () => status != N.PopErrorScopeStatus.Success
            ? [new(GpuDiagnosticKind.Runtime, $"Error scope: {status}. {Text(message)}")]
            : type == N.ErrorType.NoError ? []
            : [new(type switch
            {
                N.ErrorType.Validation => GpuDiagnosticKind.Validation,
                N.ErrorType.OutOfMemory => GpuDiagnosticKind.OutOfMemory,
                N.ErrorType.Internal => GpuDiagnosticKind.Internal,
                _ => GpuDiagnosticKind.Runtime,
            }, Text(message))]);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void Map(N.MapAsyncStatus status, F.StringViewFFI message, void* userdata, void* unused)
        => Finish<IReadOnlyList<GpuDiagnostic>>(userdata, () => status == N.MapAsyncStatus.Success
            ? [] : [new(GpuDiagnosticKind.Runtime, $"Buffer map: {status}. {Text(message)}")]);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void WorkDone(N.QueueWorkDoneStatus status, F.StringViewFFI message, void* userdata, void* unused)
        => Finish(userdata, () => (status, Text(message)));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void Lost(F.DeviceHandle* device, N.DeviceLostReason reason,
        F.StringViewFFI message, void* userdata, void* unused)
    {
        GCHandle handle = GCHandle.FromIntPtr((nint)userdata);
        var status = (WebGpuDeviceStatus)handle.Target!;
        try
        {
            // A failed request reports its diagnostic through RequestDevice, without an existing device to lose.
            if (reason != N.DeviceLostReason.FailedCreation)
            { status.Lose($"WebGPU device lost ({reason}): {Text(message)}"); }
        }
        catch (Exception error) { status.Lose($"WebGPU device lost ({reason}): {error.Message}"); }
        finally { status.ReleaseCallbacks(); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void Uncaptured(F.DeviceHandle* device, N.ErrorType type,
        F.StringViewFFI message, void* userdata, void* unused)
    {
        var status = (WebGpuDeviceStatus)GCHandle.FromIntPtr((nint)userdata).Target!;
        try { status.Lose($"Unattributed WebGPU runtime error ({type}): {Text(message)}"); }
        catch (Exception error) { status.Lose($"Unattributed WebGPU runtime error: {error.Message}"); }
    }
}
