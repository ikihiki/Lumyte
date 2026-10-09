using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed record RenderStateSnapshot(GraphicsRenderStateDesc Desc, string Key);
