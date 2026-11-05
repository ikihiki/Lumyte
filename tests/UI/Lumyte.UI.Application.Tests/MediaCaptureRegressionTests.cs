using System.Numerics;
using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using Lumyte.UI.Media;
using Lumyte.UI.Windowing;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Media.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks actual shared-flight preparation and amount-zero composition for resized media.</summary>
public sealed class MediaCaptureRegressionTests
{
    /// <summary>Checks the same image source retains its old endpoint drawing.</summary>
    /// <param name="stretch">The image placement policy.</param>
    /// <returns>The asynchronous verification.</returns>
    [Theory]
    [InlineData(ImageStretch.Uniform)]
    [InlineData(ImageStretch.UniformToFill)]
    [InlineData(ImageStretch.None)]
    public async Task ResizedImagePreservesOldDrawingAtFlightStartAsync(ImageStretch stretch)
    {
        using var texture = new Texture();
        using IGraphicsTextureView view = texture.CreateView();
        using var sampler = new Sampler();
        var source = new PreparedImage(new(view, sampler), new(20, 10), new(0, 0, 10, 10));
        await VerifyAsync(width => Image(source: source, stretch: stretch, width: width, height: 100, sharedElementKey: new UiSharedElementKey("media")));
    }

    /// <summary>Checks image, vector and glyph icons under tight non-square endpoint constraints.</summary>
    /// <param name="representation">The source representation.</param>
    /// <returns>The asynchronous verification.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ResizedIconPreservesOldDrawingAtFlightStartAsync(int representation)
    {
        using var texture = new Texture();
        using IGraphicsTextureView view = texture.CreateView();
        using var sampler = new Sampler();
        var path = new PathBuilder2D();
        path.MoveTo(new(0, 0));
        path.LineTo(new(20, 0));
        path.LineTo(new(20, 10));
        path.Close();
        Path2D geometry = path.Build();
        var paint = PaintGraph2D.Solid(new(1, 0, 0, 1));
        var viewBox = new Rect2D(0, 0, 20, 10);
        IconSource source = representation switch
        {
            0 => IconSource.Image(new(new(view, sampler), new(20, 10), new(0, 0, 10, 10))),
            1 => IconSource.Vector(geometry, viewBox, paint),
            _ => IconSource.Glyph(TextLayout2D.FromDrawings("x", viewBox, viewBox, 1, [new TextGeometry2D(geometry, paint)]), viewBox),
        };
        await VerifyAsync(width => Icon(source: source, width: width, height: 100, sharedElementKey: new UiSharedElementKey("media")));
    }

    private static async Task VerifyAsync(Func<float, Widget> create)
    {
        var driver = new BufferDriver();
        UiApplicationWindowContext? context = null;
        await using UiApplication app = UiApplication.CreateBuilder().UseDriver(() => driver).UseRoot(value =>
        {
            context = value;
            return Panel();
        }).Build();
        Task running = app.RunAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (app.State != UiApplicationState.Running)
        {
            await Task.Delay(1, timeout.Token);
        }

        using var before = new UiSurface(Panel()[create(100)]);
        using var after = new UiSurface(Panel()[create(200)]);
        UiPageVisualFlight? flight = null;
        try
        {
            Task prepared = await app.Dispatcher.InvokeAsync(
                () =>
                {
                    before.SetViewport(new(300, 200));
                    after.SetViewport(new(300, 200));
                    before.UpdateLayout();
                    after.UpdateLayout();
                    Assert.Equal(new Vector2(100, 100), before.RootElement.Children[0].ContentSize);
                    Assert.Equal(new Vector2(200, 100), after.RootElement.Children[0].ContentSize);
                    var source = new UiVisualCoordinator(app, before, () => context!.Window);
                    var target = new UiVisualCoordinator(app, after, () => context!.Window);
                    var plan = new UiViewVisualPlan(null, null, false, true, Duration.FromSeconds(1), AnimationEasing.Linear);
                    flight = new(app, target, source, plan, static () => false);
                    Task pending = flight.PrepareAsync(timeout.Token).AsTask();
                    Assert.True(app.Windows.TryGetSnapshot(context!.Window, out UiWindowSnapshot? window));
                    source.Presented(new(window!.Id, window.SourceId, window.Role, window.BoundsInPresentation, window.ClipInPresentation, window.Scale, 1, true), true, before.VisualRevision);
                    return pending;
                },
                timeout.Token);
            await prepared.WaitAsync(timeout.Token);
            await app.Dispatcher.InvokeAsync(
                () =>
                {
                    Assert.Equal(2, driver.Captures.Count);
                    Texture old = driver.Captures[0];
                    Assert.Equal(100u, old.Width);
                    Assert.Equal(100u, old.Height);
                    DrawingCommand2D expected = Assert.Single(before.CaptureVisual(before.RootElement.Children[0], Vector2.One).Commands);
                    AssertDrawing(expected, Assert.Single(old.Drawing!.Commands));
                    var canvas = new Canvas2D();
                    canvas.Begin();

                    // The real flight samples its connected tween at amount zero before any Advance.
                    flight!.Record(canvas, new(app, after, () => context!.Window));
                    FillRectCommand2D command = Assert.IsType<FillRectCommand2D>(Assert.Single(canvas.End().Commands));
                    Assert.Equal(new Rect2D(0, 0, 100, 100), command.Rect);
                    CompositePaintNode2D composite = Assert.IsType<CompositePaintNode2D>(command.Paint.Root);
                    Assert.Equal(CompositeMode2D.Plus, composite.Mode);
                    OpacityPaintNode2D previous = Assert.IsType<OpacityPaintNode2D>(composite.Backdrop);
                    OpacityPaintNode2D next = Assert.IsType<OpacityPaintNode2D>(composite.Source);
                    Assert.Equal(1, previous.Opacity);
                    Assert.Equal(0, next.Opacity);
                    ImagePaintNode2D image = Assert.IsType<ImagePaintNode2D>(previous.Child);
                    Assert.Same(old, image.Pattern.View.Texture);
                },
                timeout.Token);
        }
        finally
        {
            if (flight is not null)
            {
                await app.Dispatcher.InvokeAsync(flight.Stop);
                await flight.DrainAsync();
            }

            await app.StopAsync();
            await running.WaitAsync(timeout.Token);
        }
    }

    private static void AssertDrawing(DrawingCommand2D expected, DrawingCommand2D actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.State.Transform, actual.State.Transform);
        Assert.Equal(expected.State.Clip.Elements.Count, actual.State.Clip.Elements.Count);
        for (int index = 0; index < expected.State.Clip.Elements.Count; index++)
        {
            ClipElement2D expectedClip = expected.State.Clip.Elements[index];
            ClipElement2D actualClip = actual.State.Clip.Elements[index];
            Assert.Equal(expectedClip.Transform, actualClip.Transform);
            Assert.Equal(expectedClip.Rule, actualClip.Rule);
            Assert.Equal(expectedClip.Path.Segments, actualClip.Path.Segments);
        }

        if (expected is DrawImageCommand2D image)
        {
            DrawImageCommand2D recorded = Assert.IsType<DrawImageCommand2D>(actual);
            Assert.Same(image.Image, recorded.Image);
            Assert.Equal(image.Source, recorded.Source);
            Assert.Equal(image.Destination, recorded.Destination);
        }
        else
        {
            FillPathCommand2D path = Assert.IsType<FillPathCommand2D>(expected);
            FillPathCommand2D recorded = Assert.IsType<FillPathCommand2D>(actual);
            Assert.Same(path.Path, recorded.Path);
            Assert.Same(path.Paint, recorded.Paint);
        }
    }

    private sealed class BufferDriver : UiApplicationDriver
    {
        internal List<Texture> Captures { get; } = [];

        internal override bool SupportsVisuals => true;

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal override ValueTask<UiVisualSnapshot?> CaptureVisualAsync(DrawList2D commands, uint width, uint height, IDisposable reservation, IReadOnlyList<UiVisualSnapshot>? visuals = null)
        {
            var texture = new Texture { Width = width, Height = height, Drawing = commands };
            Captures.Add(texture);
            return ValueTask.FromResult<UiVisualSnapshot?>(new(texture, new View(texture), reservation));
        }

        internal override ValueTask InitializeAsync(UiApplication application, IServiceProvider services, bool textInput, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        internal override ValueTask<UiDriverWindow> CreateWindowAsync(string key, string title, Vector2 size, UiDriverWindow? owner, bool primary, CancellationToken cancellationToken) => ValueTask.FromResult<UiDriverWindow>(new BufferWindow(key));

        internal override void Pump()
        {
        }

        internal override Task<bool> DrawAsync(UiDriverWindow window, DrawingSnapshot2D scene) => Task.FromResult(true);

        internal override ValueTask CloseWindowAsync(UiDriverWindow window) => window.DisposeAsync();
    }

    private sealed class BufferWindow(string key) : UiDriverWindow(key)
    {
        private readonly Guid _scope = Guid.NewGuid();
        private bool _visible;
        private bool _closed;
        private ulong _revision = 1;

        public override ValueTask DisposeAsync()
        {
            _closed = true;
            _revision++;
            return ValueTask.CompletedTask;
        }

        internal override UiWindowSourceSnapshot CaptureState() => new(_revision, null, new(10, 10), Vector2.One, _visible, _visible, _closed, _scope);

        internal override void Show()
        {
            _visible = true;
            _revision++;
        }

        internal override void SetTitle(string title)
        {
        }
    }

    private sealed class Texture : IGraphicsTexture
    {
        public uint Width { get; init; } = 10;

        public uint Height { get; init; } = 10;

        public uint MipLevels => 1;

        public uint ArrayLayers => 1;

        public TextureFormat Format => TextureFormat.Rgba16Float;

        public TextureUsage Usage => TextureUsage.Sampled | TextureUsage.RenderAttachment;

        internal DrawList2D? Drawing { get; init; }

        internal int Disposals { get; private set; }

        public void Dispose() => Disposals++;

        public (uint Width, uint Height) GetMipSize(uint mipLevel) => (Width, Height);

        public IGraphicsTextureView CreateView(TextureViewDesc? desc = null) => new View(this);
    }

    private sealed class View(IGraphicsTexture texture) : IGraphicsTextureView
    {
        public IGraphicsTexture Texture => texture;

        public TextureViewInfo Info => new() { Format = texture.Format, Dimension = TextureViewDimension.D2, BaseMipLevel = 0, MipLevelCount = 1, BaseArrayLayer = 0, ArrayLayerCount = 1 };

        internal int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    private sealed class Sampler : IGraphicsSampler
    {
        public SamplerDesc Desc { get; } = new();

        public void Dispose()
        {
        }
    }
}
