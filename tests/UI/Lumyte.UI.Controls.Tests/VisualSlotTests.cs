using Lumyte.Composition;
using Lumyte.Input.Abstractions;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls.Tests;

/// <summary>Exercises owned visual templates through real surface update and input boundaries.</summary>
public sealed class VisualSlotTests
{
    /// <summary>Templates see accepted values and run only at idle state/configuration boundaries.</summary>
    [Fact]
    public void AcceptedValuesAreDeferredAndCoalesced()
    {
        var values = new List<double>();
        CompositionTemplate<SliderThumbContext, Widget> template = thumb =>
        {
            values.Add(thumb.Value);
            return Rectangle();
        };
        Compose.Definitions.Slider declaration = Slider(value: 0.2)[Slider.Thumb()[template]];
        Assert.Empty(values);
        using UiSurface surface = Create(declaration);
        Assert.Equal(new[] { 0.2 }, values);
        surface.UpdateLayout();
        surface.CaptureAutomationSnapshot();
        Assert.Single(values);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(2, 10)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(70, 10)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(80, 10)));
        Assert.Equal(0.8, ((SliderElement)surface.RootElement).Value, 12);
        surface.UpdateLayout();
        Assert.Equal(0.8, values[^1], 12);
        int evaluations = values.Count;
        surface.UpdateLayout();
        Assert.Equal(evaluations, values.Count);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(80, 10)));
    }

    /// <summary>Fixed content stays on its host, but cannot be borrowed by a second surface.</summary>
    [Fact]
    public void FixedVisualOwnershipIsPerHost()
    {
        Widget fixedWidget = Rectangle();
        Compose.Definitions.Slider declaration = Slider()[Slider.Thumb()[fixedWidget]];
        using UiSurface surface = Create(declaration);
        UiElement child = Find(surface.RootElement, fixedWidget);
        surface.Dispatch(new UiSetNumericInput(surface.RootElement.AutomationId, 0.6, null, new(5)));
        surface.UpdateLayout();
        Assert.Same(child, Find(surface.RootElement, fixedWidget));
        Assert.Throws<InvalidOperationException>(() => new UiSurface(declaration));
        surface.Rebuild(Slider(value: 0.4)[Slider.Thumb()[fixedWidget]]);
        surface.UpdateLayout();
        Assert.Same(child, Find(surface.RootElement, fixedWidget));
    }

    /// <summary>Focusable/button-like decoration cannot intercept pointer, focus, or automation.</summary>
    [Fact]
    public void DecorationIsOwnerOnlyAndUsesFixedRectangle()
    {
        int clicks = 0;
        Widget visual = Button(onClick: () => clicks++);
        using UiSurface surface = Create(Slider(value: 0.5)[Slider.Thumb()[visual]]);
        UiElement decoration = Find(surface.RootElement, visual);
        Assert.Equal(10, decoration.Size.X);
        Assert.Equal(surface.RootElement.Size.Y, decoration.Size.Y);
        Assert.False(surface.TryFocus(decoration));
        Assert.Single(surface.CaptureAutomationSnapshot().Nodes);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(48, 10)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(80, 10)));
        surface.UpdateLayout();
        Assert.True(surface.CaptureAutomationSnapshot().Nodes[0].HasActivePress);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(80, 10)));
        Assert.Equal(0, clicks);
        Assert.True(surface.TryFocus(surface.RootElement));
        surface.Dispatch(new UiKeyInput(Key.End, true));
        surface.UpdateLayout();
        surface.Dispatch(new UiKeyInput(Key.End, false));
        Assert.Equal(1, ((SliderElement)surface.RootElement).Value);
    }

    /// <summary>Throwing/null/parented templates never replace the previously published subtree.</summary>
    /// <param name="failure">The invalid result mode.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidRefreshRollsBackAndRetries(int failure)
    {
        bool fail = false;
        Widget parented = Rectangle();
        _ = Panel()[parented];
        CompositionTemplate<SliderThumbContext, Widget> template = _ =>
        {
            if (!fail)
            {
                return Rectangle();
            }

            return failure switch { 0 => throw new InvalidOperationException("template"), 1 => null!, _ => parented };
        };
        using UiSurface surface = Create(Slider()[Slider.Thumb()[template]]);
        UiElement old = surface.RootElement.Children[0];
        fail = true;
        surface.Dispatch(new UiSetNumericInput(surface.RootElement.AutomationId, 0.6, null, new(5)));
        Assert.ThrowsAny<Exception>(surface.UpdateTree);
        Assert.Same(old, surface.RootElement.Children[0]);
        Assert.True(old.IsMounted);
        Assert.ThrowsAny<Exception>(surface.UpdateTree);
        fail = false;
        surface.UpdateLayout();
        Assert.Equal(0.6, ((SliderElement)surface.RootElement).Value);
    }

    /// <summary>Both range thumb identities use accepted state and distinct owner boundaries.</summary>
    [Fact]
    public void RangeThumbsIdentifyLowerAndUpper()
    {
        var values = new List<RangeSliderThumbContext>();
        CompositionTemplate<RangeSliderThumbContext, Widget> template = thumb =>
        {
            values.Add(thumb);
            return Rectangle();
        };
        using UiSurface surface = Create(RangeSlider(value: new SliderRange(0.2, 0.8))[RangeSlider.Thumb()[template]]);
        Assert.Equal(new[] { new RangeSliderThumbContext(0.2, false), new RangeSliderThumbContext(0.8, true) }, values);
        surface.Dispatch(new UiSetNumericInput(surface.RootElement.AutomationId, 0.3, 0.7, new(5)));
        surface.UpdateLayout();
        Assert.Equal(0.3, values[^2].Value, 12);
        Assert.False(values[^2].IsUpper);
        Assert.Equal(0.7, values[^1].Value, 12);
        Assert.True(values[^1].IsUpper);
        Assert.Single(surface.CaptureAutomationSnapshot().Nodes);
    }

    /// <summary>Fixed range parts remain on their respective mounted hosts.</summary>
    [Fact]
    public void RangeFixedPartsAreDistinct()
    {
        Widget lower = Rectangle();
        Widget upper = Rectangle();
        using UiSurface surface = Create(RangeSlider(value: new SliderRange(0.2, 0.8))[
            RangeSlider.LowerThumb()[lower], RangeSlider.UpperThumb()[upper], RangeSlider.Track()[Rectangle()]]);
        UiElement a = Find(surface.RootElement, lower);
        UiElement b = Find(surface.RootElement, upper);
        surface.Dispatch(new UiSetNumericInput(surface.RootElement.AutomationId, 0.3, 0.7, new(5)));
        surface.UpdateLayout();
        Assert.Same(a, Find(surface.RootElement, lower));
        Assert.Same(b, Find(surface.RootElement, upper));
        Widget shared = Rectangle();
        Assert.Throws<InvalidOperationException>(() => new UiSurface(RangeSlider()[RangeSlider.Thumb()[shared]]));
    }

    /// <summary>Generated signal reconstruction retains post-factory template slot assignments.</summary>
    [Fact]
    public void ReactiveArgumentsRetainTemplateSlots()
    {
        var value = Signal.Create(0.2);
        var seen = new List<double>();
        CompositionTemplate<SliderThumbContext, Widget> template = thumb =>
        {
            seen.Add(thumb.Value);
            return Rectangle();
        };
        using UiSurface surface = Create(Slider(value: value)[Slider.Thumb()[template]]);
        value.Value = 0.7;
        surface.UpdateLayout();
        Assert.Equal(0.7, ((SliderElement)surface.RootElement).Value);
        Assert.Equal(0.7, seen[^1]);
        Assert.NotEmpty(surface.RootElement.Children);
    }

    /// <summary>Factory templates are independent across surfaces, and invalid configuration adoption is atomic.</summary>
    [Fact]
    public void IndependentSurfacesAndInvalidAdoption()
    {
        Compose.Definitions.Slider declaration = Slider(value: 0.2)[Slider.Thumb()[_ => Rectangle()]];
        using UiSurface first = Create(declaration);
        using UiSurface second = Create(declaration);
        first.Dispatch(new UiSetNumericInput(first.RootElement.AutomationId, 0.8, null, new(5)));
        first.UpdateLayout();
        Assert.Equal(0.8, ((SliderElement)first.RootElement).Value);
        Assert.Equal(0.2, ((SliderElement)second.RootElement).Value);
        UiElement old = first.RootElement.Children[0];
        first.Rebuild(Slider(value: 0.4)[Slider.Thumb()[_ => throw new InvalidOperationException("adoption")]]);
        Assert.Throws<InvalidOperationException>(first.UpdateTree);
        Assert.Equal(0.8, ((SliderElement)first.RootElement).Value);
        Assert.Same(old, first.RootElement.Children[0]);
        first.Rebuild(Slider(value: 0.4));
        first.UpdateLayout();
        Assert.Empty(first.RootElement.Children);
        Assert.Equal(0.4, ((SliderElement)first.RootElement).Value);
    }

    /// <summary>Owned roots cannot cross sibling hosts or return after their mounted lifetime ends.</summary>
    [Fact]
    public void FixedRootsCannotCrossHostsOrLifetimes()
    {
        Widget shared = Rectangle();
        Assert.Throws<InvalidOperationException>(() => new UiSurface(Stack()[
            Slider()[Slider.Thumb()[shared]], Slider()[Slider.Thumb()[shared]]]));
        Widget fixedWidget = Rectangle();
        using (UiSurface surface = Create(Slider()[Slider.Thumb()[fixedWidget]]))
        {
            Assert.True(Find(surface.RootElement, fixedWidget).IsMounted);
        }

        Assert.Throws<InvalidOperationException>(() => new UiSurface(Slider()[Slider.Thumb()[fixedWidget]]));
    }

    /// <summary>Retained and custom track recording never evaluates templates on unchanged frames.</summary>
    /// <param name="track">Whether only the track is replaced, preserving built-in thumb drawing.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawingDoesNotEvaluateUnchangedTemplates(bool track)
    {
        int evaluations = 0;
        CompositionTemplate<SliderThumbContext, Widget> template = _ =>
        {
            evaluations++;
            return Rectangle();
        };
        Compose.Definitions.Slider declaration = track
            ? Slider()[Slider.Track()[template]] : Slider()[Slider.Thumb()[template]];
        using UiSurface surface = Create(declaration);
        using UiDrawingScene2D drawing = surface.CreateDrawingScene();
        drawing.Update();
        drawing.Update();
        Assert.Equal(1, evaluations);
        surface.Dispatch(new UiSetNumericInput(surface.RootElement.AutomationId, 0.6, null, new(5)));
        drawing.Update();
        Assert.Equal(2, evaluations);
        drawing.Update();
        Assert.Equal(2, evaluations);
    }

    /// <summary>Uncustomized sliders retain the original cached local drawing path.</summary>
    [Fact]
    public void DefaultDrawingRemainsRetained()
    {
        using UiSurface surface = Create(Slider());
        using UiDrawingScene2D drawing = surface.CreateDrawingScene();
        drawing.Update();
        Assert.Equal(1, drawing.LastRecordingCount);
        drawing.Update();
        Assert.Equal(0, drawing.LastRecordingCount);
    }

    /// <summary>Invalid proposed bounds fail before adoption or invoking a visual template.</summary>
    /// <param name="range">Whether the owner is a range slider.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidBoundsNeverEvaluateVisualTemplates(bool range)
    {
        int evaluations = 0;
        using UiSurface surface = Create(range ? RangeSlider() : Slider());
        CompositionTemplate<SliderThumbContext, Widget> sliderTemplate = _ =>
        {
            evaluations++;
            return Rectangle();
        };
        CompositionTemplate<RangeSliderThumbContext, Widget> rangeTemplate = _ =>
        {
            evaluations++;
            return Rectangle();
        };
        surface.Rebuild(range
            ? RangeSlider(min: 2, max: 1)[RangeSlider.Thumb()[rangeTemplate]]
            : Slider(min: 2, max: 1)[Slider.Thumb()[sliderTemplate]]);
        Assert.Throws<ArgumentOutOfRangeException>(surface.UpdateTree);
        Assert.Equal(0, evaluations);
        Assert.Empty(surface.RootElement.Children);
    }

    /// <summary>A fixed visual boundary follows its reactive current declaration across owner refreshes.</summary>
    /// <param name="range">Whether the fixed visual is the lower range thumb.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedReactiveThumbPreservesItsCurrentDeclaration(bool range)
    {
        var width = Signal.Create<float?>(4);
        Widget fixedWidget = Rectangle(width: width);
        Widget declaration = range
            ? RangeSlider()[RangeSlider.LowerThumb()[fixedWidget]]
            : Slider()[Slider.Thumb()[fixedWidget]];
        using UiSurface surface = Create(declaration);
        UiElement visual = Find(surface.RootElement, fixedWidget);
        width.Value = 8;
        surface.UpdateLayout();
        Widget current = visual.Widget;
        Assert.NotSame(fixedWidget, current);
        Assert.Equal(8, current.Width);
        surface.Dispatch(new UiSetNumericInput(surface.RootElement.AutomationId, 0.3, range ? 0.8 : null, new(5)));
        surface.UpdateLayout();
        Assert.Same(current, visual.Widget);
        Assert.True(visual.IsMounted);
        Assert.Equal(8, visual.Widget.Width);
        surface.Rebuild(range
            ? RangeSlider(value: new SliderRange(0.4, 0.9))[RangeSlider.LowerThumb()[fixedWidget]]
            : Slider(value: 0.4)[Slider.Thumb()[fixedWidget]]);
        surface.UpdateLayout();
        Assert.Same(current, visual.Widget);
        Assert.Throws<InvalidOperationException>(() => new UiSurface(declaration));
        surface.Rebuild(range
            ? RangeSlider()[RangeSlider.UpperThumb()[fixedWidget]]
            : Slider()[Slider.Track()[fixedWidget]]);
        Assert.Throws<InvalidOperationException>(surface.UpdateTree);
        Assert.Same(current, visual.Widget);
    }

    private static UiElement Find(UiElement root, Widget widget) => ReferenceEquals(root.Widget, widget)
        ? root : root.Children.SelectMany(Flatten).First(element => ReferenceEquals(element.Widget, widget));

    private static IEnumerable<UiElement> Flatten(UiElement root)
    {
        yield return root;
        foreach (UiElement child in root.Children.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private static UiSurface Create(Widget widget)
    {
        var surface = new UiSurface(widget);
        surface.SetViewport(new(100, 50));
        surface.UpdateLayout();
        return surface;
    }
}
