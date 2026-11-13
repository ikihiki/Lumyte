using System.Numerics;
using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;
using Lumyte.Signals;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Windowing;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks the standard compositor shares local content, not whole direct recordings.</summary>
public sealed class HostedRetainedDrawingTests
{
    /// <summary>Checks an attached inactive coordinator does not force opaque recording.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task OrdinaryCoordinatorSharesUnchangedFragmentsAsync()
    {
        var color = Signal.Create(new Rgba<float>(1, 0, 0, 1));
        using UiSurface surface = Surface(Panel()[Rectangle(key: "changing", fill: color), Rectangle(key: "stable", fill: new Rgba<float>(0, 0, 1, 1))]);
        await using UiApplication app = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => Panel()).Build();
        var coordinator = new UiVisualCoordinator(app, surface, () => null);
        surface.VisualHost = coordinator;
        UiHostedDrawingSource source = Source();
        UiWindowPresentationView view = View(source);
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(view, surface, default);
        using DrawingSnapshot2D first = Frame(source);
        DrawingSceneEntry2D[] initial = Entries(first);
        Assert.Equal(3, initial.Length);
        using DrawingSnapshot2D unchanged = Frame(source);
        Assert.True(initial.Select(e => e.Content).SequenceEqual(Entries(unchanged).Select(e => e.Content)));
        Assert.Equal(2ul, coordinator.PresentedSequence);
        color.Value = new(0, 1, 0, 1);
        using DrawingSnapshot2D changed = Frame(source);
        DrawingSceneEntry2D[] next = Entries(changed);

        // Ancestor visual revision changes can refresh its empty self fragment.
        Assert.Empty(next[0].Content.Commands);
        Assert.NotSame(initial[1].Content, next[1].Content);
        Assert.Same(initial[2].Content, next[2].Content);
        Assert.Equal(new Rgba<float>(0, 1, 0, 1), Color(next[1]));
        Assert.Equal(view, source.Presented(view.Window));
        using DrawingSnapshot2D generation = Frame(source, 1);
        Assert.NotSame(next[2].Content, Entries(generation)[2].Content);
    }

    /// <summary>Checks painter order, visibility, scale and external clipping remain separate from local content.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task PlacementChangesKeepContentAndMatchDirectGeometryAsync()
    {
        using UiSurface first = Surface(Rectangle(fill: new Rgba<float>(1, 0, 0, 1), clipToBounds: true));
        using UiSurface second = Surface(Rectangle(fill: new Rgba<float>(0, 1, 0, 1)));
        UiHostedDrawingSource source = Source();
        UiWindowPresentationView view = View(source);
        await using IUiWindowPresentation one = await source.PreparePresentationAsync(view, first, default);
        await using IUiWindowPresentation two = await source.PreparePresentationAsync(view with { Window = new(source.SourceId, 2) }, second, default);
        using DrawingSnapshot2D initial = Frame(source);
        DrawList2D content = Entries(initial)[0].Content;
        UiWindowPresentationView moved = view with { BoundsInPresentation = new(3, 4, 20, 10), ClipInPresentation = new(1, 2, 15, 8), Scale = new(2, 3), StackingOrder = 10 };
        one.Update(moved);
        using DrawingSnapshot2D next = Frame(source);
        DrawingSceneEntry2D[] entries = Entries(next);
        Assert.Equal(new Rgba<float>(0, 1, 0, 1), Color(entries[0]));
        Assert.Same(content, entries[1].Content);
        Assert.Equal(Matrix3x2.CreateTranslation(3, 4) * Matrix3x2.CreateScale(2, 3), entries[1].Transform);
        var canvas = new Canvas2D();
        canvas.Begin();
        source.Record(canvas);
        DrawList2D direct = canvas.End();
        source.CompleteFrame();
        DrawingCommand2D command = direct.Commands[1];
        Assert.Equal(command.State.Transform, entries[1].Content.Commands[0].State.Transform * entries[1].Transform);
        Assert.Equal(command.State.Clip.Elements.Select(c => c.Transform), entries[1].Clips.Select(c => c.Transform));
        one.Update(moved with { IsVisible = false });
        using DrawingSnapshot2D hidden = Frame(source);
        Assert.Single(Entries(hidden));
        one.Update(moved);
        using DrawingSnapshot2D shown = Frame(source);
        Assert.Same(content, Entries(shown)[1].Content);
    }

    /// <summary>Checks unknown visual hosts remain live and switching back removes opaque content.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task OpaqueCompositionAndOrdinaryDrawingCanAlternateAsync()
    {
        using UiSurface surface = Surface(Panel()[Rectangle()]);
        UiHostedDrawingSource source = Source();
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(View(source), surface, default);
        using DrawingSnapshot2D normal = Frame(source);
        Assert.Equal(2, Entries(normal).Length);
        var host = new DynamicHost();
        surface.VisualHost = host;
        using DrawingSnapshot2D opaque = Frame(source);
        using DrawingSnapshot2D opaqueAgain = Frame(source);
        Assert.Single(Entries(opaque));
        Assert.NotSame(Entries(opaque)[0].Content, Entries(opaqueAgain)[0].Content);
        Assert.Equal(4, host.Calls);
        surface.VisualHost = null;
        using DrawingSnapshot2D ordinary = Frame(source);
        Assert.Equal(2, Entries(ordinary).Length);
    }

    /// <summary>Checks validation convergence and recording failures do not wedge the next retained frame.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task LaterValidationAndRecordingFailuresRecoverAsync()
    {
        using UiSurface first = Surface(Rectangle(fill: new Rgba<float>(1, 0, 0, 1)));
        using UiSurface second = Surface(Panel());
        UiHostedDrawingSource source = Source();
        await using IUiWindowPresentation one = await source.PreparePresentationAsync(View(source), first, default);
        await using IUiWindowPresentation two = await source.PreparePresentationAsync(View(source) with { Window = new(source.SourceId, 2) }, second, default);
        bool change = true;
        source.ValidateSurface = surface =>
        {
            if (ReferenceEquals(surface, second) && change)
            {
                change = false;
                first.Rebuild(Rectangle(fill: new Rgba<float>(0, 1, 0, 1)));
            }
        };
        using DrawingSnapshot2D frame = Frame(source);
        Assert.Equal(new Rgba<float>(0, 1, 0, 1), Color(Entries(frame)[0]));
        source.ValidateSurface = null;
        first.Rebuild(new CustomPanel(() => throw new ArithmeticException()));
        Assert.Throws<ArithmeticException>(() => source.RecordRetainedFrame(0));
        first.Rebuild(Rectangle());
        using DrawingSnapshot2D recovered = Frame(source);
        Assert.Equal(2, Entries(recovered).Length);
    }

    /// <summary>Checks custom drawing is refreshed without recapturing an unchanged sibling.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task CustomDrawingRemainsLiveAsync()
    {
        int recordings = 0;
        using UiSurface surface = Surface(Panel()[new CustomPanel(() => recordings++), Rectangle()]);
        UiHostedDrawingSource source = Source();
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(View(source), surface, default);
        using DrawingSnapshot2D first = Frame(source);
        using DrawingSnapshot2D next = Frame(source);
        Assert.Equal(2, recordings);
        Assert.NotSame(Entries(first)[1].Content, Entries(next)[1].Content);
        Assert.Same(Entries(first)[2].Content, Entries(next)[2].Content);
    }

    /// <summary>Checks retained dependency hooks converge before any surface is captured.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task RetainedDependencyHookRevisitsEarlierSurfaceAsync()
    {
        using UiSurface first = Surface(Rectangle(fill: new Rgba<float>(1, 0, 0, 1)));
        bool change = true;
        using UiSurface second = Surface(new ValidationPanel(() =>
        {
            if (change)
            {
                change = false;
                first.Rebuild(Rectangle(fill: new Rgba<float>(0, 1, 0, 1)));
            }
        }));
        UiHostedDrawingSource source = Source();
        await using IUiWindowPresentation one = await source.PreparePresentationAsync(View(source), first, default);
        await using IUiWindowPresentation two = await source.PreparePresentationAsync(View(source) with { Window = new(source.SourceId, 2) }, second, default);
        using DrawingSnapshot2D frame = Frame(source);
        Assert.Equal(new Rgba<float>(0, 1, 0, 1), Color(Entries(frame)[0]));
    }

    /// <summary>Checks animation registrations can be added and removed without a structural rebuild.</summary>
    /// <param name="visible">Whether the element is visible.</param>
    /// <returns>The verification.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnimationAttachmentsRemainLiveAsync(bool visible)
    {
        using UiSurface surface = Surface(Rectangle(width: 10, isVisible: visible));
        UiHostedDrawingSource source = Source();
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(View(source), surface, default);
        using DrawingSnapshot2D first = Frame(source);
        UiElement element = surface.RootElement;
        ulong structure = surface.StructureRevision;
        var target = Signal.Create<float?>(10);
        UiAnimated<float?> animation = Animate(target, Duration.Zero, UiAnimationInterpolators.Nullable(AnimationInterpolators.Float));
        surface.Rebuild(Rectangle(width: animation, isVisible: visible));
        using DrawingSnapshot2D animated = Frame(source);
        using DrawingSnapshot2D repeated = Frame(source);
        Assert.Same(element, surface.RootElement);
        Assert.Equal(structure, surface.StructureRevision);
        surface.Rebuild(Rectangle(width: 10, isVisible: visible));
        using DrawingSnapshot2D restored = Frame(source);
        using DrawingSnapshot2D unchanged = Frame(source);
        Assert.True(Entries(restored).Select(e => e.Content).SequenceEqual(Entries(unchanged).Select(e => e.Content)));
        Assert.Equal(Entries(first).Length, Entries(restored).Length);
        Assert.Same(element, surface.RootElement);
    }

    /// <summary>Checks absent presence content still renews frame receipts without a direct frame route.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task AbsentPresenceRenewsReceiptsAsync()
    {
        using UiSurface surface = Surface(Rectangle(isPresent: false));
        UiHostedDrawingSource source = Source();
        UiWindowPresentationView view = View(source);
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(view, surface, default);
        using DrawingSnapshot2D first = Frame(source);
        using DrawingSnapshot2D second = Frame(source);
        Assert.Empty(Entries(first).SelectMany(e => e.Content.Commands));
        Assert.Empty(Entries(second).SelectMany(e => e.Content.Commands));
        Assert.Equal(view, source.Presented(view.Window));
    }

    private static DrawingSceneEntry2D[] Entries(DrawingSnapshot2D snapshot) => ((IDrawingSceneSource2D)snapshot).Entries.ToArray();

    private static Rgba<float> Color(DrawingSceneEntry2D entry) => Assert.IsType<SolidPaintNode2D>(Assert.IsType<FillRectCommand2D>(entry.Content.Commands.Single()).Paint.Root).Color;

    private static UiSurface Surface(Widget widget)
    {
        var surface = new UiSurface(widget);
        surface.SetViewport(new(20, 10));
        return surface;
    }

    private static UiHostedDrawingSource Source()
    {
        var state = new UiWindowSourceSnapshot(1, null, new(20, 10), Vector2.One, true, true, false, Guid.NewGuid());
        return new(() => state);
    }

    private static UiWindowPresentationView View(UiHostedDrawingSource source) => new(new(source.SourceId, 1), source.SourceId, UiWindowRole.Normal, new(0, 0, 20, 10), new(0, 0, 20, 10), Vector2.One, 1, true);

    private static DrawingSnapshot2D Frame(UiHostedDrawingSource source, ulong generation = 0)
    {
        DrawingSnapshot2D frame = source.RecordRetainedFrame(generation);
        source.CompleteFrame();
        return frame;
    }

    private sealed class DynamicHost : IUiVisualHost
    {
        public bool CanPresent => true;

        public UiMotionOptions Motion { get; } = new();

        internal int Calls { get; private set; }

        public bool Record(UiElement widget, DrawingCanvas2D canvas)
        {
            Calls++;
            return false;
        }
    }

    private sealed class ValidationPanel(Action validate) : Definitions.Panel
    {
        protected override UiElement CreateElement() => new ValidationElement(this, validate);
    }

    private sealed class ValidationElement(ValidationPanel widget, Action validate) : PanelElement(widget)
    {
        protected override void ValidateRetainedDrawing() => validate();
    }

    private sealed class CustomPanel(Action record) : Definitions.Panel
    {
        protected override UiElement CreateElement() => new CustomElement(this, record);
    }

    private sealed class CustomElement(CustomPanel widget, Action record) : PanelElement(widget)
    {
        protected override void OnRecord(DrawingCanvas2D canvas) => record();
    }
}
