using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed record RenderStateSnapshot(GraphicsRenderStateDesc Desc, string Key);
