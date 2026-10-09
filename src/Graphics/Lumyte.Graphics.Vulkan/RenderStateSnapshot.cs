using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed record RenderStateSnapshot(GraphicsRenderStateDesc Desc, string Key);
