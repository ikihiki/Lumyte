using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Passes;

public static class ImagePassExtensions
{
    public static TexturePassResult AddClearPass(this GpuRenderGraph graph, string name, ClearPassRequest request) =>
        graph.AddPass(name, ClearPassContract.Instance, request);

    public static TexturePassResult AddCopyPass(this GpuRenderGraph graph, string name, TextureCopyPassRequest request) =>
        graph.AddPass(name, TextureCopyPassContract.Instance, request);

    public static TexturePassResult AddOutputPass(this GpuRenderGraph graph, string name, OutputPassRequest request) =>
        graph.AddPass(name, OutputPassContract.Instance, request);
}
