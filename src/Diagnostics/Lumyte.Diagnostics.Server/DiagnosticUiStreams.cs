namespace Lumyte.Diagnostics.Server;

internal sealed class DiagnosticUiStreams : IDisposable
{
    internal SemaphoreSlim Slots { get; } = new(16, 16);

    public void Dispose() => Slots.Dispose();
}
