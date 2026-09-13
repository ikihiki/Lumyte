using Lumyte.Graphics.Native.Resources.Tests;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Native.RenderGraph.Tests;

public sealed class NativeGraphResourcesTests
{
    [Fact]
    public async Task ZeroUploadStridesPreserveTightlyPackedRows()
    {
        TestResourceBackend backend = new();
        await using IGpuRenderRuntime runtime = await CreateRuntime(backend);
        using GpuGraphResourceScope scope = runtime.Resources.CreateScope();
        byte[] pixels = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255];

        GpuGraphPackageRef package = await scope.ImportPackageAsync(Package(pixels));

        Assert.Equal(pixels, Assert.Single(backend.Textures).Data);
        Assert.Equal(new GpuGraphTextureDescription(2, 2, GpuFormat.Rgba8Unorm), package.GetTexture("color").Description);
    }

    [Fact]
    public async Task ScopeDisposalDefersPendingUploadOwnershipUntilCompletion()
    {
        TestResourceBackend backend = new() { AutoComplete = false };
        IGpuRenderRuntime runtime = await CreateRuntime(backend);
        GpuGraphResourceScope scope = runtime.Resources.CreateScope();
        Task<GpuGraphPackageRef> upload = scope.ImportPackageAsync(Package(new byte[16])).AsTask();

        scope.Dispose();
        Task closing = runtime.DisposeAsync().AsTask();
        Assert.False(closing.IsCompleted);
        Assert.Empty(backend.Destroyed.OfType<TestResourceBackend.Texture>());
        backend.CompleteAll();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => upload);
        await closing;
        Assert.True(backend.Disposed);
    }

    private static ValueTask<IGpuRenderRuntime> CreateRuntime(TestResourceBackend backend)
        => new NativeRenderProvider("test", (_, _) => new(backend), new()).CreateAsync(new());
    private static GpuPackageUploadData Package(byte[] pixels) => new(new("package", 1), [],
        [new(new("image", 1), new(2, 2, GpuFormat.Rgba8Unorm), GpuImageColorEncoding.Linear, GpuImageAlphaMode.Premultiplied,
            [new(0, 0, 0, 0, pixels)])], [GpuPackageUploadExport.Image("color", 0)], new("images.sampled", 1));
}
