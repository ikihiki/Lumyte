using BenchmarkDotNet.Attributes;
using Lumyte.Diagnostics.Server;
using Lumyte.Diagnostics.Transport;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Measures authenticated-session publication and detached telemetry snapshots.</summary>
[MemoryDiagnoser]
public class ServerRegistryBenchmarks
{
    private DiagnosticSessionRegistry _registry = null!;
    private DiagnosticMessage _message = null!;
    private Guid _session;

    /// <summary>Gets or sets the publication size.</summary>
    [Params(1, 128)]
    public int EventCount { get; set; }

    /// <summary>Creates a negotiated session and retained events outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _registry = new(new DiagnosticServerOptions(), TimeProvider.System);
        SessionWelcome welcome = _registry.Open(new(Guid.NewGuid(), 1, [new(new("input", "Input", 1), [])]));
        _session = welcome.SessionId;
        var fields = new Dictionary<string, DiagnosticValue>
        {
            ["instance"] = DiagnosticValue.From("benchmark"),
            ["enabled"] = DiagnosticValue.From(true),
            ["frame"] = DiagnosticValue.From(9007199254740993L),
            ["elapsed"] = DiagnosticValue.From(1.25),
        };
        DiagnosticEvent[] events = Enumerable.Range(0, EventCount).Select(index => new DiagnosticEvent("metric", index, "frame.duration", DiagnosticValue.From(1.25), null, null, null, 0, fields)).ToArray();
        _message = new(Guid.NewGuid(), _session, DiagnosticMessageKind.Telemetry, null, null, events);
        _registry.Publish(_session, _message);
    }

    /// <summary>Validates, snapshots, fingerprints and retains an incoming batch.</summary>
    /// <returns>The receipt.</returns>
    [Benchmark]
    public PublishReceipt Publish() => _registry.Publish(_session, _message with { MessageId = Guid.NewGuid() });

    /// <summary>Returns detached values for the operator API.</summary>
    /// <returns>The retained events.</returns>
    [Benchmark]
    public DiagnosticEvent[] ReadRetained() => _registry.Telemetry(_session);
}
