using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks opt-in live allocation ownership across the public graph preparation boundary.</summary>
public sealed class LiveImageResourceTests
{
    /// <summary>The returned graph owner pins an allocation independently of the CPU recording and renderer.</summary>
    /// <returns>The preparation lifetime verification.</returns>
    [Fact]
    public async Task GraphPreparationPinsLiveAllocationUntilOwnerDisposalAsync()
    {
        var device = new ProfilingDevice();
        using var texture = new DispatchTexture();
        Image2D image = new(texture.CreateView(), new DispatchSampler());
        ImageResourceRegistry2D.Resource resource = ImageResourceRegistry2D.Register(image);
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.DrawImage(image, new(0, 0, 1, 1), new(0, 0, 1, 1));
        RenderGraphDrawList2D recording = canvas.End();
        using var graph = new Graph(device);
        var bindings = new DrawingGraphBindings2D(graph);
        bindings.BindTexture(texture, graph.ImportTexture(texture, TextureState.Sampled, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead)));
        var description = new TextureDesc { Width = 16, Height = 16, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled };
        RenderGraphTexture target = graph.CreateTexture(description);
        var strategies = new DispatchRenderers();
        using var renderer = new RenderGraphRenderer2D(strategies.CreateTable(), recording, new() { Texture = target, Description = description }, bindings);
        using PreparedDrawingPasses2D prepared = await graph.Append2D(renderer);
        Assert.True(resource.IsReferenced);
        Assert.Null(resource.TryAcquireWrite());
        renderer.Dispose();
        graph.Dispose();
        Assert.True(resource.IsReferenced);
        prepared.Dispose();
        Assert.False(resource.IsReferenced);
        using ImageResourceRegistry2D.Resource.WriteLease writer = resource.TryAcquireWrite()!;
        Assert.NotNull(writer);
    }

    /// <summary>A racing writer is rejected before image binding lookup, pass append or custom preparation.</summary>
    /// <param name="retire">Whether the live producer invalidates this allocation permanently.</param>
    /// <returns>The failed graph admission check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GraphRejectsWritingOrRetiredLiveDescriptorAsync(bool retire)
    {
        var device = new ProfilingDevice();
        Image2D image = new(new DispatchTexture().CreateView(), new DispatchSampler());
        ImageResourceRegistry2D.Resource resource = ImageResourceRegistry2D.Register(image);
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.DrawImage(image, new(0, 0, 1, 1), new(0, 0, 1, 1));
        using ImageResourceRegistry2D.Resource.WriteLease writer = resource.TryAcquireWrite()!;
        if (retire)
        {
            writer.Invalidate();
            writer.Dispose();
        }

        using var graph = new Graph(device);
        var description = new TextureDesc { Width = 16, Height = 16, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled };
        RenderGraphTexture target = graph.CreateTexture(description);
        var strategies = new DispatchRenderers();
        using var renderer = new RenderGraphRenderer2D(strategies.CreateTable(), canvas.End(), new() { Texture = target, Description = description }, new DrawingGraphBindings2D(graph));
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => graph.Append2D(renderer).AsTask());
        Assert.Contains("live image", failure.Message, StringComparison.Ordinal);
        Assert.Empty(strategies.Commands);
        Assert.False(resource.IsReferenced);
    }
}
