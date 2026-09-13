using P = Lumyte.Graphics.Portable;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private sealed class BufferResource(WebGpuBackend owner, F.BufferHandle handle,
        Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics) : P.GpuBufferHandle
    {
        internal readonly WebGpuBackend Owner = owner;
        internal readonly F.BufferHandle Handle = handle;
        internal readonly Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal bool Destroyed;
    }

    public unsafe P.GpuBufferHandle CreateBuffer(P.GpuBufferDescription description)
    {
        N.BufferUsage usage = MapBufferUsage(description.Usage);
        lock (gate)
        {
            RequireAvailable();
            var native = new F.BufferDescriptorFFI { Size = description.Size, Usage = usage };
            F.BufferHandle buffer = default;
            try
            {
                PushScopes();
                Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics;
                try { buffer = F.WebGPU_FFI.DeviceCreateBuffer(device, &native); }
                finally { diagnostics = PopScopes(); }
                if ((nuint)buffer == 0)
                {
                    status.Lose("WebGPU buffer creation returned no object.");
                    status.ThrowIfFailed();
                }
                return new BufferResource(this, buffer, diagnostics);
            }
            catch
            {
                if ((nuint)buffer != 0)
                {
                    F.WebGPU_FFI.BufferDestroy(buffer);
                    F.WebGPU_FFI.BufferRelease(buffer);
                }
                throw;
            }
        }
    }

    public void DestroyBuffer(P.GpuBufferHandle buffer)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            BufferResource resource = RequireBuffer(buffer);
            resource.Destroyed = true;
            F.WebGPU_FFI.BufferDestroy(resource.Handle);
            F.WebGPU_FFI.BufferRelease(resource.Handle);
        }
    }

    public unsafe ValueTask<P.GpuMappedBufferRange> MapBufferAsync(
        P.GpuBufferHandle buffer, P.GpuMapMode mode, ulong offset, ulong length)
    {
        N.MapMode nativeMode = mode switch
        {
            P.GpuMapMode.Read => N.MapMode.Read,
            P.GpuMapMode.Write => N.MapMode.Write,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        if (length > int.MaxValue) { throw new ArgumentOutOfRangeException(nameof(length), "Mapped memory must fit System.Memory."); }
        nuint nativeOffset = checked((nuint)offset);
        nuint nativeLength = checked((nuint)length);
        lock (gate)
        {
            RequireAvailable();
            BufferResource resource = RequireBuffer(buffer);
            var result = new WebGpuCallbacks.Result<IReadOnlyList<P.GpuDiagnostic>>();
            F.WebGPU_FFI.BufferAddRef(resource.Handle);
            bool submitted = false;
            Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics;
            try
            {
                PushScopes();
                try
                {
                    N.Future future = F.WebGPU_FFI.BufferMapAsync(resource.Handle, nativeMode, nativeOffset, nativeLength, new()
                    {
                        Mode = N.CallbackMode.AllowSpontaneous,
                        Callback = &WebGpuCallbacks.Map,
                        Userdata1 = result.Allocate(),
                    });
                    submitted = true;
                    events!.Register(future, result.Task);
                }
                finally { diagnostics = PopScopes(); }
            }
            catch
            {
                if (submitted) { _ = ReleaseFailedMappingAsync(resource, result.Task); }
                else { result.Release(); F.WebGPU_FFI.BufferRelease(resource.Handle); }
                throw;
            }
            return FinishMapAsync(resource, mode, nativeOffset, nativeLength, result.Task,
                WebGpuDiagnostics.CombineAsync(result.Task, diagnostics));
        }
    }

    private async ValueTask<P.GpuMappedBufferRange> FinishMapAsync(
        BufferResource resource, P.GpuMapMode mode, nuint offset, nuint length,
        Task<IReadOnlyList<P.GpuDiagnostic>> mapping,
        Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics)
    {
        try
        {
            await WebGpuDiagnostics.ObserveMapAsync(resource.Diagnostics, diagnostics, status).ConfigureAwait(false);
            return AcquireMappedRange(resource, mode, offset, length);
        }
        catch
        {
            // Device loss can precede the map callback. The callback's native reference stays alive until it finishes.
            _ = ReleaseFailedMappingAsync(resource, mapping);
            throw;
        }
    }

    private unsafe P.GpuMappedBufferRange AcquireMappedRange(BufferResource resource, P.GpuMapMode mode,
        nuint offset, nuint length)
    {
        lock (gate)
        {
            RequireAvailable();
            ObjectDisposedException.ThrowIf(resource.Destroyed, resource);
            void* pointer = mode == P.GpuMapMode.Read
                ? F.WebGPU_FFI.BufferGetConstMappedRange(resource.Handle, offset, length)
                : F.WebGPU_FFI.BufferGetMappedRange(resource.Handle, offset, length);
            if (pointer == null && length != 0)
            { throw new P.GpuOperationException("MapBuffer", [new(P.GpuDiagnosticKind.Runtime, "WebGPU returned no mapped memory.")]); }
            return new MappedRange(this, resource, mode, pointer, checked((int)length));
        }
    }

    private async Task ReleaseFailedMappingAsync(BufferResource resource,
        Task<IReadOnlyList<P.GpuDiagnostic>> mapping)
    {
        try
        {
            IReadOnlyList<P.GpuDiagnostic> result = await mapping.ConfigureAwait(false);
            lock (gate)
            {
                if (result.Count == 0) { F.WebGPU_FFI.BufferUnmap(resource.Handle); }
            }
        }
        catch (Exception error)
        {
            status.Lose($"WebGPU mapping cleanup failed: {error.Message}");
        }
        finally
        {
            lock (gate) { F.WebGPU_FFI.BufferRelease(resource.Handle); }
        }
    }

    private BufferResource RequireBuffer(P.GpuBufferHandle buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer is not BufferResource resource || !ReferenceEquals(resource.Owner, this))
        { throw new ArgumentException("Buffer belongs to another device.", nameof(buffer)); }
        ObjectDisposedException.ThrowIf(resource.Destroyed, buffer);
        return resource;
    }

    internal Task<IReadOnlyList<P.GpuDiagnostic>> GetCreationDiagnostics(P.GpuBufferHandle buffer)
    {
        lock (gate) { return RequireBuffer(buffer).Diagnostics; }
    }

    internal static N.BufferUsage MapBufferUsage(P.GpuBufferUsage usage)
    {
        const P.GpuBufferUsage known = P.GpuBufferUsage.CopySource | P.GpuBufferUsage.CopyDestination
            | P.GpuBufferUsage.Uniform | P.GpuBufferUsage.Storage | P.GpuBufferUsage.Index
            | P.GpuBufferUsage.IndirectArguments | P.GpuBufferUsage.MapRead | P.GpuBufferUsage.MapWrite;
        if ((usage & ~known) != 0) { throw new ArgumentOutOfRangeException(nameof(usage)); }
        N.BufferUsage result = 0;
        if ((usage & P.GpuBufferUsage.CopySource) != 0) { result |= N.BufferUsage.CopySrc; }
        if ((usage & P.GpuBufferUsage.CopyDestination) != 0) { result |= N.BufferUsage.CopyDst; }
        if ((usage & P.GpuBufferUsage.Uniform) != 0) { result |= N.BufferUsage.Uniform; }
        if ((usage & P.GpuBufferUsage.Storage) != 0) { result |= N.BufferUsage.Storage; }
        if ((usage & P.GpuBufferUsage.Index) != 0) { result |= N.BufferUsage.Index; }
        if ((usage & P.GpuBufferUsage.IndirectArguments) != 0) { result |= N.BufferUsage.Indirect; }
        if ((usage & P.GpuBufferUsage.MapRead) != 0) { result |= N.BufferUsage.MapRead; }
        if ((usage & P.GpuBufferUsage.MapWrite) != 0) { result |= N.BufferUsage.MapWrite; }
        return result;
    }
}
