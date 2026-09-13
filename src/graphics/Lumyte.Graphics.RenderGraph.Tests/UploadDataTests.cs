namespace Lumyte.Graphics.RenderGraph.Tests;

public sealed class UploadDataTests
{
    [Fact]
    public void ImageSubresourceOwnsItsSourceBytes()
    {
        byte[] pixels = [1, 2, 3, 4];
        var image = new GpuImageSubresourceData(0, 0, 4, 4, pixels);

        pixels[0] = 9;

        Assert.Equal(1, image.Data.Span[0]);
    }

    [Fact]
    public void PackageOwnsExportAndImageCollections()
    {
        var image = new GpuImageUploadData(new("pixel", 1), new(1, 1, GpuFormat.Rgba8Unorm),
            GpuImageColorEncoding.Linear, GpuImageAlphaMode.Opaque, [new(0, 0, 4, 4, new byte[] { 1, 2, 3, 255 })]);
        var images = new[] { image };
        var exports = new[] { GpuPackageUploadExport.Image("original", 0) };
        var package = new GpuPackageUploadData(new("package", 1), [], images, exports, new("images.sampled", 1));

        images[0] = null!;
        exports[0] = GpuPackageUploadExport.Image("changed", 0);

        Assert.Same(image, Assert.Single(package.Images));
        Assert.Equal("original", Assert.Single(package.Exports).Name);
    }

    [Fact]
    public void CoreContractsHaveNoProviderOrHostingAssemblyDependencies()
    {
        var references = typeof(GpuRenderGraph).Assembly.GetReferencedAssemblies().Select(reference => reference.Name);

        Assert.DoesNotContain(references, name => name is not null &&
            (name.StartsWith("Lumyte.Graphics.Native", StringComparison.Ordinal)
            || name.StartsWith("Lumyte.Graphics.Portable", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal)));
    }
}
