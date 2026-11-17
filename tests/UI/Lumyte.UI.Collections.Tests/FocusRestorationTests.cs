using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Collections.Tests;

/// <summary>Checks explicit intent between tree publication and queued focus restoration.</summary>
public sealed class FocusRestorationTests
{
    /// <summary>Successful requests win; unsuccessful requests preserve restoration.</summary>
    /// <param name="request">The intervening focus request.</param>
    [Theory]
    [InlineData("sibling")]
    [InlineData("clear")]
    [InlineData("navigate")]
    [InlineData("failed")]
    [InlineData("detach")]
    [InlineData("callback")]
    [InlineData("nested-clear")]
    [InlineData("bookmark")]
    [InlineData("foreign")]
    [InlineData("stale")]
    public void NewerFocusIntentSupersedesItemRestoration(string request)
    {
        Action? gained = null;
        Lumyte.UI.Compose.Definitions.Widget Tree() => Stack()[
            ListBox<string>(items: new[] { "a", "b" }, keySelector: x => x, itemTemplate: _ => SizedBox(width: 100, height: 20)),
            new FocusButton(() => gained?.Invoke())];
        using var surface = new UiSurface(Tree());
        surface.SetViewport(new(200, 160));
        surface.UpdateLayout();
        UiElement previous = surface.RootElement.Children[0].Children[0].Children[0].Children[1];
        UiElement sibling = surface.RootElement.Children[1];
        Assert.True(surface.TryFocus(previous));
        UiFocusBookmark bookmark = surface.CaptureFocus();
        surface.Rebuild(Tree());
        surface.UpdateTree();
        Assert.Same(sibling, surface.RootElement.Children[1]);
        UiElement replacement = surface.RootElement.Children[0].Children[0].Children[0].Children[1];
        switch (request)
        {
            case "sibling":
                Assert.True(surface.TryFocus(sibling));
                break;
            case "callback":
                gained = () => Assert.True(surface.TryFocus(null));
                Assert.True(surface.TryFocus(sibling));
                break;
            case "nested-clear":
                gained = () =>
                {
                    Assert.True(surface.TryFocus(null));
                    surface.UpdateLayout();
                };
                Assert.True(surface.TryFocus(sibling));
                break;
            case "bookmark":
                Assert.True(surface.TryRestoreFocus(surface.CaptureFocus()));
                break;
            case "clear":
                Assert.True(surface.TryFocus(null));
                break;
            case "navigate":
                Assert.True(surface.MoveFocus(UiFocusNavigation.Last));
                break;
            case "failed":
                Assert.False(surface.TryFocus(surface.RootElement));
                break;
            case "foreign":
                using (var foreign = new UiSurface(Button()))
                {
                    Assert.Throws<ArgumentException>(() => surface.TryFocus(foreign.RootElement));
                }

                break;
            case "stale":
                Assert.False(surface.TryRestoreFocus(bookmark));
                break;
            case "detach":
                surface.Rebuild(Button());
                surface.UpdateTree();
                break;
        }

        surface.UpdateLayout();
        Assert.Same(request is "clear" or "detach" or "callback" or "nested-clear" or "bookmark" ? null : request is "failed" or "foreign" or "stale" ? replacement : sibling, surface.FocusedElement);
        Assert.NotSame(previous, surface.FocusedElement);
    }

    /// <summary>A later template callback cannot reintroduce intent captured by an earlier list preparation.</summary>
    [Fact]
    public void NewerRequestDuringPreparationPreventsLateQueuePublication()
    {
        Action? preparing = null;
        Lumyte.UI.Compose.Definitions.Widget Tree() => Stack()[
            ListBox<string>(items: new[] { "a", "b" }, keySelector: x => x, itemTemplate: _ => SizedBox(width: 100, height: 20)),
            Button(width: 100, height: 20),
            ItemsRepeater<string>(items: new[] { "tail" }, keySelector: x => x, itemTemplate: _ =>
            {
                preparing?.Invoke();
                return SizedBox();
            })];
        using var surface = new UiSurface(Tree());
        surface.SetViewport(new(200, 160));
        surface.UpdateLayout();
        UiElement previous = surface.RootElement.Children[0].Children[0].Children[0].Children[1];
        UiElement sibling = surface.RootElement.Children[1];
        Assert.True(surface.TryFocus(previous));
        preparing = () => Assert.True(surface.TryFocus(sibling));
        surface.Rebuild(Tree());
        surface.UpdateLayout();
        Assert.Same(sibling, surface.FocusedElement);
    }

    private sealed class FocusButton(Action gained) : Lumyte.UI.Compose.Definitions.Button
    {
        protected override UiElement CreateElement() => new FocusButtonElement(this, gained);
    }

    private sealed class FocusButtonElement(Lumyte.UI.Compose.Definitions.Button widget, Action gained) : ButtonElement(widget)
    {
        protected override UiInputResult OnInput(in UiInputContext context)
        {
            if (context.Input is UiFocusChangedInput { IsFocused: true })
            {
                gained();
            }

            return base.OnInput(in context);
        }
    }
}
