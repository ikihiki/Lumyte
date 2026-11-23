using System.Numerics;
using Lumyte.Core.Time;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks host ambient context survives component-owned refresh and projected viewport adoption.</summary>
public sealed class OwnedContentAmbientMergeTests
{
    /// <summary>Owned roots retain the host provider, respect nearer providers, and discard removed overrides.</summary>
    /// <param name="viewport">Whether refresh uses the unpublished viewport proposal path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedRefreshKeepsHostAmbientAndNearestProvider(bool viewport)
    {
        var key = new UiContextKey<string>();
        var environment = UiUseEnvironment.Provide(null, key, "window");
        using var surface = new UiSurface(new Host(key, viewport), new Dispatcher(), new SystemMonotonicClock(), environment);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        AssertValue("window");
        Refresh();
        AssertValue("window");
        surface.Rebuild(Provide(key, "nearer", new Host(key, viewport)));
        surface.UpdateLayout();
        AssertValue("nearer");
        Refresh();
        AssertValue("nearer");
        surface.Rebuild(new Host(key, viewport));
        surface.UpdateLayout();
        AssertValue("window");
        Refresh();
        AssertValue("window");

        void Refresh()
        {
            Assert.IsType<HostElement>(surface.RootElement).Request();
            surface.UpdateTree();
            surface.UpdateLayout();
        }

        void AssertValue(string expected)
        {
            HostElement owner = Assert.IsType<HostElement>(surface.RootElement);
            Assert.Equal(expected, owner.AcceptedValue);
            Assert.Same(surface, owner.PreparedSurface);
            UiFunctionElement reader = Assert.IsAssignableFrom<UiFunctionElement>(Assert.Single(owner.Children));
            Assert.Equal(expected, reader.Content.Widget.AutomationName);
        }
    }

    private sealed class Host(UiContextKey<string> key, bool viewport) : Widget
    {
        internal UiContextKey<string> ContextKey { get; } = key;

        internal override bool RequiresViewportPreparation => viewport;

        protected override UiElement CreateElement() => new HostElement(this);
    }

    private sealed class HostElement(Host widget) : UiElement<Host>(widget)
    {
        private bool _viewportRequested;

        internal string? AcceptedValue { get; private set; }

        internal UiSurface? PreparedSurface { get; private set; }

        internal void Request()
        {
            if (Widget.RequiresViewportPreparation)
            {
                _viewportRequested = true;
            }
            else
            {
                InvalidateContent();
            }
        }

        internal override bool PrepareViewportContent()
        {
            if (!_viewportRequested)
            {
                return false;
            }

            _viewportRequested = false;
            PreparationSurface!.RefreshElementContent(this);
            return true;
        }

        protected override UiElementContent? BuildContent(Widget next, UiElementBuildContext context)
        {
            UiContextKey<string> key = ((Host)next).ContextKey;
            string value = UiUseEnvironment.Read(context.Environment, key);
            return new(HookTestUi.Reader(key), ownsContent: true, onCommitted: () => AcceptedValue = value);
        }

        protected override Action? PrepareUpdate(Host next, UiElementUpdateContext context)
        {
            PreparedSurface = context.Surface;
            return null;
        }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            Vector2 size = constraints.Constrain(new(100));
            foreach (UiElement child in Children)
            {
                LayoutChild(child, UiBoxConstraints.Tight(size));
                SetChildOffset(child, Vector2.Zero);
            }

            return size;
        }
    }

    private sealed class Dispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            action();
            return ValueTask.CompletedTask;
        }

        public ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default) => ValueTask.FromResult(action());
    }
}
