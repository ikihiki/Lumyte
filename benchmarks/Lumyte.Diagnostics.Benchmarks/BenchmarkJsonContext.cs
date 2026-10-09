using System.Text.Json.Serialization;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Static JSON metadata for the same typed schema.</summary>
[JsonSerializable(typeof(WireBatch))]
public partial class BenchmarkJsonContext : JsonSerializerContext
{
}
