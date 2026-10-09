using System.Text.Json.Serialization;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

[JsonSerializable(typeof(GraphicsRenderStateDesc))]
internal sealed partial class PipelineJsonContext : JsonSerializerContext;
