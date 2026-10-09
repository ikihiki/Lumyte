using System.Collections.Concurrent;

namespace Lumyte.Diagnostics;

internal sealed class TelemetryRouter
{
    private readonly ConcurrentDictionary<string, DiagnosticTelemetry> _sinks = new(StringComparer.Ordinal);

    public void Register(Guid id, DiagnosticTelemetry sink) => _sinks[id.ToString("D")] = sink;

    public void Remove(Guid id) => _sinks.TryRemove(id.ToString("D"), out _);

    public bool TryGet(string id, out DiagnosticTelemetry? sink) => _sinks.TryGetValue(id, out sink);
}
