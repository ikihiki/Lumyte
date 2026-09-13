using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Passes;

public sealed class ClearValueInputContract : IGpuGraphInputContract<TextureClearValue>
{
    public static ClearValueInputContract Instance { get; } = new();

    private ClearValueInputContract() { }

    public TextureClearValue Snapshot(TextureClearValue value) => value;

    public void Retain(GpuRenderInputRetentionContext context, TextureClearValue snapshot) { }
}
