using System.Text.Json.Serialization;

namespace Lumyte.Settings.Benchmark;

[JsonSerializable(typeof(BenchmarkModel))]
internal partial class BenchmarkJson : JsonSerializerContext;
