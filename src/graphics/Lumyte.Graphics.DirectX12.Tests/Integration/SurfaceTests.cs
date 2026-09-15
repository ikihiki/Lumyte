using Lumyte.Graphics.RenderGraph.Conformance;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
public sealed class SurfaceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "WindowPresentation")]
    public Task ReturnedSurfaceImagesAllowResize(bool present) => WindowConformance.RunAsync(window =>
    {
        using var backend = DirectX12Backend.Create(new() { EnableValidation = true });
        var surface = backend.CreateWindowSurface(window.Handle);
        try
        {
            var first = window.Complete(surface.AcquireAsync(160, 120).AsTask());
            window.Complete((present ? surface.PresentAsync(first) : surface.DiscardAsync(first)).AsTask());
            window.Resize(224, 144);
            var second = window.Complete(surface.AcquireAsync(224, 144).AsTask());
            Assert.Equal(224u, second.Description.Width);
            window.Complete(surface.DiscardAsync(second).AsTask());
        }
        finally { window.Complete(surface.DisposeAsync().AsTask()); }
    });
}
