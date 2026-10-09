using System.Text.Json.Serialization;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

[JsonSerializable(typeof(GraphicsRenderStateDesc))]
internal sealed partial class PipelineJsonContext : JsonSerializerContext;
