using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;
using Lumyte.Signals;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Windowing;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks retained CPU commands preserve fresh frame receipts and dependency invalidation.</summary>
public sealed class RetainedValidationTests
{
    /// <summary>Checks later validation cannot leave earlier recorded surfaces stale.</summary>
    /// <param name="applyImmediately">Whether the callback itself publishes the mutation.</param>
    /// <returns>The verification.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LaterValidationRevisitsEarlierSurfacesAsync(bool applyImmediately)
    {
        using UiSurface first = Surface(Rectangle(fill: new SrgbRgba<float>(1, 0, 0, 1)));
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
                first.Rebuild(Rectangle(fill: new SrgbRgba<float>(0, 1, 0, 1)));
                if (applyImmediately)
                {
                    first.UpdateTree();
                }
            }
        };
        Assert.Equal(new SrgbRgba<float>(0, 1, 0, 1), Color(Frame(source).Single()));
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
        Assert.Throws<InvalidOperationException>(() => source.RecordRetainedFrame(0));
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
        Assert.Equal("invalid-editor", Assert.Throws<UiApplicationException>(() => source.RecordRetainedFrame(0)).Code);
        show.Value = false;
        _ = Frame(source);
    }

    /// <summary>Checks validation callback signal updates are applied before recording or cache comparison.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task ValidationAppliesItsOwnSignalMutationAsync()
    {
        var color = Signal.Create(new SrgbRgba<float>(1, 0, 0, 1));
        using UiSurface surface = Surface(Rectangle(fill: color));
        UiHostedDrawingSource source = Source();
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(View(source), surface, default);
        _ = Frame(source);
        source.ValidateSurface = _ => color.Value = new(0, 1, 0, 1);
        Assert.Equal(new SrgbRgba<float>(0, 1, 0, 1), Color(Frame(source).Single()));
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
                value.Rebuild(Rectangle(fill: new SrgbRgba<float>(0, 1, 0, 1)));
            }
        };
        source.PrepareCandidate = (commands, _) =>
        {
            Assert.Equal(new SrgbRgba<float>(0, 1, 0, 1), Color(((IDrawingSceneSource2D)commands).Entries.SelectMany(entry => entry.Content.Commands).Single()));
            return ValueTask.CompletedTask;
        };
        await using IUiStagedWindowPresentation staged = source.BeginReplacement(null, View(source), surface);
        await staged.PrepareAsync(default);
        Assert.True(staged.TryValidateCommit(out _));
        await staged.DiscardAsync(default);
    }

    /// <summary>Checks unknown visual hosts perform dynamic drawing on every retained capture.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task UnknownVisualHostsRemainLiveAsync()
    {
        using UiSurface surface = Surface(Rectangle());
        var host = new DynamicHost();
        surface.VisualHost = host;
        UiHostedDrawingSource source = Source();
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(View(source), surface, default);
        _ = Frame(source);
        _ = Frame(source);
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
        Assert.Throws<ArithmeticException>(() => source.RecordRetainedFrame(0));
        surface.Rebuild(Panel());
        _ = Frame(source);
        Assert.True(presentation.DisposeAsync().IsCompletedSuccessfully);
    }

    /// <summary>Checks staged preparation borrows a snapshot only through its asynchronous callback.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task CandidateSnapshotLivesThroughPreparationAsync()
    {
        using UiSurface surface = Surface(Rectangle());
        UiHostedDrawingSource source = Source();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DrawingSnapshot2D? observed = null;
        source.PrepareCandidate = async (snapshot, _) =>
        {
            observed = snapshot;
            entered.SetResult();
            await release.Task;
            Assert.Single(((IDrawingSceneSource2D)snapshot).Entries);
        };
        await using IUiStagedWindowPresentation staged = source.BeginReplacement(null, View(source), surface);
        Task preparing = staged.PrepareAsync(default).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(observed);
        release.SetResult();
        await preparing;
        Assert.Throws<ObjectDisposedException>(() => ((IDrawingSceneSource2D)observed).Entries);
        await staged.DiscardAsync(default);
    }

    private static SrgbRgba<float> Color(DrawingCommand2D command) => Assert.IsType<SolidPaintNode2D>(Assert.IsType<FillRectCommand2D>(command).Paint.Root).Color;

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

    private static DrawingCommand2D[] Frame(UiHostedDrawingSource source, ulong generation = 0)
    {
        using DrawingSnapshot2D snapshot = source.RecordRetainedFrame(generation);
        source.CompleteFrame();
        return ((IDrawingSceneSource2D)snapshot).Entries.SelectMany(entry => entry.Content.Commands).ToArray();
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
