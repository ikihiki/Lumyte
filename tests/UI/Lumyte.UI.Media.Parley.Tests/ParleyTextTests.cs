using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using Lumyte.Graphics.Text.Parley;
using Xunit;
using TextWidget = Lumyte.UI.Media.Compose.Definitions.Text;

namespace Lumyte.UI.Media.Parley.Tests;

/// <summary>Exercises real registered fonts through the display-only adapter and UI layout.</summary>
public sealed class ParleyTextTests
{
    private const string Paragraph = "office AV wrapping words across several lines of real outlined text";

    /// <summary>Checks wrapping, zero width and completed snapshot independence from native owners.</summary>
    [Fact]
    public void RealFontWrapsAndSnapshotRendersAfterEngineDisposal()
    {
        TextLayout2D snapshot;
        using (var engine = new ParleyTextEngine2D())
        {
            string family = RegisterFont(engine);
            var provider = new ParleyTextLayoutProvider(engine, new() { FontFamily = family, FontSize = 20 });
            TextLayout2D wide = provider.CreateLayout(Paragraph, float.PositiveInfinity);
            snapshot = provider.CreateLayout(Paragraph, 110);
            Assert.True(snapshot.LineCount > wide.LineCount);
            Assert.True(snapshot.AdvanceBounds.Height > wide.AdvanceBounds.Height);
            Assert.NotEmpty(snapshot.Geometry);
            TextLayout2D empty = provider.CreateLayout(Paragraph, 0);
            Assert.Equal(Paragraph, empty.Text);
            Assert.Equal(0, empty.AdvanceBounds.Width);
            Assert.Equal(0, empty.AdvanceBounds.Height);
            Assert.Empty(empty.Drawings);
        }

        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawText(snapshot);
        Assert.True(canvas.End().Count > 0);
    }

    /// <summary>Checks invalid widths before native shaping.</summary>
    /// <param name="width">The invalid maximum width.</param>
    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidWidthsAreRejected(float width)
    {
        using var engine = new ParleyTextEngine2D();
        var provider = new ParleyTextLayoutProvider(engine);
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.CreateLayout("text", width));
        Assert.Throws<ArgumentNullException>(() => provider.CreateLayout(null!, 20));
    }

    /// <summary>Checks all supplied mutable options lists are captured at construction.</summary>
    [Fact]
    public void SuppliedOptionsListsAreCopied()
    {
        using var engine = new ParleyTextEngine2D();
        string family = RegisterFont(engine);
        var fallback = new List<string> { family };
        var features = new List<ParleyFontFeature2D> { new("liga", 0) };
        var variations = new List<ParleyFontVariation2D> { new("wght", 400) };
        var runs = new List<ParleyTextRun2D> { new() { Start = 0, End = 6, FontSize = 30 } };
        var options = new ParleyTextOptions2D
        {
            FontFamily = family,
            FontSize = 20,
            WrappingLength = 1,
            FallbackFontFamilies = fallback,
            FontFeatures = features,
            FontVariations = variations,
            TextRuns = runs,
        };
        var provider = new ParleyTextLayoutProvider(engine, options);
        TextLayout2D before = provider.CreateLayout(Paragraph, 180);
        fallback[0] = "unregistered family";
        features[0] = new("bad", 1);
        variations[0] = new("bad", float.NaN);
        runs[0] = new() { Start = -1, End = -2 };
        TextLayout2D after = provider.CreateLayout(Paragraph, 180);
        Assert.Equal(before.AdvanceBounds, after.AdvanceBounds);
        Assert.Equal(before.LineCount, after.LineCount);
        Assert.Equal(before.Geometry.Count, after.Geometry.Count);
        Assert.True(before.LineCount < Paragraph.Length);
    }

    /// <summary>Checks failed font registration and missing-font layout can be retried with explicit fonts.</summary>
    [Fact]
    public void FontFailureCanRetryAndBorrowedEngineRemainsUsable()
    {
        var engine = new ParleyTextEngine2D();
        try
        {
            var provider = new ParleyTextLayoutProvider(engine);
            Assert.ThrowsAny<Exception>(() => engine.AddFont(new byte[] { 1, 2, 3 }));
            Assert.ThrowsAny<Exception>(() => provider.CreateLayout(Paragraph, 100));
            RegisterFont(engine);
            TextLayout2D layout = provider.CreateLayout(Paragraph, 100);
            Assert.NotEmpty(layout.Geometry);
            using (var surface = new UiSurface(new TextWidget { Value = Paragraph, LayoutProvider = provider }))
            {
                surface.SetViewport(new(120, 200));
                surface.UpdateLayout();
            }

            Assert.NotEmpty(provider.CreateLayout("still borrowed", 200).Geometry);
            engine.Dispose();
            Assert.Throws<ObjectDisposedException>(() => provider.CreateLayout("disposed", 100));
            Assert.Empty(provider.CreateLayout("zero", 0).Drawings);
            var canvas = new Canvas2D();
            canvas.Begin();
            canvas.DrawText(layout);
            Assert.True(canvas.End().Count > 0);
        }
        finally
        {
            engine.Dispose();
        }
    }

    /// <summary>Checks one layout call per effective width and no shaping from getters or drawing.</summary>
    [Fact]
    public void FinalWidthDeterminesHeightInOnePassAndDrawingDoesNotShape()
    {
        using var engine = new ParleyTextEngine2D();
        string family = RegisterFont(engine);
        var provider = new CountingProvider(new ParleyTextLayoutProvider(engine, new() { FontFamily = family, FontSize = 20 }));
        TextWidget text = Lumyte.UI.Media.Compose.Text(value: Paragraph, layoutProvider: provider, width: 100);
        using var surface = new UiSurface(Lumyte.UI.Compose.Stack()[text]);
        surface.SetViewport(new(300, 500));
        surface.UpdateLayout();
        TextElement element = Assert.IsType<TextElement>(Assert.Single(surface.RootElement.Children));
        Assert.Equal(1, provider.Calls);
        Assert.Equal(100, provider.Width);
        Assert.Equal(element.CurrentLayout!.AdvanceBounds.Height, element.ContentSize.Y);
        Assert.True(element.CurrentLayout.LineCount > 1);
        surface.UpdateLayout();
        for (int i = 0; i < 2; i++)
        {
            var canvas = new Canvas2D();
            canvas.Begin();
            surface.Record(canvas);
            Assert.True(canvas.End().Count > 0);
            Assert.NotNull(element.CurrentLayout);
        }

        Assert.Equal(1, provider.Calls);
        UiAutomationNode node = Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.Role == UiRole.Text);
        Assert.Equal(UiAutomationActions.None, node.Actions);
    }

    /// <summary>Checks a failed provider call never replaces the last successful snapshot and retries cleanly.</summary>
    [Fact]
    public void ProviderFailurePreservesSnapshotAndRetries()
    {
        using var engine = new ParleyTextEngine2D();
        RegisterFont(engine);
        var provider = new CountingProvider(new ParleyTextLayoutProvider(engine));
        using var surface = new UiSurface(Lumyte.UI.Compose.Stack()[Lumyte.UI.Media.Compose.Text(value: Paragraph, layoutProvider: provider)]);
        surface.SetViewport(new(200, 500));
        surface.UpdateLayout();
        TextElement element = Assert.IsType<TextElement>(Assert.Single(surface.RootElement.Children));
        TextLayout2D? previous = element.CurrentLayout;
        provider.Fail = true;
        surface.SetViewport(new(90, 500));
        Assert.ThrowsAny<Exception>(() => surface.UpdateLayout());
        Assert.Same(previous, element.CurrentLayout);
        provider.Fail = false;
        surface.UpdateLayout();
        Assert.NotSame(previous, element.CurrentLayout);
        Assert.Equal(3, provider.Calls);
        Assert.True(element.CurrentLayout!.AdvanceBounds.Height > previous!.AdvanceBounds.Height);
    }

    /// <summary>Checks provider and value validation occurs before mounting.</summary>
    [Fact]
    public void MissingProviderAndNullValueAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => new UiSurface(new TextWidget()));
        using var engine = new ParleyTextEngine2D();
        Assert.Throws<ArgumentNullException>(() => new UiSurface(new TextWidget { Value = null!, LayoutProvider = new ParleyTextLayoutProvider(engine) }));
    }

    private static string RegisterFont(ParleyTextEngine2D engine)
    {
        using Stream font = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fonts/DejaVuSans.ttf"));
        return engine.AddFont(font);
    }

    private sealed class CountingProvider(ITextLayoutProvider inner) : ITextLayoutProvider
    {
        public int Calls { get; private set; }

        public float Width { get; private set; }

        public bool Fail { get; set; }

        public TextLayout2D CreateLayout(string value, float maximumWidth)
        {
            Calls++;
            Width = maximumWidth;
            if (Fail)
            {
                throw new InvalidOperationException("Injected layout failure.");
            }

            return inner.CreateLayout(value, maximumWidth);
        }
    }
}
