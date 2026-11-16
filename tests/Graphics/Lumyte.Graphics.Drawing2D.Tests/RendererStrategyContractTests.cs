using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks common public renderer admission without allocating native resources.</summary>
public sealed class RendererStrategyContractTests
{
    /// <summary>All concrete strategies expose the exact borrowed device without eager GPU allocation.</summary>
    [Fact]
    public void ConstructorsBorrowDeviceAndAllocateLazily()
    {
        var device = new DispatchDevice();
        using IRenderer2D standard = Renderer2D.CreateDefault(device);
        using IRenderer2D sparse = new SparseStripRenderer2D(device);
        using IRenderer2D compute = new ComputeRenderer2D(device);
        foreach (IRenderer2D renderer in new[] { standard, sparse, compute })
        {
            Assert.Same(device, renderer.Device);
            Assert.Equal(default, renderer.RetentionStatistics);
            Assert.IsAssignableFrom<IDrawingPassRenderer2D>(renderer);
        }
    }

    /// <summary>A nonpositive budget is rejected at construction without silently changing algorithms.</summary>
    /// <param name="bytes">The invalid byte count.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidBudget(long bytes)
    {
        var device = new DispatchDevice();
        var options = new DrawingRendererOptions2D { MaxWorkingBytes = bytes };
        Assert.Throws<ArgumentOutOfRangeException>(() => Renderer2D.CreateDefault(device, options));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SparseStripRenderer2D(device, options));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ComputeRenderer2D(device, options));
    }

    /// <summary>Disposal rejects descriptor-only preparation before allocating resources.</summary>
    /// <returns>The completed rejection assertions.</returns>
    [Fact]
    public async Task DisposedStrategiesRejectNewGraphPreparationAsync()
    {
        var device = new DispatchDevice();
        var canvas = new Canvas2D();
        canvas.Begin();
        DrawList2D content = canvas.End();
        using var arguments = new DrawingArgumentPlan2D(device);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled } };
        foreach (IRenderer2D renderer in new IRenderer2D[] { Renderer2D.CreateDefault(device), new SparseStripRenderer2D(device), new ComputeRenderer2D(device) })
        {
            renderer.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await ((IDrawingPassRenderer2D)renderer).PreparePassesAsync(content, target, arguments, default));
        }
    }

    /// <summary>Legacy constructors preserve custom strategy dispatch through the common contract.</summary>
    [Fact]
    public void StandardPreservesFiveAndSixParameterConstructors()
    {
        Type renderer = typeof(Renderer2D);
        Assert.NotNull(renderer.GetConstructor([typeof(IGraphicDevice), typeof(DrawingCommandRenderers2D), typeof(DrawingResources2D), typeof(IDisposable), typeof(Action)]));
        Assert.NotNull(renderer.GetConstructor([typeof(IGraphicDevice), typeof(DrawingCommandRenderers2D), typeof(DrawingResources2D), typeof(IDisposable), typeof(Action), typeof(DrawingRetentionOptions2D)]));
    }
}
