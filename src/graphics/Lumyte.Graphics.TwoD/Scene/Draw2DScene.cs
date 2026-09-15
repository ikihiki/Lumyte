using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.TwoD;

/// <summary>Owning immutable semantic scene. Child snapshots are shared without copying their content.</summary>
public sealed class Draw2DScene
{
    private readonly Lazy<IReadOnlyList<GpuRenderGraphTexture>> logicalTextures;
    internal Draw2DScene(IEnumerable<Draw2DCommand> commands, float deviceScale)
    {
        if (!float.IsFinite(deviceScale) || deviceScale <= 0)
        { throw new ArgumentOutOfRangeException(nameof(deviceScale)); }
        DeviceScale = deviceScale;
        Commands = Array.AsReadOnly(commands.ToArray());
        var uploads = new HashSet<IGpuUploadData>(ReferenceEqualityComparer.Instance);
        var textures = new HashSet<GpuRenderGraphTexture>();
        var children = new HashSet<Draw2DScene>(ReferenceEqualityComparer.Instance);
        foreach (var command in Commands)
        {
            switch (command)
            {
                case Draw2DShapeCommand shape:
                    AddBrush(shape.Brush);
                    break;
                case Draw2DPathCommand path:
                    AddBrush(path.Brush);
                    break;
                case Draw2DGeometryCommand geometry:
                    AddBrush(geometry.Brush);
                    break;
                case Draw2DImageCommand image:
                    AddImage(image.Source);
                    break;
                case Draw2DDistanceFieldCommand distance:
                    uploads.Add(distance.Data);
                    uploads.Add(distance.Data.Image);
                    AddBrush(distance.Brush);
                    break;
                case Draw2DTextCommand text:
                    foreach (var upload in text.Data.Uploads)
                    { uploads.Add(upload); }
                    AddBrush(text.Brush);
                    break;
                case Draw2DLayerCommand layer:
                    children.Add(layer.Content);
                    if (layer.Options.Mask is { } mask)
                    { AddImage(mask.Image); }
                    break;
                case Draw2DSceneCommand scene:
                    children.Add(scene.Content);
                    break;
            }
        }
        OwnUploads = Array.AsReadOnly(uploads.ToArray());
        OwnTextures = Array.AsReadOnly(textures.ToArray());
        ChildScenes = Array.AsReadOnly(children.ToArray());
        logicalTextures = new(() => Array.AsReadOnly(OwnTextures.Concat(ChildScenes.SelectMany(child => child.LogicalTextures)).Distinct().ToArray()));

        void AddBrush(Brush brush)
        { if (brush is ImageBrush image) { AddImage(image.Source); } }
        void AddImage(Draw2DImageSource source)
        {
            if (source.Upload is { } upload)
            { uploads.Add(upload); }
            if (source.Texture is { } texture)
            { textures.Add(texture); }
        }
    }
    public float DeviceScale { get; }
    public IReadOnlyList<Draw2DCommand> Commands { get; }
    /// <summary>Direct immutable upload ownership; child ownership remains structurally shared.</summary>
    public IReadOnlyList<IGpuUploadData> OwnUploads { get; }
    public IReadOnlyList<GpuRenderGraphTexture> OwnTextures { get; }
    public IReadOnlyList<Draw2DScene> ChildScenes { get; }
    /// <summary>The fixed external texture set, materialized only when required for a constant pass declaration.</summary>
    public IReadOnlyList<GpuRenderGraphTexture> LogicalTextures => logicalTextures.Value;
}
