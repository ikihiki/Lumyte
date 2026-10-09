using System.Text.Json.Serialization;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

[JsonSerializable(typeof(GraphicsRenderStateDesc))]
internal sealed partial class PipelineJsonContext : JsonSerializerContext;
