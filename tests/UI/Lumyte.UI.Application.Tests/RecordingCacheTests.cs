using System.Numerics;
using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Windowing;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks retained CPU commands preserve fresh frame receipts and dependency invalidation.</summary>
public sealed class RecordingCacheTests
{
    /// <summary>Checks unchanged commands are reused but every frame renews presentation confirmation.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task StaticFramesReuseCommandsAndRenewReceiptsAsync()
    {
        using UiSurface surface = Surface(Rectangle(fill: new Color2D(1, 0, 0, 1)));
        UiHostedDrawingSource source = Source();
        UiWindowPresentationView view = View(source);
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(view, surface, default);
        DrawList2D first = Frame(source);
        for (int index = 0; index < 5; index++)
        {
            Assert.Same(first, Frame(source));
            Assert.Equal(view, source.Presented(view.Window));
        }

        Assert.Single(first.Commands);
        Assert.NotSame(first, Frame(source, 1));
    }

    /// <summary>Checks visual changes invalidate even when structure and Element identity are unchanged.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task AlternatingColorsNeverReuseStaleCommandsAsync()
    {
        var color = Signal.Create(new Color2D(1, 0, 0, 1));
        using UiSurface surface = Surface(Panel()[Rectangle(fill: color)]);
        UiHostedDrawingSource source = Source();
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(View(source), surface, default);
        UiElement element = surface.RootElement.Children[0];
        ulong structure = surface.StructureRevision;
        DrawList2D previous = Frame(source);
        for (int index = 0; index < 6; index++)
        {
            color.Value = index % 2 == 0 ? new(0, 1, 0, 1) : new(1, 0, 0, 1);
            DrawList2D next = Frame(source);
            Assert.NotSame(previous, next);
            Assert.Equal(color.Value, Color(next.Commands.Single()));
            Assert.Equal(structure, surface.StructureRevision);
            Assert.Same(element, surface.RootElement.Children[0]);
            Assert.Same(next, Frame(source));
            previous = next;
        }
    }

    /// <summary>Checks nonstructural animation attachments invalidate eligibility, including hidden and stopped registrations.</summary>
    /// <param name="visible">Whether the animated node is visible.</param>
    /// <returns>The verification.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StaticAnimatedStaticOnTheSameElementAsync(bool visible)
    {
        using UiSurface surface = Surface(Rectangle(width: 10, isVisible: visible));
        UiHostedDrawingSource source = Source();
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(View(source), surface, default);
        DrawList2D first = Frame(source);
        Assert.Same(first, Frame(source));
        UiElement element = surface.RootElement;
        ulong structure = surface.StructureRevision;
        var target = Signal.Create<float?>(10);
        UiAnimated<float?> animation = Animate(target, Duration.Zero, UiAnimationInterpolators.Nullable(AnimationInterpolators.Float));
        surface.Rebuild(Rectangle(width: animation, isVisible: visible));
        DrawList2D animated = Frame(source);
        Assert.NotSame(animated, Frame(source));
        Assert.Same(element, surface.RootElement);
        Assert.Equal(structure, surface.StructureRevision);
        surface.Rebuild(Rectangle(width: 10, isVisible: visible));
        DrawList2D staticAgain = Frame(source);
        Assert.Same(staticAgain, Frame(source));
        Assert.Same(element, surface.RootElement);
    }

    /// <summary>Checks custom recording executes every frame, including after replacing an eligible tree.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task CustomRecordingAndPublicRecordRemainLiveAsync()
    {
        int records = 0;
        using UiSurface surface = Surface(Panel());
        UiHostedDrawingSource source = Source();
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(View(source), surface, default);
        DrawList2D first = Frame(source);
        Assert.Same(first, Frame(source));
        surface.Rebuild(new CustomPanel(() => records++));
        Assert.NotSame(Frame(source), Frame(source));
        Assert.Equal(2, records);
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        surface.Record(canvas);
        _ = canvas.End();
        Assert.Equal(4, records);
    }

    /// <summary>Checks geometry, visibility and presentation order invalidate the complete recording.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task PresentationChangesInvalidateTheFrameAsync()
    {
        using UiSurface first = Surface(Rectangle(fill: new Color2D(1, 0, 0, 1)));
        using UiSurface second = Surface(Rectangle(fill: new Color2D(0, 1, 0, 1)));
        UiHostedDrawingSource source = Source();
        UiWindowPresentationView view = View(source);
        await using IUiWindowPresentation one = await source.PreparePresentationAsync(view, first, default);
        await using IUiWindowPresentation two = await source.PreparePresentationAsync(view with { Window = new(source.SourceId, 2) }, second, default);
        DrawList2D previous = Frame(source);
        one.Update(view with { StackingOrder = 10 });
        DrawList2D reordered = Frame(source);
        Assert.NotSame(previous, reordered);
        Assert.Equal(new Color2D(0, 1, 0, 1), Color(reordered.Commands[0]));
        one.Update(view with { IsVisible = false });
        Assert.Single(Frame(source).Commands);
        one.Update(view with { Scale = new(2, 2), ClipInPresentation = new(0, 0, 5, 5) });
        DrawList2D scaled = Frame(source);
        Assert.NotSame(reordered, scaled);
        Assert.Same(scaled, Frame(source));
        first.SetViewport(new(30, 20));
        Assert.NotSame(scaled, Frame(source));
    }

    /// <summary>Checks presence content is never admitted to the static command cache.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task PresenceRegistrationsDisableReuseAsync()
    {
        using UiSurface surface = Surface(Rectangle(isPresent: false));
        UiHostedDrawingSource source = Source();
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(View(source), surface, default);
        Assert.NotSame(Frame(source), Frame(source));
    }

    /// <summary>Checks later validation cannot leave earlier recorded surfaces stale.</summary>
    /// <param name="applyImmediately">Whether the callback itself publishes the mutation.</param>
    /// <returns>The verification.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LaterValidationRevisitsEarlierSurfacesAsync(bool applyImmediately)
    {
        using UiSurface first = Surface(Rectangle(fill: new Color2D(1, 0, 0, 1)));
        using UiSurface second = Surface(Panel());
        UiHostedDrawingSource source = Source();
        await using IUiWindowPresentation one = await source.PreparePresentationAsync(View(source), first, default);
        await using IUiWindowPresentation two = await source.PreparePresentationAsync(View(source) with { Window = new(source.SourceId, 2) }, second, default);
        _ = Frame(source);
        bool change = true;
        source.ValidateSurface = surface =>
        {
            if (ReferenceEquals(surface, second) && change)
            {
                change = false;
                first.Rebuild(Rectangle(fill: new Color2D(0, 1, 0, 1)));
                if (applyImmediately)
                {
                    first.UpdateTree();
                }
            }
        };
        Assert.Equal(new Color2D(0, 1, 0, 1), Color(Frame(source).Commands.Single()));
    }

    /// <summary>Checks exceptions and nonconvergent callbacks release the unfinished frame.</summary>
    /// <param name="throwImmediately">Whether validation throws instead of continuously enqueueing a new root.</param>
    /// <returns>The verification.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedValidationReleasesFrameAsync(bool throwImmediately)
    {
        using UiSurface surface = Surface(Panel());
        UiHostedDrawingSource source = Source();
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(View(source), surface, default);
        source.ValidateSurface = value =>
        {
            if (throwImmediately)
            {
                throw new InvalidOperationException("validation");
            }

            value.Rebuild(Panel());
        };
        Assert.Throws<InvalidOperationException>(() => source.RecordFrame(0));
        source.ValidateSurface = null;
        _ = Frame(source);
        Assert.True(presentation.DisposeAsync().IsCompletedSuccessfully);
    }

    /// <summary>Checks reactive editors are checked after Compute publishes the current subtree.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task ComputedEditorsAreValidatedOnMountedSurfacesAsync()
    {
        var show = Signal.Create(false);
        using UiSurface surface = Surface(Compute<Widget>(() => show.Value ? new Lumyte.UI.TextEditing.Compose.Definitions.TextBox() : Panel()));
        await using UiApplication app = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => Panel()).Build();
        UiHostedDrawingSource source = Source();
        source.ValidateSurface = app.ValidateEditors;
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(View(source), surface, default);
        _ = Frame(source);
        show.Value = true;
        Assert.Equal("invalid-editor", Assert.Throws<UiApplicationException>(() => source.RecordFrame(0)).Code);
        show.Value = false;
        _ = Frame(source);
    }

    /// <summary>Checks validation callback signal updates are applied before recording or cache comparison.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task ValidationAppliesItsOwnSignalMutationAsync()
    {
        var color = Signal.Create(new Color2D(1, 0, 0, 1));
        using UiSurface surface = Surface(Rectangle(fill: color));
        UiHostedDrawingSource source = Source();
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(View(source), surface, default);
        _ = Frame(source);
        source.ValidateSurface = _ => color.Value = new(0, 1, 0, 1);
        Assert.Equal(new Color2D(0, 1, 0, 1), Color(Frame(source).Commands.Single()));
    }

    /// <summary>Checks staged candidate recording validates the post-callback tree too.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task StagedValidationAppliesQueuedRebuildAsync()
    {
        using UiSurface surface = Surface(Panel());
        UiHostedDrawingSource source = Source();
        bool change = true;
        source.ValidateSurface = value =>
        {
            if (change)
            {
                change = false;
                value.Rebuild(Rectangle(fill: new Color2D(0, 1, 0, 1)));
            }
        };
        source.PrepareCandidate = (commands, _) =>
        {
            Assert.Equal(new Color2D(0, 1, 0, 1), Color(commands.Commands.Single()));
            return ValueTask.CompletedTask;
        };
        await using IUiStagedWindowPresentation staged = source.BeginReplacement(null, View(source), surface);
        await staged.PrepareAsync(default);
        Assert.True(staged.TryValidateCommit(out _));
        await staged.DiscardAsync(default);
    }

    /// <summary>Checks an unknown visual host may perform dynamic drawing on every call.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task UnknownVisualHostsDisableCachingAsync()
    {
        using UiSurface surface = Surface(Rectangle());
        var host = new DynamicHost();
        surface.VisualHost = host;
        UiHostedDrawingSource source = Source();
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(View(source), surface, default);
        Assert.NotSame(Frame(source), Frame(source));
        Assert.Equal(2, host.Calls);
    }

    /// <summary>Checks an exception after recording starts releases the frame before the next attempt.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task RecordingExceptionsReleaseTheFrameAsync()
    {
        using UiSurface surface = Surface(new CustomPanel(() => throw new ArithmeticException("record")));
        UiHostedDrawingSource source = Source();
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(View(source), surface, default);
        Assert.Throws<ArithmeticException>(() => source.RecordFrame(0));
        surface.Rebuild(Panel());
        _ = Frame(source);
        Assert.True(presentation.DisposeAsync().IsCompletedSuccessfully);
    }

    private static Color2D Color(DrawingCommand2D command) => Assert.IsType<SolidPaintNode2D>(Assert.IsType<FillRectCommand2D>(command).Paint.Root).Color;

    private static UiSurface Surface(Widget root)
    {
        var surface = new UiSurface(root);
        surface.SetViewport(new(20, 10));
        return surface;
    }

    private static UiHostedDrawingSource Source()
    {
        var state = new UiWindowSourceSnapshot(1, null, new(20, 10), Vector2.One, true, true, false, Guid.NewGuid());
        return new(() => state);
    }

    private static UiWindowPresentationView View(UiHostedDrawingSource source) => new(new(source.SourceId, 1), source.SourceId, UiWindowRole.Normal, new(0, 0, 20, 10), new(0, 0, 20, 10), Vector2.One, 1, true);

    private static DrawList2D Frame(UiHostedDrawingSource source, ulong generation = 0)
    {
        DrawList2D result = source.RecordFrame(generation);
        source.CompleteFrame();
        return result;
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

    private sealed class CustomPanel(Action record) : Definitions.Panel
    {
        protected override UiElement CreateElement() => new CustomElement(this, record);
    }

    private sealed class CustomElement(CustomPanel widget, Action record) : PanelElement(widget)
    {
        protected override void OnRecord(DrawingCanvas2D canvas) => record();
    }
}
