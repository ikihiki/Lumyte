using System.Numerics;

using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Passes;

public sealed class ImageSamplingInputContract : IGpuGraphInputContract<ImageSampling>
{
    public static ImageSamplingInputContract Instance { get; } = new();
    private ImageSamplingInputContract() { }
    public ImageSampling Snapshot(ImageSampling value) => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));
    public void Retain(GpuRenderInputRetentionContext context, ImageSampling snapshot) { }
}
public sealed class BlurRadiusInputContract : IGpuGraphInputContract<int>
{
    public static BlurRadiusInputContract Instance { get; } = new();
    private BlurRadiusInputContract() { }
    public int Snapshot(int value) => value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    public void Retain(GpuRenderInputRetentionContext context, int snapshot) { }
}
public sealed class CompositeOpacityInputContract : IGpuGraphInputContract<float>
{
    public static CompositeOpacityInputContract Instance { get; } = new();
    private CompositeOpacityInputContract() { }
    public float Snapshot(float value) => float.IsFinite(value) && value is >= 0 and <= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    public void Retain(GpuRenderInputRetentionContext context, float snapshot) { }
}
public sealed class ExposureInputContract : IGpuGraphInputContract<float>
{
    public static ExposureInputContract Instance { get; } = new();
    private ExposureInputContract() { }
    public float Snapshot(float value) => float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));
    public void Retain(GpuRenderInputRetentionContext context, float snapshot) { }
}
public sealed class LinearColorInputContract : IGpuGraphInputContract<Vector4>
{
    public static LinearColorInputContract Instance { get; } = new();
    private LinearColorInputContract() { }
    public Vector4 Snapshot(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z)
        && float.IsFinite(value.W) && value.W is >= 0 and <= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    public void Retain(GpuRenderInputRetentionContext context, Vector4 snapshot) { }
}
