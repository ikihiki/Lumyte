using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Passes;

public static class ImagePassExtensions
{
    public static TexturePassResult AddBlitPass(this GpuRenderGraph graph, string name, BlitPassRequest request) =>
        graph.AddPass(name, BlitPassContract.Instance, request);
    public static BlurPassResult AddBlurPass(this GpuRenderGraph graph, string name, BlurPassRequest request) =>
        graph.AddPass(name, BlurPassContract.Instance, request);
    public static TexturePassResult AddCompositePass(this GpuRenderGraph graph, string name, CompositePassRequest request) =>
        graph.AddPass(name, CompositePassContract.Instance, request);
    public static ToneMapPassResult AddToneMapPass(this GpuRenderGraph graph, string name, ToneMapPassRequest request) =>
        graph.AddPass(name, ToneMapPassContract.Instance, request);
    public static TexturePassResult AddClearPass(this GpuRenderGraph graph, string name, ClearPassRequest request) =>
        graph.AddPass(name, ClearPassContract.Instance, request);

    public static TexturePassResult AddCopyPass(this GpuRenderGraph graph, string name, TextureCopyPassRequest request) =>
        graph.AddPass(name, TextureCopyPassContract.Instance, request);

    public static TexturePassResult AddOutputPass(this GpuRenderGraph graph, string name, OutputPassRequest request) =>
        graph.AddPass(name, OutputPassContract.Instance, request);
}
