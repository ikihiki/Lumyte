using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Portable.Resources.Tests.Unit.Management;

namespace Lumyte.Graphics.Portable.RenderGraph.Tests;

public sealed class PortableGraphResourcesTests
{
    [Fact]
    public async Task PreparedImageUploadPadsRowsAndPinsExportsAfterPackageRelease()
    {
        var backend = new ManagerTestBackend();
        var provider = new PortableRenderProvider("test", (_, _) => ValueTask.FromResult<IPortableGpuBackend>(backend), new());
        await using var runtime = (PortableRenderRuntime)await provider.CreateAsync(new());
        using GpuGraphResourceScope scope = runtime.Resources.CreateScope();
        byte[] pixels = [1, 2, 3, 255, 4, 5, 6, 255];
        var image = new GpuImageUploadData(new("image", 1), new(1, 2, GpuFormat.Rgba8Unorm),
            GpuImageColorEncoding.Linear, GpuImageAlphaMode.Opaque, [new(0, 0, 4, 8, pixels)]);

        GpuGraphPackageRef package = await scope.ImportPackageAsync(new(new("package", 1), [], [image],
            [GpuPackageUploadExport.Image("image", 0)], new("images.sampled", 1)));
        GpuGraphTextureRef reference = package.GetTexture("image");
        using GpuGraphResourcePin pin = runtime.Resources.Pin(reference);
        scope.Release(package); scope.Dispose(); runtime.Resources.Collect();
        var texture = Assert.IsType<ManagerTestBackend.Texture>(
            runtime.Resources.Manager.GetTextureHandle(runtime.Resources.ResolveTexture(reference)));

        Assert.Equal(pixels, texture.Bytes);
    }

    [Fact]
    public async Task UnknownUploadProfileIsRejectedBeforeResourceCreation()
    {
        var backend = new ManagerTestBackend();
        var provider = new PortableRenderProvider("test", (_, _) => ValueTask.FromResult<IPortableGpuBackend>(backend), new());
        await using var runtime = (PortableRenderRuntime)await provider.CreateAsync(new());
        using GpuGraphResourceScope scope = runtime.Resources.CreateScope();

        await Assert.ThrowsAsync<NotSupportedException>(() => scope.ImportPackageAsync(new(new("package", 1), [], [], [], new("unknown", 1))).AsTask());

        Assert.Empty(backend.Created);
    }

    [Fact]
    public async Task ClosingScopeDuringUploadDefersItsReleaseUntilTheUploadEnds()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        var provider = new PortableRenderProvider("test", (_, _) => ValueTask.FromResult<IPortableGpuBackend>(backend), new());
        await using var runtime = (PortableRenderRuntime)await provider.CreateAsync(new());
        using GpuGraphResourceScope scope = runtime.Resources.CreateScope();
        var image = new GpuImageUploadData(new("image", 1), new(1, 1, GpuFormat.Rgba8Unorm),
            GpuImageColorEncoding.Linear, GpuImageAlphaMode.Opaque, [new(0, 0, 4, 4, new byte[] { 1, 2, 3, 255 })]);
        Task<GpuGraphPackageRef> pending = scope.ImportPackageAsync(new(new("package", 1), [], [image],
            [GpuPackageUploadExport.Image("image", 0)], new("images.sampled", 1))).AsTask();

        scope.Dispose();
        Assert.False(pending.IsCompleted);
        backend.TestQueue.Complete(0);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        runtime.Resources.Collect();

        Assert.Equal(0, runtime.Resources.Manager.Statistics.ResourceCount);
    }
}
