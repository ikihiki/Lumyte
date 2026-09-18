using System.Numerics;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.ModelPreparation;

internal sealed record ModelImageKey(GpuImageUploadData Image, bool Color);
internal sealed record ModelImageLevel(uint Width, uint Height, byte[] Bytes, ulong Pitch);
internal sealed record ModelPreparedImage(ModelImageLevel[] Levels);
internal static class ModelImages
{
    internal static GpuImageUploadData White { get; } = new(new("lumyte.model.white", 0), new(1, 1, GpuFormat.Rgba8Unorm),
        GpuImageColorEncoding.Linear, GpuImageAlphaMode.Opaque, [new(0, 0, 4, 4, new byte[] { 255, 255, 255, 255 })]);
    internal static ModelPreparedImage Prepare(ModelImageKey key)
    {
        var image = key.Image; var d = image.Description;
        if (d.Dimension != GpuGraphTextureDimension.TwoD || d.DepthOrArrayLayers != 1 || d.SampleCount != 1)
        { throw new NotSupportedException("Model material images must be single-layer, single-sample 2D images."); }
        List<ModelImageLevel> levels = [];
        for (uint mip = 0, width = d.Width, height = d.Height; ; mip++, width = Math.Max(1, width / 2), height = Math.Max(1, height / 2))
        {
            var source = image.Subresources.SingleOrDefault(s => s.MipLevel == mip && s.ArrayLayer == 0);
            if (source is null)
            {
                if (mip == 0) { throw new ArgumentException("A model image must contain mip zero.", nameof(key)); }
                levels.Add(new(width,height,[],0));
                if (width == 1 && height == 1) { break; }
                continue;
            }
            var pixels = Decode(image, source, width, height, key.Color);
            ulong pitch = (width * 8ul + 255) & ~255ul;
            byte[] bytes = new byte[checked((int)(pitch * height))];
            for (uint y = 0; y < height; y++)
            {
                for (uint x = 0; x < width; x++)
                {
                    var p = pixels[y * width + x]; var output = bytes.AsSpan(checked((int)(y * pitch + x * 8)), 8);
                    BitConverter.TryWriteBytes(output, (Half)p.X); BitConverter.TryWriteBytes(output[2..], (Half)p.Y);
                    BitConverter.TryWriteBytes(output[4..], (Half)p.Z); BitConverter.TryWriteBytes(output[6..], (Half)p.W);
                }
            }
            levels.Add(new(width, height, bytes, pitch));
            if (width == 1 && height == 1) { break; }
        }
        return new(levels.ToArray());
    }
    private static Vector4[] Decode(GpuImageUploadData image, GpuImageSubresourceData source, uint width, uint height, bool color)
    {
        int pixelBytes = image.Description.Format switch
        {
            GpuFormat.R8Unorm => 1, GpuFormat.Rg8Unorm => 2, GpuFormat.Rgba16Float => 8,
            GpuFormat.Rgba8Unorm or GpuFormat.Bgra8Unorm or GpuFormat.Rgba8UnormSrgb or GpuFormat.Bgra8UnormSrgb => 4,
            _ => throw new NotSupportedException($"Model image format {image.Description.Format} is not supported."),
        };
        ulong pitch = source.RowStride == 0 ? width * (ulong)pixelBytes : source.RowStride;
        if (pitch < width * (ulong)pixelBytes) { throw new ArgumentException("Image row stride is too small.", nameof(source)); }
        var pixels = new Vector4[checked((int)(width * height))];
        for (uint y = 0; y < height; y++)
        {
            for (uint x = 0; x < width; x++)
            {
                var bytes = source.Data.Span.Slice(checked((int)(y * pitch + x * (ulong)pixelBytes)), pixelBytes);
                Vector4 p = pixelBytes == 8 ? new((float)BitConverter.ToHalf(bytes), (float)BitConverter.ToHalf(bytes[2..]),
                    (float)BitConverter.ToHalf(bytes[4..]), (float)BitConverter.ToHalf(bytes[6..]))
                    : new(bytes[0] / 255f, pixelBytes > 1 ? bytes[1] / 255f : 0, pixelBytes > 2 ? bytes[2] / 255f : 0, pixelBytes > 3 ? bytes[3] / 255f : 1);
                if (image.Description.Format is GpuFormat.Bgra8Unorm or GpuFormat.Bgra8UnormSrgb) { (p.X, p.Z) = (p.Z, p.X); }
                if (color)
                {
                    if (image.AlphaMode == GpuImageAlphaMode.Premultiplied)
                    { p = p.W > 0 ? new(p.X / p.W, p.Y / p.W, p.Z / p.W, p.W) : Vector4.Zero; }
                    if (image.Encoding == GpuImageColorEncoding.Srgb) { p = new(Linear(p.X), Linear(p.Y), Linear(p.Z), p.W); }
                    if (image.AlphaMode == GpuImageAlphaMode.Opaque) { p.W = 1; }
                }
                pixels[y * width + x] = p;
            }
        }
        return pixels;
    }
    private static float Linear(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
}
