using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Passes;

internal static class ImageFilterContract
{
    internal static void RequireImage(GpuRenderGraphTexture image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var d = image.Description;
        if (d.Dimension != GpuGraphTextureDimension.TwoD || d.DepthOrArrayLayers != 1 || d.MipLevelCount != 1 || d.SampleCount != 1
            || d.Format is not (GpuFormat.Rgba8Unorm or GpuFormat.Bgra8Unorm or GpuFormat.Rgba16Float))
        { throw new NotSupportedException("Image filtering version 1 requires a linear RGBA8, BGRA8 or RGBA16Float 2D image with one mip, layer and sample."); }
    }
    internal static void RequirePair(GpuRenderGraphTexture source, GpuRenderGraphTexture target, bool sameExtent)
    {
        RequireImage(source);
        RequireImage(target);
        if (source == target)
        { throw new ArgumentException("Image filtering requires distinct source and target resources."); }
        if (sameExtent && (source.Description.Width != target.Description.Width || source.Description.Height != target.Description.Height))
        { throw new ArgumentException("Composite requires matching source and target extents."); }
    }
}

public sealed class BlitPassContract : IGpuRenderPassContract<BlitPassRequest, TexturePassResult>
{
    public static BlitPassContract Instance { get; } = new();
    private BlitPassContract() { }
    public string Id => "lumyte.image.blit";
    public int Version => 1;
    public BlitPassRequest Snapshot(BlitPassRequest request) => request ?? throw new ArgumentNullException(nameof(request));
    public TexturePassResult Declare(GpuPassDeclarationContext context, BlitPassRequest request)
    {
        ImageFilterContract.RequirePair(request.Source, request.Target, false);
        context.Read(request.Source);
        context.Write(request.Target);
        context.ReadInput(request.Filter, ImageSamplingInputContract.Instance);
        return new(request.Target);
    }
}
public sealed class BlurPassContract : IGpuRenderPassContract<BlurPassRequest, BlurPassResult>
{
    public static BlurPassContract Instance { get; } = new();
    private BlurPassContract() { }
    public string Id => "lumyte.image.blur";
    public int Version => 1;
    public BlurPassRequest Snapshot(BlurPassRequest request) => request ?? throw new ArgumentNullException(nameof(request));
    public BlurPassResult Declare(GpuPassDeclarationContext context, BlurPassRequest request)
    {
        ImageFilterContract.RequireImage(request.Source);
        var target = context.CreateTexture("color", request.Source.Description with { Format = GpuFormat.Rgba16Float });
        context.Read(request.Source);
        context.Write(target);
        context.ReadInput(request.Radius, BlurRadiusInputContract.Instance);
        return new(target);
    }
}
public sealed class CompositePassContract : IGpuRenderPassContract<CompositePassRequest, TexturePassResult>
{
    public static CompositePassContract Instance { get; } = new();
    private CompositePassContract() { }
    public string Id => "lumyte.image.composite";
    public int Version => 1;
    public CompositePassRequest Snapshot(CompositePassRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Layers);
        return request with { Layers = Array.AsReadOnly(request.Layers.ToArray()) };
    }
    public TexturePassResult Declare(GpuPassDeclarationContext context, CompositePassRequest request)
    {
        ImageFilterContract.RequireImage(request.Target);
        if (!Enum.IsDefined(request.Content))
        { throw new ArgumentOutOfRangeException(nameof(request)); }
        foreach (var layer in request.Layers)
        {
            ArgumentNullException.ThrowIfNull(layer);
            ImageFilterContract.RequirePair(layer.Source, request.Target, true);
            context.Read(layer.Source);
            context.ReadInput(layer.Opacity, CompositeOpacityInputContract.Instance);
        }
        context.ReadInput(request.ClearColor, LinearColorInputContract.Instance);
        if (request.Content == TargetContent.Preserve)
        { context.ReadWrite(request.Target); }
        else
        { context.Write(request.Target); }
        return new(request.Target);
    }
}
public sealed class ToneMapPassContract : IGpuRenderPassContract<ToneMapPassRequest, ToneMapPassResult>
{
    public static ToneMapPassContract Instance { get; } = new();
    private ToneMapPassContract() { }
    public string Id => "lumyte.image.tonemap";
    public int Version => 1;
    public ToneMapPassRequest Snapshot(ToneMapPassRequest request) => request ?? throw new ArgumentNullException(nameof(request));
    public ToneMapPassResult Declare(GpuPassDeclarationContext context, ToneMapPassRequest request)
    {
        ImageFilterContract.RequireImage(request.Source);
        var target = context.CreateTexture("color", request.Source.Description with { Format = GpuFormat.Rgba16Float });
        context.Read(request.Source);
        context.Write(target);
        context.ReadInput(request.ExposureStops, ExposureInputContract.Instance);
        return new(target);
    }
}
