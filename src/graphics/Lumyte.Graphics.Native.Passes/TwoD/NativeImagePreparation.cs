using System.Buffers.Binary;

using Lumyte.Graphics.Native.RenderGraph;
using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Native.Passes;

public sealed partial class NativeDraw2DPass
{
    private async ValueTask<NativePassTexture> ImportImage(BuildState build, Draw2DImageSource source)
    {
        if (source.Texture is { } logical)
        { return build.Context.ImportTexture(logical); }
        GpuImageUploadData image = source.Upload!;
        if (!images.TryGetValue(image, out CachedImage? cached))
        {
            GpuResourceScope scope = services.Resources.CreateScope();
            try
            {
                NativeGpuTextureDescription description = new(NativeGpuTextureDimension.TwoD, image.Description.Width,
                    image.Description.Height, 1, 1, 1, 1, GpuFormat.Rgba16Float, NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.CopyDestination);
                GpuTextureRef texture = scope.CreateTexture(description);
                GpuTextureUpload upload = PrepareImage(image);
                await services.Resources.UploadTextureAsync(texture, upload, build.Cancellation);
                cached = new(scope, texture, description);
                images.Add(image, cached);
                imageOrder.AddLast(image);
                if (imageOrder.Count > 64)
                { var oldest = imageOrder.First!.Value; imageOrder.RemoveFirst(); images.Remove(oldest, out var retired); retired!.Scope.Dispose(); }
            }
            catch { scope.Dispose(); throw; }
        }
        else
        { imageOrder.Remove(image); imageOrder.AddLast(image); }
        return build.Context.ImportTexture(cached.Texture, cached.Description);
    }

    private static GpuTextureUpload PrepareImage(GpuImageUploadData image)
    {
        GpuImageSubresourceData subresource = image.Subresources.Single(data => data.MipLevel == 0 && data.ArrayLayer == 0);
        uint width = image.Description.Width, height = image.Description.Height;
        ulong rowPitch = (width * 8ul + 255) / 256 * 256;
        byte[] bytes = new byte[checked((int)(rowPitch * height))];
        int sourcePixelBytes = image.Description.Format switch
        {
            GpuFormat.R8Unorm => 1,
            GpuFormat.Rg8Unorm => 2,
            GpuFormat.Rgba16Float => 8,
            GpuFormat.Rgba8Unorm or GpuFormat.Bgra8Unorm or GpuFormat.Rgba8UnormSrgb or GpuFormat.Bgra8UnormSrgb => 4,
            _ => throw new NotSupportedException($"The Native 2D image format {image.Description.Format} has no color conversion."),
        };
        ulong sourcePitch = subresource.RowStride == 0 ? width * (ulong)sourcePixelBytes : subresource.RowStride;
        for (uint y = 0; y < height; y++)
        {
            for (uint x = 0; x < width; x++)
            {
                ReadOnlySpan<byte> pixel = subresource.Data.Span.Slice(checked((int)(y * sourcePitch + x * (ulong)sourcePixelBytes)), sourcePixelBytes);
                float r, g, b, a;
                if (sourcePixelBytes == 8)
                {
                    r = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(pixel));
                    g = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(pixel[2..]));
                    b = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(pixel[4..]));
                    a = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(pixel[6..]));
                }
                else
                {
                    r = pixel[0] / 255f;
                    g = sourcePixelBytes > 1 ? pixel[1] / 255f : 0;
                    b = sourcePixelBytes > 2 ? pixel[2] / 255f : 0;
                    a = sourcePixelBytes > 3 ? pixel[3] / 255f : 1;
                    if (image.Description.Format is GpuFormat.Bgra8Unorm or GpuFormat.Bgra8UnormSrgb)
                    { (r, b) = (b, r); }
                }
                if (image.AlphaMode == GpuImageAlphaMode.Opaque)
                { a = 1; }
                if (image.Encoding == GpuImageColorEncoding.Srgb)
                {
                    if (image.AlphaMode == GpuImageAlphaMode.Premultiplied && a > 0)
                    { r /= a; g /= a; b /= a; }
                    r = Linear(r);
                    g = Linear(g);
                    b = Linear(b);
                    r *= a;
                    g *= a;
                    b *= a;
                }
                else if (image.AlphaMode == GpuImageAlphaMode.Straight && image.Encoding != GpuImageColorEncoding.Data)
                { r *= a; g *= a; b *= a; }
                Span<byte> output = bytes.AsSpan(checked((int)(y * rowPitch + x * 8ul)), 8);
                BinaryPrimitives.WriteUInt16LittleEndian(output, BitConverter.HalfToUInt16Bits((Half)r));
                BinaryPrimitives.WriteUInt16LittleEndian(output[2..], BitConverter.HalfToUInt16Bits((Half)g));
                BinaryPrimitives.WriteUInt16LittleEndian(output[4..], BitConverter.HalfToUInt16Bits((Half)b));
                BinaryPrimitives.WriteUInt16LittleEndian(output[6..], BitConverter.HalfToUInt16Bits((Half)a));
            }
        }
        return new(bytes, new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(width, height, 1), rowPitch, rowPitch * height), height, width * 8ul);
    }
    private static float Linear(float x) => x <= 0.04045f ? x / 12.92f : MathF.Pow((x + .055f) / 1.055f, 2.4f);
}
