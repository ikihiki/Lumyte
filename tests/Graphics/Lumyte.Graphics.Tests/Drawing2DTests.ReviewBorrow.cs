using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks every structural borrow path after the originating common renderer is disposed.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks the Vulkan layer compositor retains a live Graph borrow.</summary>
    /// <returns>The asynchronous pixel check.</returns>
    [GpuFact]
    public async Task VulkanReviewLayerBorrowAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckReviewStructuralBorrowAsync(device, false);
    }

    /// <summary>Checks the Vulkan backdrop snapshot and replacement retain a live Graph borrow.</summary>
    /// <returns>The asynchronous pixel check.</returns>
    [GpuFact]
    public async Task VulkanReviewBackdropBorrowAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckReviewStructuralBorrowAsync(device, true);
    }

    /// <summary>Checks the native wgpu layer compositor retains a live Graph borrow.</summary>
    /// <returns>The asynchronous pixel check.</returns>
    [GpuFact]
    public async Task WgpuReviewLayerBorrowAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckReviewStructuralBorrowAsync(device, false);
    }

    /// <summary>Checks the native wgpu backdrop compositor retains a live Graph borrow.</summary>
    /// <returns>The asynchronous pixel check.</returns>
    [GpuFact]
    public async Task WgpuReviewBackdropBorrowAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckReviewStructuralBorrowAsync(device, true);
    }

    private static async Task CheckReviewStructuralBorrowAsync(IGraphicDevice device, bool backdrop)
    {
        void Draw(RenderGraphCanvas2D canvas)
        {
            if (backdrop)
            {
                canvas.FillRect(new(0, 0, 4, 2), new SRgba<float>(1, 0, 0, 0.5f));
                canvas.DrawBackdropEffect(new() { OutputBounds = new(0, 0, 4, 2), Effects = graph => graph.AppendOpacity(graph.Source, 0.5f) });
            }
            else
            {
                canvas.BeginLayer(new() { ContentBounds = new(0, 0, 4, 2), Opacity = 0.5f });
                canvas.FillRect(new(0, 0, 4, 2), new SRgba<float>(1, 0, 0, 1));
                canvas.EndLayer();
            }
        }

        Vector4[] reference = await RenderAsync(device, 4, 2, Draw, useDefaultResources: true);
        Vector4[] actual = await RenderAsync(device, 4, 2, Draw, useDefaultResources: true, composed: true);
        Assert.Equal(reference, actual);
        AssertPixel(actual, 4, 0, 0, backdrop ? new(0.25f, 0, 0, 0.25f) : new(0.5f, 0, 0, 0.5f));
    }
}
