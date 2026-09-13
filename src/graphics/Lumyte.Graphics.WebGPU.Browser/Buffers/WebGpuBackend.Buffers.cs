using System.Runtime.InteropServices.JavaScript;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser;

public sealed partial class WebGpuBackend
{
    private sealed class BufferResource(WebGpuBackend owner, JSObject handle, P.GpuBufferDescription description,
        Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics) : P.GpuBufferHandle
    {
        internal readonly WebGpuBackend Owner = owner;
        internal readonly JSObject Handle = handle;
        internal readonly P.GpuBufferDescription Description = description;
        internal readonly Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal bool Destroyed;
    }

    public P.GpuBufferHandle CreateBuffer(P.GpuBufferDescription description)
    {
        RequireAvailable();
        var created = CreateObject(device, "buffer", new
        {
            Size = Exact(description.Size, nameof(description)),
            Usage = MapBufferUsage(description.Usage),
        }, []);
        try { return new BufferResource(this, created.Handle, description, created.Diagnostics); }
        catch
        {
            try { BrowserInterop.Destroy(created.Handle); }
            finally { created.Handle.Dispose(); }
            throw;
        }
    }

    public void DestroyBuffer(P.GpuBufferHandle buffer)
    {
        runtime.RequireThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        BufferResource resource = RequireBuffer(buffer);
        resource.Destroyed = true;
        try { BrowserInterop.Destroy(resource.Handle); }
        finally { resource.Handle.Dispose(); }
    }

    public ValueTask<P.GpuMappedBufferRange> MapBufferAsync(P.GpuBufferHandle buffer,
        P.GpuMapMode mode, ulong offset, ulong length)
    {
        RequireAvailable();
        BufferResource resource = RequireBuffer(buffer);
        int nativeMode = mode switch
        {
            P.GpuMapMode.Read => 1,
            P.GpuMapMode.Write => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        if (length > int.MaxValue) { throw new ArgumentOutOfRangeException(nameof(length), "Mapped memory must fit System.Memory."); }
        double nativeOffset = Exact(offset, nameof(offset));
        double nativeLength = Exact(length, nameof(length));
        Task? mapping = null;
        try
        {
            Task<IReadOnlyList<P.GpuDiagnostic>> scopes;
            BrowserInterop.PushErrorScopes(device);
            try
            {
                try { mapping = BrowserInterop.MapAsync(resource.Handle, nativeMode, nativeOffset, nativeLength); }
                catch (JSException error) { mapping = Task.FromException(error); }
            }
            finally { scopes = ReadDiagnosticsAsync(BrowserInterop.PopErrorScopesAsync(device)); }
            return FinishMapAsync(resource, mode, nativeOffset, (int)length, mapping,
                CollectMappingDiagnosticsAsync(mapping, scopes));
        }
        catch
        {
            if (mapping is not null) { _ = ReleaseFailedMappingAsync(resource, mapping); }
            throw;
        }
    }

    private static async Task<IReadOnlyList<P.GpuDiagnostic>> CollectMappingDiagnosticsAsync(
        Task mapping, Task<IReadOnlyList<P.GpuDiagnostic>> scopes)
    {
        JSException? mapError = null;
        try { await mapping.ConfigureAwait(false); }
        catch (JSException error) { mapError = error; }
        IReadOnlyList<P.GpuDiagnostic> diagnostics = await scopes.ConfigureAwait(false);
        return diagnostics.Count != 0 || mapError is null ? diagnostics
            : [new(P.GpuDiagnosticKind.Runtime, mapError.Message)];
    }

    private async ValueTask<P.GpuMappedBufferRange> FinishMapAsync(BufferResource resource, P.GpuMapMode mode,
        double offset, int length, Task mapping, Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics)
    {
        try
        {
            // Resume on the browser's synchronization context before accessing JavaScript objects.
            await BrowserDiagnostics.ObserveMapAsync(resource.Diagnostics, diagnostics, status);
            RequireAvailable();
            ObjectDisposedException.ThrowIf(resource.Destroyed, resource);
            JSObject mapped = BrowserInterop.GetMappedRange(resource.Handle, offset, length);
            try
            {
                byte[] bytes = new byte[length];
                BrowserInterop.ReadMapped(mapped, bytes);
                return new MappedRange(this, resource, mode, mapped, bytes);
            }
            catch { mapped.Dispose(); throw; }
        }
        catch (JSException error)
        {
            _ = ReleaseFailedMappingAsync(resource, mapping);
            throw new P.GpuOperationException("MapBuffer", [new(P.GpuDiagnosticKind.Runtime, error.Message)]);
        }
        catch
        {
            _ = ReleaseFailedMappingAsync(resource, mapping);
            throw;
        }
    }

    private async Task ReleaseFailedMappingAsync(BufferResource resource, Task mapping)
    {
        try
        {
            // A rejected second map must never unmap the first caller's successful lease.
            await mapping;
            runtime.RequireThread();
            if (!resource.Destroyed) { BrowserInterop.Unmap(resource.Handle); }
        }
        catch (JSException) when (!mapping.IsCompletedSuccessfully) { }
        catch (Exception error) { status.Lose($"Browser WebGPU mapping cleanup failed: {error.Message}"); }
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
    { runtime.RequireThread(); return RequireBuffer(buffer).Diagnostics; }

    private static uint MapBufferUsage(P.GpuBufferUsage usage)
    {
        const P.GpuBufferUsage known = P.GpuBufferUsage.CopySource | P.GpuBufferUsage.CopyDestination
            | P.GpuBufferUsage.Uniform | P.GpuBufferUsage.Storage | P.GpuBufferUsage.Index
            | P.GpuBufferUsage.IndirectArguments | P.GpuBufferUsage.MapRead | P.GpuBufferUsage.MapWrite;
        if ((usage & ~known) != 0) { throw new ArgumentOutOfRangeException(nameof(usage)); }
        uint result = 0;
        if ((usage & P.GpuBufferUsage.MapRead) != 0) { result |= 1; }
        if ((usage & P.GpuBufferUsage.MapWrite) != 0) { result |= 2; }
        if ((usage & P.GpuBufferUsage.CopySource) != 0) { result |= 4; }
        if ((usage & P.GpuBufferUsage.CopyDestination) != 0) { result |= 8; }
        if ((usage & P.GpuBufferUsage.Index) != 0) { result |= 16; }
        if ((usage & P.GpuBufferUsage.Uniform) != 0) { result |= 64; }
        if ((usage & P.GpuBufferUsage.Storage) != 0) { result |= 128; }
        if ((usage & P.GpuBufferUsage.IndirectArguments) != 0) { result |= 256; }
        return result;
    }
}
