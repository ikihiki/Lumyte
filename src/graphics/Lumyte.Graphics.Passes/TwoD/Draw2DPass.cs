using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Passes;

public sealed class Draw2DPassRequest
{
    public Draw2DPassRequest(GpuGraphValue<Draw2DScene> scene, GpuRenderGraphTexture color, IEnumerable<GpuRenderGraphTexture>? readTextures = null)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        Color = color ?? throw new ArgumentNullException(nameof(color));
        ReadTextures = Array.AsReadOnly(readTextures?.Distinct().ToArray() ?? []);
    }
    public GpuGraphValue<Draw2DScene> Scene { get; }
    public GpuRenderGraphTexture Color { get; }
    public IReadOnlyList<GpuRenderGraphTexture> ReadTextures { get; }
}

public readonly record struct Draw2DPassResult(GpuRenderGraphTexture Color);

public sealed class Draw2DPassContract : IGpuRenderPassContract<Draw2DPassRequest, Draw2DPassResult>
{
    public static Draw2DPassContract Instance { get; } = new();
    private Draw2DPassContract() { }
    public string Id => "lumyte.draw.2d";
    public int Version => 1;
    public Draw2DPassRequest Snapshot(Draw2DPassRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Scene.TryGetConstant(out var scene))
        { return request; }
        ArgumentNullException.ThrowIfNull(scene);
        return new(request.Scene, request.Color, request.ReadTextures.Concat(scene.LogicalTextures));
    }
    public Draw2DPassResult Declare(GpuPassDeclarationContext context, Draw2DPassRequest request)
    {
        var description = request.Color.Description;
        if (description.Dimension != GpuGraphTextureDimension.TwoD || description.MipLevelCount != 1 || description.DepthOrArrayLayers != 1 || description.SampleCount != 1)
        { throw new NotSupportedException("2D drawing version 1 requires a 2D target with one mip, layer, and sample."); }
        if (description.Format is not (GpuFormat.Rgba8Unorm or GpuFormat.Bgra8Unorm or GpuFormat.Rgba16Float))
        { throw new NotSupportedException("2D drawing version 1 requires a linear RGBA8, BGRA8, or RGBA16Float target."); }
        if (request.ReadTextures.Contains(request.Color))
        { throw new ArgumentException("A 2D target cannot be sampled by its own scene.", nameof(request)); }
        context.ReadWrite(request.Color);
        foreach (var texture in request.ReadTextures)
        { context.Read(texture); }
        context.ReadInput(request.Scene, Draw2DSceneInputContract.Instance);
        return new(request.Color);
    }
}

public sealed class Draw2DSceneInputContract : IGpuGraphInputContract<Draw2DScene>
{
    public static Draw2DSceneInputContract Instance { get; } = new();
    private Draw2DSceneInputContract() { }
    public Draw2DScene Snapshot(Draw2DScene value) => value ?? throw new ArgumentNullException(nameof(value));
    public void Retain(GpuRenderInputRetentionContext context, Draw2DScene value)
    {
        foreach (var upload in value.OwnUploads)
        { context.ReadUpload(upload); }
        foreach (var texture in value.OwnTextures)
        { context.UseDeclaredRead(texture); }
        foreach (var child in value.ChildScenes)
        { context.ReadSnapshot(child, this); }
    }
}

public static class Draw2DPassExtensions
{
    public static Draw2DPassResult Add2DPass(this GpuRenderGraph graph, string name, Draw2DPassRequest request) => graph.AddPass(name, Draw2DPassContract.Instance, request);
}
