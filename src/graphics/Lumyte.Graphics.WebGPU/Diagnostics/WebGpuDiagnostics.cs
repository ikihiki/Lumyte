using Lumyte.Graphics.Portable;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.WebGPU;

/// <summary>Device events have no object or submission to which they can safely be reassigned.</summary>
internal sealed class WebGpuDeviceStatus
{
    private readonly TaskCompletionSource<GpuDeviceLostException> failure =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private nint callbackRoot;

    internal Task<GpuDeviceLostException> Failure => failure.Task;
    internal nint RegisterCallbacks() => callbackRoot = GCHandle.ToIntPtr(GCHandle.Alloc(this));
    internal void ReleaseCallbacks()
    {
        nint root = Interlocked.Exchange(ref callbackRoot, 0);
        if (root != 0) { GCHandle.FromIntPtr(root).Free(); }
    }
    internal void Lose(string message) => failure.TrySetResult(new(message));
    internal void ThrowIfFailed()
    {
        if (failure.Task.IsCompletedSuccessfully) { throw failure.Task.Result; }
    }
}

internal static class WebGpuDiagnostics
{
    internal static async Task<IReadOnlyList<GpuDiagnostic>> CombineAsync(
        params Task<IReadOnlyList<GpuDiagnostic>>[] operations)
    {
        IReadOnlyList<GpuDiagnostic>[] diagnostics = await Task.WhenAll(operations).ConfigureAwait(false);
        return diagnostics.SelectMany(static items => items).ToArray();
    }

    internal static async Task ObserveAsync(
        string operation,
        Task<IReadOnlyList<GpuDiagnostic>> diagnostics,
        WebGpuDeviceStatus status)
    {
        await Task.WhenAny(diagnostics, status.Failure).ConfigureAwait(false);
        status.ThrowIfFailed();
        IReadOnlyList<GpuDiagnostic> result = await diagnostics.ConfigureAwait(false);
        status.ThrowIfFailed();
        if (result.Count != 0) { throw new GpuOperationException(operation, result); }
    }

    internal static Task ObserveMapAsync(
        Task<IReadOnlyList<GpuDiagnostic>> creation,
        Task<IReadOnlyList<GpuDiagnostic>> mapping,
        WebGpuDeviceStatus status)
        => ObserveAsync("MapBuffer", CombineAsync(creation, mapping), status);
}
