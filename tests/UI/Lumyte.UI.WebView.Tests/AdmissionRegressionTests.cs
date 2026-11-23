using System.Numerics;
using System.Reflection;
using Xunit;
using WebCompose = Lumyte.UI.WebView.Compose;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Fake-host admission regressions; no native browser is created.</summary>
public sealed class AdmissionRegressionTests
{
    /// <summary>Hidden nodes need no layout override to suspend their host and resume.</summary>
    /// <param name="ancestor">Whether the hidden declaration is an ancestor.</param>
    /// <param name="presence">Whether to change presence instead of visibility.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void HiddenSubtreeSuspendsAndResumesFakeHost(bool ancestor, bool presence)
    {
        var host = new FakeHost();
        UiWebViewSession session = Session(host);
        Lumyte.UI.Compose.Definitions.Widget Tree(bool shown)
        {
            WebCompose.Definitions.WebView view = WebCompose.WebView(session: session);
            Lumyte.UI.Compose.Definitions.Widget target = ancestor ? Lumyte.UI.Compose.Panel()[view] : view;
            target.IsVisible = presence || shown;
            target.IsPresent = !presence || shown;
            return target;
        }

        using var surface = new UiSurface(Tree(true));
        surface.SetViewport(new(100, 80));
        surface.AdvanceAnimations();
        surface.UpdateLayout();
        Assert.True(host.Visible);
        surface.Rebuild(Tree(false));
        surface.AdvanceAnimations();
        surface.UpdateLayout();
        Assert.False(host.Visible);
        surface.Rebuild(Tree(true));
        surface.AdvanceAnimations();
        surface.UpdateLayout();
        Assert.True(host.Visible);
    }

    /// <summary>Presence completion after the frame callback suspends before the next upload phase.</summary>
    [Fact]
    public void PresenceCompletionInAnimationPhaseSuspendsFakeHost()
    {
        var host = new FakeHost();
        WebCompose.Definitions.WebView declaration = WebCompose.WebView(session: Session(host));
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(100, 80));
        surface.UpdateLayout();
        Assert.True(host.Visible);
        UiElement element = surface.RootElement;
        Type entryType = typeof(UiElement).Assembly.GetType("Lumyte.UI.UiPresenceEntry")!;
        object entry = Activator.CreateInstance(entryType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [surface, element], null)!;
        typeof(UiElement).GetField("_presence", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(element, entry);
        entryType.GetMethod("Seed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(entry, [true]);
        typeof(UiSurface).GetMethod("AddPresence", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, [entry]);

        // Fake an already-running zero-duration exit, completed after frame advancement.
        entryType.GetField("_started", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(entry, true);
        entryType.GetField("_requested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(entry, false);
        entryType.GetField("_transition", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(entry, new UiVisualTransition(new UiVisualPose(), new UiVisualPose(), Lumyte.Core.Time.Duration.Zero));
        entryType.GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(entry, UiPresenceState.Exiting);
        declaration.IsPresent = false;
        surface.AdvanceAnimations();
        Assert.Equal(UiPresenceState.Hidden, element.PresenceState);
        Assert.False(host.Visible);
    }

    /// <summary>Window freeze immediately suspends the fake host, independently of layout.</summary>
    [Fact]
    public void WindowFreezeSuspendsAndResumesFakeHost()
    {
        var host = new FakeHost();
        using var surface = new UiSurface(WebCompose.WebView(session: Session(host)));
        surface.SetViewport(new(100, 80));
        surface.AdvanceAnimations();
        surface.UpdateLayout();
        Assert.True(host.Visible);
        surface.SetAnimationUpdatesEnabled(false);
        Assert.False(host.Visible);
        surface.SetViewport(new(120, 90));
        surface.UpdateLayout();
        Assert.False(host.Visible);
        surface.SetAnimationUpdatesEnabled(true);
        surface.AdvanceAnimations();
        Assert.True(host.Visible);
    }

    /// <summary>A new participant mounted while the window is frozen stays suspended until resume.</summary>
    [Fact]
    public void NewMountDuringWindowFreezeStaysSuspendedUntilResume()
    {
        var host = new FakeHost();
        using var surface = new UiSurface(Lumyte.UI.Compose.Panel());
        surface.SetViewport(new(100, 80));
        surface.SetAnimationUpdatesEnabled(false);
        surface.Rebuild(WebCompose.WebView(session: Session(host)));
        surface.UpdateLayout();
        Assert.False(host.Visible);
        surface.AdvanceAnimations();
        surface.SetAnimationUpdatesEnabled(false);
        Assert.False(host.Visible);
        surface.SetAnimationUpdatesEnabled(true);
        surface.AdvanceAnimations();
        Assert.True(host.Visible);
    }

    /// <summary>A foreign but unattached session is rejected before declaration and input publication.</summary>
    [Fact]
    public void ForeignSurfaceSwitchPreservesDeclarationFocusOwnerAndSubscribers()
    {
        var host = new FakeHost();
        UiWebViewSession old = Session(host);
        using var surface = new UiSurface(WebCompose.WebView(session: old));
        surface.SetViewport(new(100, 80));
        surface.UpdateLayout();
        Assert.True(surface.TryFocus(surface.RootElement));
        Lumyte.UI.Compose.Definitions.Widget declaration = surface.Root;
        Delegate? subscribers = Subscribers(old);
        var foreignHost = new FakeHost { ExpectedSurface = new UiSurface(Lumyte.UI.Compose.Panel()) };
        using UiSurface other = foreignHost.ExpectedSurface;
        UiWebViewSession foreign = Session(foreignHost);
        Assert.Throws<InvalidOperationException>(() =>
        {
            surface.Rebuild(WebCompose.WebView(session: foreign));
            surface.UpdateLayout();
        });
        Assert.Same(declaration, surface.Root);
        Assert.Same(surface.RootElement, surface.FocusedElement);
        Assert.True(host.Focused);
        Assert.False(host.Detached);
        Assert.Equal(subscribers, Subscribers(old));
        Assert.Throws<InvalidOperationException>(() => old.Attach(new object()));
        object probe = new();
        foreign.Attach(probe);
        foreign.Detach(probe);
    }

    /// <summary>A first mount is rejected without acquiring ownership or subscriptions.</summary>
    [Fact]
    public void ForeignSurfaceInitialMountDoesNotPublishSession()
    {
        using var expected = new UiSurface(Lumyte.UI.Compose.Panel());
        var host = new FakeHost { ExpectedSurface = expected };
        UiWebViewSession session = Session(host);
        Assert.Throws<InvalidOperationException>(() => new UiSurface(WebCompose.WebView(session: session)));
        Assert.Null(Subscribers(session));
        Assert.False(host.Detached);
        object probe = new();
        session.Attach(probe);
        session.Detach(probe);
    }

    private static UiWebViewSession Session(FakeHost host) => new(new("https://example.invalid/"), () => { }, _ => { }) { Host = host };

    private static Delegate? Subscribers(UiWebViewSession session) => (Delegate?)typeof(UiWebViewSession).GetField("PixelsChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session);

    private sealed class FakeHost : IWebViewHost
    {
        internal UiSurface? ExpectedSurface { get; init; }

        internal bool Visible { get; private set; }

        internal bool Focused { get; private set; }

        internal bool Detached { get; private set; }

        public void ValidateSurface(UiSurface surface)
        {
            if (ExpectedSurface is not null && !ReferenceEquals(ExpectedSurface, surface))
            {
                throw new InvalidOperationException("fake-foreign-surface");
            }
        }

        public void Viewport(Vector2 logicalSize, bool visible) => Visible = visible;

        public void Input(UiInput input, Vector2? localPosition)
        {
        }

        public void Focus(bool focused) => Focused = focused;

        public void Detach() => Detached = true;
    }
}
