using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed record RenderStateSnapshot(GraphicsRenderStateDesc Desc, string Key);
