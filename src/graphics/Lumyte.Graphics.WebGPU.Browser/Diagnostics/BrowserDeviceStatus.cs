using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser;

/// <summary>Unattributed device failures cannot be reassigned to an arbitrary recording or resource.</summary>
internal sealed class BrowserDeviceStatus
{
    private readonly TaskCompletionSource<GpuDeviceLostException> failure =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task<GpuDeviceLostException> Failure => failure.Task;
    internal void Lose(string message) => failure.TrySetResult(new(message));
    internal void ThrowIfFailed()
    {
        if (failure.Task.IsCompletedSuccessfully) { throw failure.Task.Result; }
    }
}

internal static class BrowserDiagnostics
{
    internal static async Task<IReadOnlyList<P.GpuDiagnostic>> CombineAsync(
        params Task<IReadOnlyList<P.GpuDiagnostic>>[] operations)
    {
        IReadOnlyList<P.GpuDiagnostic>[] diagnostics = await Task.WhenAll(operations).ConfigureAwait(false);
        return diagnostics.SelectMany(static items => items).ToArray();
    }

    internal static async Task ObserveAsync(string operation,
        Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics, BrowserDeviceStatus status)
    {
        await Task.WhenAny(diagnostics, status.Failure).ConfigureAwait(false);
        status.ThrowIfFailed();
        IReadOnlyList<P.GpuDiagnostic> result = await diagnostics.ConfigureAwait(false);
        status.ThrowIfFailed();
        if (result.Count != 0) { throw new P.GpuOperationException(operation, result); }
    }

    internal static Task ObserveMapAsync(Task<IReadOnlyList<P.GpuDiagnostic>> creation,
        Task<IReadOnlyList<P.GpuDiagnostic>> mapping, BrowserDeviceStatus status)
        => ObserveAsync("MapBuffer", CombineAsync(creation, mapping), status);
}
