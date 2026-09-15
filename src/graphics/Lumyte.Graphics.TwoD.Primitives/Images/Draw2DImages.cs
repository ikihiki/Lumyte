using System.Numerics;

using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.TwoD;

public sealed class Draw2DImageSource
{
    public Draw2DImageSource(GpuImageUploadData upload) => Upload = upload ?? throw new ArgumentNullException(nameof(upload));
    public Draw2DImageSource(GpuRenderGraphTexture texture) => Texture = texture ?? throw new ArgumentNullException(nameof(texture));
    public GpuImageUploadData? Upload { get; }
    public GpuRenderGraphTexture? Texture { get; }
    public GpuGraphTextureDescription Description => Upload?.Description ?? Texture!.Description;
    public static implicit operator Draw2DImageSource(GpuImageUploadData upload) => new(upload);
    public static implicit operator Draw2DImageSource(GpuRenderGraphTexture texture) => new(texture);
}

public enum Draw2DImageFilter { Linear, Nearest }
public enum Draw2DImageExtend { Clamp, Repeat, Mirror }
public readonly record struct Draw2DImageSampling(Draw2DImageFilter Filter = Draw2DImageFilter.Linear, Draw2DImageExtend ExtendX = Draw2DImageExtend.Clamp, Draw2DImageExtend ExtendY = Draw2DImageExtend.Clamp);
public sealed record Draw2DLayerMask(Draw2DImageSource Image, Rect Destination);
public sealed record ShadowOptions(Vector2 Offset, Color Color, float BlurRadius = 0);
public sealed record Draw2DLayerOptions(Rect? Bounds = null, float Opacity = 1, CompositeMode CompositeMode = CompositeMode.SourceOver, Draw2DLayerMask? Mask = null, float BlurRadius = 0, ShadowOptions? Shadow = null);
