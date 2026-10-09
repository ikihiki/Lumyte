namespace Lumyte.Diagnostics.Server;

internal sealed class DiagnosticDashboardLimits : IDisposable
{
    public SemaphoreSlim Slots { get; } = new(16, 16);

    public void Dispose() => Slots.Dispose();
}
