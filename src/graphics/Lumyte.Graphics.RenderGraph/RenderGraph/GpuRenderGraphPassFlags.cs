namespace Lumyte.Graphics.RenderGraph;

[Flags]
public enum GpuRenderGraphPassFlags
{
    None = 0,
    NeverCull = 1 << 0,
}
