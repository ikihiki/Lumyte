using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.UI;
using Lumyte.UI.Media;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Media.Compose;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Exercises borrowed media through real immutable Drawing2D recordings.</summary>
public sealed class UiMediaRecordingTests
{
    /// <summary>Checks centered placement and the default body clip for every stretch mode.</summary>
    /// <param name="stretch">The stretch mode.</param>
    /// <param name="x">The expected horizontal origin.</param>
    /// <param name="y">The expected vertical origin.</param>
    /// <param name="width">The expected width.</param>
    /// <param name="height">The expected height.</param>
    [Theory]
    [InlineData(ImageStretch.None, 5, 10, 20, 10)]
    [InlineData(ImageStretch.Fill, 0, 0, 30, 30)]
    [InlineData(ImageStretch.Uniform, 0, 7.5f, 30, 15)]
    [InlineData(ImageStretch.UniformToFill, -15, 0, 60, 30)]
    public void StretchRecordsSourceAndCenteredDestination(ImageStretch stretch, float x, float y, float width, float height)
    {
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var sampler = new DispatchSampler();
        PreparedImage source = Source(view, sampler);
        using var surface = new UiSurface(Image(source: source, stretch: stretch));
        surface.SetViewport(new(30, 30));
        DrawImageCommand2D command = Assert.IsType<DrawImageCommand2D>(Assert.Single(Record(surface).Commands));
        Assert.Same(source.Image, command.Image);
        Assert.Equal(source.Source, command.Source);
        Assert.Equal(new Rect2D(x, y, width, height), command.Destination);
        Assert.Equal(2, command.State.Clip.Elements.Count);
    }

    /// <summary>Checks a single tight axis preserves intrinsic aspect and clipping is explicitly removable.</summary>
    [Fact]
    public void SingleAxisConstraintPreservesAspectAndClipCanBeDisabled()
    {
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var sampler = new DispatchSampler();
        using var surface = new UiSurface(Panel()[Image(source: Source(view, sampler), width: 40, clipToBounds: false)]);
        surface.SetViewport(new(100, 100));
        DrawImageCommand2D command = Assert.IsType<DrawImageCommand2D>(Assert.Single(Record(surface).Commands));
        Assert.Equal(new Vector2(40, 20), surface.RootElement.Children[0].Size);
        Assert.Equal(new Rect2D(0, 0, 40, 20), command.Destination);
        Assert.Single(command.State.Clip.Elements);
    }

    /// <summary>Checks absent and zero-area images never produce commands.</summary>
    [Fact]
    public void UnavailableImageUsesPlaceholderWithoutDrawing()
    {
        using var surface = new UiSurface(Panel()[Image(placeholderSize: new Vector2(12, 7))]);
        surface.SetViewport(new(100, 100));
        Assert.Empty(Record(surface).Commands);
        Assert.Equal(new Vector2(12, 7), surface.RootElement.Children[0].Size);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var sampler = new DispatchSampler();
        surface.Rebuild(Panel()[Image(source: Source(view, sampler), width: 0, height: 10)]);
        Assert.Empty(Record(surface).Commands);
    }

    /// <summary>Checks image icons record the actual image under uniform placement and caller affine transforms.</summary>
    [Fact]
    public void ImageIconRecordsActualBorrowedImage()
    {
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var sampler = new DispatchSampler();
        PreparedImage source = Source(view, sampler);
        using var surface = new UiSurface(Icon(source: IconSource.Image(source), size: 30));
        surface.SetViewport(new(30, 30));
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Scale(new(2, 3));
        surface.Record(canvas);
        DrawImageCommand2D command = Assert.IsType<DrawImageCommand2D>(Assert.Single(canvas.End().Commands));
        Assert.Same(source.Image, command.Image);
        Assert.Equal(new Rect2D(0, 0, 20, 10), command.Destination);
        Assert.Equal(new Vector2(0, 22.5f), Vector2.Transform(Vector2.Zero, command.State.Transform));
        Assert.Equal(new Vector2(60, 67.5f), Vector2.Transform(new(20, 10), command.State.Transform));
    }

    /// <summary>Checks old direct and captured recordings borrow the original resources across replacement and unmount.</summary>
    [Fact]
    public void ReplacementUnmountAndSnapshotDisposalNeverDisposeBorrowedOwner()
    {
        var lifetime = new List<string>();
        using var texture = new DispatchTexture { Lifetime = lifetime };
        using IGraphicsTextureView view = texture.CreateView();
        using var sampler = new DispatchSampler();
        PreparedImage source = Source(view, sampler);
        using var surface = new UiSurface(Image(source: source));
        surface.SetViewport(new(30, 30));
        DrawList2D direct = Record(surface);
        using UiDrawingScene2D adapter = surface.CreateDrawingScene();
        adapter.Update();
        using DrawingSnapshot2D capture = adapter.Scene.CaptureSnapshot();
        surface.Rebuild(Image());
        adapter.Update();
        Assert.Empty(Record(surface).Commands);
        surface.Dispose();
        adapter.Dispose();
        DrawImageCommand2D historical = Assert.IsType<DrawImageCommand2D>(Assert.Single(((IDrawingSceneSource2D)capture).Entries.SelectMany(entry => entry.Content.Commands)));
        Assert.Same(source.Image, historical.Image);
        Assert.Same(source.Image, Assert.IsType<DrawImageCommand2D>(Assert.Single(direct.Commands)).Image);
        capture.Dispose();
        Assert.False(texture.Disposed);
        Assert.Empty(lifetime);
    }

    /// <summary>Checks source texel bounds reject invalid values before a UI tree is published.</summary>
    /// <param name="x">Source origin.</param>
    /// <param name="width">Source width.</param>
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(15, 2)]
    [InlineData(0, 0)]
    [InlineData(0, float.NaN)]
    [InlineData(float.PositiveInfinity, 1)]
    public void InvalidSourceRangeIsRejected(float x, float width)
    {
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var sampler = new DispatchSampler();
        Assert.Throws<ArgumentOutOfRangeException>(() => new PreparedImage(new(view, sampler), new(20, 10), new(x, 0, width, 1)));
    }

    /// <summary>Checks a media recording remains inside a single group-alpha boundary without flattening its image.</summary>
    [Fact]
    public void GroupAlphaWrapsActualImageCommands()
    {
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var sampler = new DispatchSampler();
        PreparedImage source = Source(view, sampler);
        using var surface = new UiSurface(Image(source: source));
        surface.SetViewport(new(30, 30));
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.BeginLayer(new() { ContentBounds = new(0, 0, 30, 30), Opacity = 0.5f });
        surface.Record(canvas);
        canvas.EndLayer();
        DrawList2D list = canvas.End().Commands;
        Assert.Equal(3, list.Commands.Count);
        BeginLayerCommand2D layer = Assert.IsType<BeginLayerCommand2D>(list.Commands[0]);
        Assert.Equal(0.5f, layer.Options.Opacity);
        Assert.Same(source.Image, Assert.IsType<DrawImageCommand2D>(list.Commands[1]).Image);
        Assert.IsType<EndLayerCommand2D>(list.Commands[2]);
    }

    /// <summary>Checks a failed image batch returns predecessors without disposing the image or replacing the UI tree.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task PreparationFailureLeavesBorrowedImageAndTreeReusableAsync()
    {
        var lifetime = new List<string>();
        using var texture = new DispatchTexture { Lifetime = lifetime };
        using IGraphicsTextureView view = texture.CreateView();
        using var sampler = new DispatchSampler();
        PreparedImage source = Source(view, sampler);
        using var surface = new UiSurface(Image(source: source));
        surface.SetViewport(new(30, 30));
        UiElement root = surface.RootElement;
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 1, 1), new Color2D(1, 0, 0, 1));
        surface.Record(canvas);
        DrawList2D recording = canvas.End();
        bool fail = true;
        var strategies = new DispatchRenderers
        {
            OnPreparing = name =>
            {
                if (fail && name == nameof(DrawImageCommand2D))
                {
                    throw new ArithmeticException("Injected image preparation failure.");
                }
            },
        };
        using var renderer = new Renderer2D(new DispatchDevice(), strategies.CreateTable());
        await Assert.ThrowsAsync<ArithmeticException>(async () => await renderer.PrepareAsync(recording, new() { View = view }));
        Assert.Equal(1, Assert.Single(strategies.Batches).DisposeCount);
        Assert.Same(root, surface.RootElement);
        Assert.Same(source.Image, Assert.IsType<DrawImageCommand2D>(Assert.Single(Record(surface).Commands)).Image);
        Assert.Empty(lifetime);
        fail = false;
        using PreparedDraw2D retry = await renderer.PrepareAsync(recording, new() { View = view });
        retry.Dispose();
        Assert.Empty(lifetime);
    }

    private static PreparedImage Source(IGraphicsTextureView view, IGraphicsSampler sampler) => new(new(view, sampler), new(20, 10), new(2, 3, 10, 5));

    private static DrawList2D Record(UiSurface surface)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        return canvas.End();
    }
}
