using System.Collections.Concurrent;

namespace Lumyte.Diagnostics;

internal sealed class TelemetryRouter
{
    private readonly ConcurrentDictionary<string, DiagnosticTelemetry> _sinks = new(StringComparer.Ordinal);

    public void Register(string id, DiagnosticTelemetry sink) => _sinks[id] = sink;

    public void Remove(string id) => _sinks.TryRemove(id, out _);

    public bool TryGet(string id, out DiagnosticTelemetry? sink) => _sinks.TryGetValue(id, out sink);
}
