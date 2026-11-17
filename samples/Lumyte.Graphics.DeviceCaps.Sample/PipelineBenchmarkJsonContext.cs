using System.Text.Json.Serialization;

[JsonSerializable(typeof(PipelineBenchmarkLog))]
internal partial class PipelineBenchmarkJsonContext : JsonSerializerContext
{
}
