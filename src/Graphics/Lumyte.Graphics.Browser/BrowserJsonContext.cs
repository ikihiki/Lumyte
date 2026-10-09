using System.Text.Json.Serialization;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DeviceCaps))]
[JsonSerializable(typeof(SamplerDesc))]
internal partial class BrowserJsonContext : JsonSerializerContext
{
}
