namespace Lumyte.Graphics.RenderGraph.Legacy;

[Flags]
public enum GpuRenderGraphPassFlags
{
    None = 0,
    NeverCull = 1 << 0,
}
