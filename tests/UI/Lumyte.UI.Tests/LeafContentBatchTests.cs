using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks instance-owned content can leave the leaf update path without repeating preparation.</summary>
public sealed class LeafContentBatchTests
{
    /// <summary>Checks a newly created content slot is built once and published with surrounding leaves.</summary>
    [Fact]
    public void FirstContentBuildIsReusedByTheGeneralPlan()
    {
        var width = Signal.Create<float?>(10);
        var key = new UiContextKey<string>();
        Widget content = Provide(key, "inherited", new ContentLeaf(width, contextKey: key));
        using var surface = new UiSurface(Panel()[Rectangle(width: width), content, Rectangle(width: width)]);
        ContentElement element = Assert.IsType<ContentElement>(surface.RootElement.Children[1]);
        Assert.Equal(1, element.Builds);
        Assert.Empty(element.Children);
        width.Value = 20;
        surface.UpdateTree();
        Assert.Same(element, surface.RootElement.Children[1]);
        Assert.Equal(2, element.Builds);
        Assert.Equal("inherited", Assert.Single(element.Children).Widget.AutomationName);
        Assert.All(surface.RootElement.Children, child => Assert.Equal(20, child.Widget.Width));
        Assert.Equal(20, element.Children[0].Widget.Width);
        width.Value = 30;
        surface.UpdateTree();
        Assert.Equal(3, element.Builds);
        Assert.Equal(30, Assert.Single(element.Children).Widget.Width);
    }

    /// <summary>Checks a failed content preparation releases a newly connected animation producer.</summary>
    [Fact]
    public void ThrowingContentReleasesPreparedAnimationConnection()
    {
        var replace = Signal.Create(false);
        var target = Signal.Create<float?>(20);
        UiAnimated<float?> animated = Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float));
        using var surface = new UiSurface(Compute(() => new ContentLeaf(replace.Value ? animated : UiArgument<float?>.Constant(10), fail: replace.Value)));
        Widget previous = surface.Root;
        replace.Value = true;
        Assert.Throws<UiStateUpdateException>(surface.UpdateTree);
        Assert.Same(previous, surface.Root);
        Assert.False(((IUiAnimationProjection)animated).Producer.IsConnected);
    }

    private sealed class ContentLeaf : Widget
    {
        private readonly UiArgument<float?> _width;

        internal ContentLeaf(UiArgument<float?> width, bool fail = false, UiContextKey<string>? contextKey = null)
        {
            _width = width;
            Fail = fail;
            ContextKey = contextKey;
            Width = width.GetInitialValue();
            width.AttachTo(this, nameof(Width), Width, static (widget, value) => widget.Width = value);
        }

        internal bool Fail { get; }

        internal UiContextKey<string>? ContextKey { get; }

        protected override UiElement CreateElement() => new ContentElement(this);

        protected override Widget RebuildDeclaration(UiWidgetRebuildContext context) => new ContentLeaf(_width, Fail, ContextKey);
    }

    private sealed class ContentElement(ContentLeaf widget) : UiElement<ContentLeaf>(widget)
    {
        internal int Builds { get; private set; }

        protected override UiElementContent? BuildContent(Widget next, UiElementBuildContext context)
        {
            Builds++;
            if (((ContentLeaf)next).Fail)
            {
                throw new InvalidOperationException("Content rejected.");
            }

            if (next.Width <= 10)
            {
                return null;
            }

            UiContextKey<string>? key = ((ContentLeaf)next).ContextKey;
            string? name = key is null ? null : UiUseEnvironment.Read(context.Environment, key);
            return new(Rectangle(width: next.Width, automationName: name));
        }
    }
}
