namespace Lumyte.Graphics.RenderGraph.Legacy;

[Flags]
public enum GpuRenderGraphAccess
{
    Read = 1 << 0,
    Write = 1 << 1,
    ReadWrite = Read | Write,
}
