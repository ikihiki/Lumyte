using System.Text.Json.Serialization;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

[JsonSerializable(typeof(GraphicsRenderStateDesc))]
internal sealed partial class PipelineJsonContext : JsonSerializerContext;
