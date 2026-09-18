using System.Numerics;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Passes;

public sealed record ModelPassRequest(GpuGraphValue<ModelRenderSnapshot> Data, GpuRenderGraphTexture Color, GpuRenderGraphTexture Depth)
{
    public Vector4 ClearColor { get; init; }
    public float ClearDepth { get; init; } = 1;
}
public readonly record struct ModelPassResult(GpuRenderGraphTexture Color, GpuRenderGraphTexture Depth);
public sealed class ModelPassContract : IGpuRenderPassContract<ModelPassRequest, ModelPassResult>
{
    public static ModelPassContract Instance { get; } = new();
    private ModelPassContract() { }
    public string Id => "lumyte.model.render";
    public int Version => 1;
    public ModelPassRequest Snapshot(ModelPassRequest request) => request ?? throw new ArgumentNullException(nameof(request));
    public ModelPassResult Declare(GpuPassDeclarationContext context, ModelPassRequest request)
    {
        var color = request.Color.Description; var depth = request.Depth.Description;
        if (color.Format != GpuFormat.Rgba16Float || depth.Format != GpuFormat.D32Float
            || color.Width != depth.Width || color.Height != depth.Height
            || new[] { color, depth }.Any(d => d.Dimension != GpuGraphTextureDimension.TwoD || d.SampleCount != 1 || d.MipLevelCount != 1 || d.DepthOrArrayLayers != 1))
        { throw new ArgumentException("Model rendering requires equally sized, single-sample 2D RGBA16Float and D32Float targets.", nameof(request)); }
        if (!float.IsFinite(request.ClearDepth) || request.ClearDepth < 0 || request.ClearDepth > 1)
        { throw new ArgumentOutOfRangeException(nameof(request), "ClearDepth must be between zero and one."); }
        context.Write(request.Color); context.Write(request.Depth);
        context.ReadInput(request.Data, ModelRenderInputContract.Instance);
        return new(request.Color, request.Depth);
    }
}
public sealed class ModelRenderInputContract : IGpuGraphInputContract<ModelRenderSnapshot>
{
    public static ModelRenderInputContract Instance { get; } = new();
    private ModelRenderInputContract() { }
    public ModelRenderSnapshot Snapshot(ModelRenderSnapshot value) => value ?? throw new ArgumentNullException(nameof(value));
    public void Retain(GpuRenderInputRetentionContext context, ModelRenderSnapshot value)
    { if (value.Draws.Root is { } root) { context.ReadSnapshot(root, NodeContract.Instance); } }
    private sealed class NodeContract : IGpuGraphInputContract<ModelDrawNode>
    {
        internal static NodeContract Instance { get; } = new();
        public ModelDrawNode Snapshot(ModelDrawNode value) => value;
        public void Retain(GpuRenderInputRetentionContext context, ModelDrawNode value)
        {
            if (value.Left is { } left) { context.ReadSnapshot(left, this); }
            if (value.Right is { } right) { context.ReadSnapshot(right, this); }
            var draw = value.Item; var g = draw.Geometry; var v = g.Vertices;
            context.ReadUpload(g); context.ReadUpload(draw.Material); context.ReadUpload(v.Positions);
            foreach (var texture in draw.Material.Textures)
            { if (texture is not null) { context.ReadUpload(texture.Texture.Image); } }
            if (v.Normals is { } n) { context.ReadUpload(n); }
            if (v.Tangents is { } t) { context.ReadUpload(t); }
            if (v.SkinInfluences is { } s) { context.ReadUpload(s); }
            if (g.Indices is { } i) { context.ReadUpload(i); }
            foreach (var uv in v.TexCoords) { context.ReadUpload(uv.Values); }
            foreach (var c in v.Colors) { context.ReadUpload(c.Values); }
            foreach (var m in g.MorphTargets)
            {
                context.ReadUpload(m);
                if (m.PositionDeltas is { } p) { context.ReadUpload(p); }
                if (m.NormalDeltas is { } normal) { context.ReadUpload(normal); }
                if (m.TangentDeltas is { } tangent) { context.ReadUpload(tangent); }
            }
            if (draw.Deformation?.Skin is { } skin) { context.ReadUpload(skin); }
            if (draw.Deformation?.Morph is { } morph) { context.ReadUpload(morph); }
        }
    }
}
public static class ModelPassExtensions
{
    public static ModelPassResult AddModelPass(this GpuRenderGraph graph, string name, ModelPassRequest request)
        => graph.AddPass(name, ModelPassContract.Instance, request);
}
