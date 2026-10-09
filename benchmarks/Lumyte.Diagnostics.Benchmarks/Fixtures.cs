namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Deterministic fixtures with precise integers and non-ASCII text.</summary>
public static class Fixtures
{
    /// <summary>Creates the selected workload.</summary>
    /// <param name="workload">The workload argument.</param>
    /// <returns>The computed result.</returns>
    public static WireBatch Create(Workload workload)
    {
        int count = workload switch
        {
            Workload.Operation => 1,
            Workload.Graph => 1000,
            Workload.Image => 0,
            _ => 128,
        };
        WireEvent[] events = Enumerable.Range(0, count).Select(index => new WireEvent
        {
            Kind = workload.ToString(),
            Timestamp = 9007199254740993L + index,
            Name = workload == Workload.Log ? "Lumyte.Physics.PhysicsLoop" : "physics.step.duration",
            Value = workload == Workload.Log ? new() { Kind = 3, Text = "物理ステップ完了: active bodies = " + index }
                : new() { Kind = 2, Number = 0.125 + (index / 10.0) },
            TraceId = workload is Workload.Trace or Workload.Log ? "0123456789abcdef0123456789abcdef" : null,
            SpanId = workload is Workload.Trace or Workload.Log ? "0123456789abcdef" : null,
            ParentSpanId = workload == Workload.Trace ? "fedcba9876543210" : null,
            DurationTicks = workload == Workload.Trace ? 12345 + index : 0,
            Fields = workload == Workload.Operation
                ? [new() { Name = "lease-id", Value = new() { Kind = 3, Text = "01234567-89ab-cdef-0123-456789abcdef" } }]
                : [new() { Name = "active-bodies", Value = new() { Kind = 1, Integer = 9007199254740993L + index } }, new() { Name = "enabled", Value = new() { Kind = 0, Boolean = true } }],
        }).ToArray();
        byte[] binary = workload == Workload.Image ? new byte[256 * 1024] : [];
        new Random(42).NextBytes(binary);
        return new() { SessionId = "01234567-89ab-cdef-0123-456789abcdef", Sequence = 9007199254740993, Events = events, Binary = binary };
    }
}
