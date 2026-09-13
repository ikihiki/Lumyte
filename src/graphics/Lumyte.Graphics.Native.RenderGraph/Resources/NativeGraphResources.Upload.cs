using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Native.RenderGraph;

public sealed partial class NativeGraphResources
{
    private async ValueTask<GpuGraphPackageRef> ImportPackageAsync(GpuResourceScope scope, GpuPackageUploadData data,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data); cancellationToken.ThrowIfCancellationRequested();
        if (data.Profile != new GpuUploadProfileId("images.sampled", 1))
        { throw new NotSupportedException($"Unsupported Native upload profile {data.Profile}."); }
        if (data.Buffers.Count != 0 || data.Exports.Any(export => export.Kind != GpuPackageUploadExportKind.Image))
        { throw new ArgumentException("The images.sampled profile contains image exports only.", nameof(data)); }
        List<GpuPackageResource> resources = [];
        for (int index = 0; index < data.Images.Count; index++)
        {
            GpuImageUploadData image = data.Images[index];
            if (image.Encoding != GpuImageColorEncoding.Linear || image.AlphaMode == GpuImageAlphaMode.Straight
                || image.Description.Format is not (GpuFormat.Rgba8Unorm or GpuFormat.Bgra8Unorm))
            { throw new NotSupportedException("images.sampled requires prepared linear premultiplied or opaque RGBA pixels."); }
            NativeGpuTextureDescription description = Describe(image.Description, NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.CopyDestination);
            List<GpuTextureUpload> uploads = [];
            foreach (GpuImageSubresourceData subresource in image.Subresources)
            {
                uint width = Math.Max(1, description.Width >> (int)subresource.MipLevel);
                uint height = Math.Max(1, description.Height >> (int)subresource.MipLevel);
                uint depth = Math.Max(1, description.Depth >> (int)subresource.MipLevel);
                ulong rowBytes = checked(width * 4ul), rowPitch = Align(rowBytes, 256), imagePitch = checked(rowPitch * height);
                ulong sourceRowStride = subresource.RowStride == 0 ? rowBytes : subresource.RowStride;
                ulong sourceSliceStride = subresource.SliceStride == 0 ? checked(sourceRowStride * height) : subresource.SliceStride;
                byte[] bytes = new byte[checked((int)(imagePitch * depth))];
                for (uint slice = 0; slice < depth; slice++)
                {
                    for (uint row = 0; row < height; row++)
                    {
                        int source = checked((int)(slice * sourceSliceStride + row * sourceRowStride));
                        subresource.Data.Span.Slice(source, checked((int)rowBytes)).CopyTo(bytes.AsSpan(checked((int)(slice * imagePitch + row * rowPitch))));
                    }
                }
                uploads.Add(new(bytes, new(subresource.MipLevel, NativeGpuTextureAspect.Color, subresource.ArrayLayer, 1,
                    default, new(width, height, depth), rowPitch, imagePitch), height, rowBytes));
            }
            resources.Add(new GpuPackageTexture($"image-{index}", description, uploads));
        }
        GpuPackagePlan plan = new(resources, data.Exports.Select(export => new GpuPackageExport(export.Name, $"image-{export.Index}")));
        await Work.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GpuPackageRef package = await scope.ImportPackageAsync(plan, cancellationToken).ConfigureAwait(false);
            lock (Sync)
            {
                Dictionary<string, GpuGraphResourceRef> exports = [];
                foreach (GpuPackageUploadExport export in data.Exports)
                { exports.Add(export.Name, Wrap(package.GetExport<GpuTextureRef>(export.Name), data.Images[export.Index].Description)); }
                GpuGraphPackageRef result = new(runtimeId, Guid.NewGuid(), exports); packages.Add(result, package); return result;
            }
        }
        finally { Work.Release(); }
    }
    private static ulong Align(ulong value, ulong alignment) => checked((value + alignment - 1) / alignment * alignment);
}
