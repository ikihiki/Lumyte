using System.Numerics;
using Lumyte.Core.Time;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls.Tests;

/// <summary>Verifies ordinary content composition and owner-loop notification lifetime.</summary>
public sealed class DialogToastRecipeTests
{
    /// <summary>Named sections are optional, single-root and laid out once in remaining space.</summary>
    [Fact]
    public void DialogSectionsUseOrdinaryOwnershipAndFiniteLayout()
    {
        Compose.Definitions.DialogContent declaration = DialogContent()[DialogContent.Header()[Rectangle(width: 80, height: 20)], DialogContent.Content()[Button(width: 60, height: 40)], DialogContent.Actions()[Button(width: 50, height: 20)]];
        using UiSurface surface = Surface(declaration);
        Assert.Equal(new Vector2(200, 200), surface.RootElement.Size);
        Assert.Equal(20, surface.RootElement.Children[1].Bounds.Y);
        Assert.Equal(60, surface.RootElement.Children[2].Bounds.Y);
        Assert.Throws<ArgumentException>(() => DialogContent()[DialogContent.Header()[Button(), Button()]]);
        Assert.Throws<ArgumentException>(() => DialogContent()[Button()]);
        using UiSurface second = Surface(declaration);
        Assert.NotSame(surface.RootElement.Children[1], second.RootElement.Children[1]);
        Assert.Null(surface.FocusedElement);
    }

    /// <summary>Dialog sections preserve the incoming generator's component-owned typed visual slots.</summary>
    [Fact]
    public void DialogSectionsComposeCurrentTypedSliderSlots()
    {
        var values = new List<double>();
        Lumyte.Composition.CompositionTemplate<SliderThumbContext, Widget> thumb = context =>
        {
            values.Add(context.Value);
            return Rectangle(width: 10, height: 10);
        };
        using UiSurface surface = Surface(DialogContent()[DialogContent.Content()[Slider(value: 0.2)[Slider.Thumb()[thumb]]]]);
        Assert.Equal(0.2, Assert.Single(values));
        surface.Rebuild(DialogContent()[DialogContent.Content()[Slider(value: 0.8)[Slider.Thumb()[thumb]]]]);
        surface.UpdateLayout();
        Assert.Equal(0.8, values[^1]);
    }

    /// <summary>Recipes use existing components and never rewrite borrowed editor names.</summary>
    [Fact]
    public void RecipesAreOrdinaryRootsAndPreserveCallerDeclarations()
    {
        Widget editor = Button(automationName: "editor");
        Widget field = UiRecipes.PropertyField(Rectangle(width: 10, height: 10), editor, Rectangle(width: 10, height: 10));
        using UiSurface surface = Surface(field);
        Assert.Equal("editor", editor.AutomationName);
        Assert.IsType<Lumyte.UI.Compose.Definitions.Stack>(field);
        Assert.IsType<Lumyte.UI.Compose.Definitions.Button>(UiRecipes.IconButton(Rectangle(), "save"));
        Assert.Throws<ArgumentException>(() => UiRecipes.IconButton(Rectangle(), " "));
        Assert.Throws<ArgumentException>(() => UiRecipes.Toolbar([editor, editor]));
        using UiSurface empty = Surface(UiRecipes.ValidationMessage(null));
        Assert.Empty(empty.RootElement.Children);
        Assert.IsType<Lumyte.UI.Compose.Definitions.Stack>(UiRecipes.Card(Rectangle()));
        Assert.IsType<Lumyte.UI.Compose.Definitions.Stack>(UiRecipes.StatusBar([Rectangle()]));
        Assert.IsType<Lumyte.UI.Compose.Definitions.Stack>(UiRecipes.EmptyState(Rectangle(), Button()));
    }

    /// <summary>Only the admitted factory runs; expiry starts at admission, not enqueue.</summary>
    [Fact]
    public void NotificationsAreBoundedFifoAndExpireOnBorrowedClock()
    {
        var clock = new Clock();
        using var controller = new ToastController(clock, 2);
        int factories = 0;
        int closed = 0;
        ToastId first = controller.Enqueue(
        () =>
        {
            factories++;
            return Toast()[Toast.Content()[Rectangle(width: 30, height: 10)]];
        },
        TimeSpan.FromSeconds(1),
        _ => closed++);
        ToastId second = controller.Enqueue(
        () =>
        {
            factories++;
            return Toast();
        },
        TimeSpan.FromSeconds(1),
        _ => closed++);
        Assert.Equal(0, factories);
        Assert.Throws<InvalidOperationException>(() => controller.Enqueue(() => Button(), TimeSpan.Zero));
        using UiSurface surface = Surface(ToastHost(controller));
        Assert.Equal(1, factories);
        Assert.True(controller.CaptureSnapshot()[0].IsVisible);
        UiElement firstGeneration = surface.RootElement.Children[0];
        controller.Advance();
        clock.Now = TimePoint.FromTicks(TimeSpan.FromSeconds(10).Ticks);
        controller.Advance();
        surface.UpdateLayout();
        Assert.Equal(2, factories);
        Assert.NotSame(firstGeneration, surface.RootElement.Children[0]);
        Assert.Equal(1, closed);
        Assert.Equal(second, Assert.Single(controller.CaptureSnapshot()).Id);
        controller.Advance();
        Assert.Single(controller.CaptureSnapshot());
        clock.Now += Duration.FromSeconds(1);
        controller.Advance();
        Assert.Empty(controller.CaptureSnapshot());
        Assert.Equal(2, closed);
        Assert.False(controller.Dismiss(first));
        Assert.False(surface.HasRunningAnimations);
    }

    /// <summary>Factory failures preserve the old mounted toast and accepted queue order.</summary>
    [Fact]
    public void FailedAdmissionPreservesPreviousTreeAndCanBeDismissed()
    {
        using var controller = new ToastController(new Clock());
        ToastId first = controller.Enqueue(() => Toast(), TimeSpan.Zero);
        int attempts = 0;
        ToastId bad = controller.Enqueue(
        () =>
        {
            attempts++;
            throw new InvalidOperationException("factory");
        },
        TimeSpan.Zero);
        using UiSurface surface = Surface(ToastHost(controller));
        UiElement old = surface.RootElement.Children[0];
        Assert.Throws<InvalidOperationException>(() => controller.Dismiss(first));
        Assert.Same(old, surface.RootElement.Children[0]);
        Assert.Equal(2, controller.CaptureSnapshot().Count);
        Assert.Throws<InvalidOperationException>(() => controller.Dismiss(first));
        Assert.Equal(1, attempts);
        Assert.True(controller.Dismiss(bad));
        Assert.True(controller.Dismiss(first));
        Assert.Empty(controller.CaptureSnapshot());
    }

    /// <summary>One controller cannot be shared across surfaces; failed reservations roll back.</summary>
    [Fact]
    public void HostReservationRejectsSharingBeforeFactoriesAndRollsBack()
    {
        using var controller = new ToastController(new Clock());
        using UiSurface surface = Surface(ToastHost(controller));
        Assert.Throws<InvalidOperationException>(() => Surface(ToastHost(controller)));
        surface.Dispose();
        using UiSurface replacement = Surface(ToastHost(controller));
        Assert.Null(replacement.FocusedElement);
    }

    /// <summary>Closure callbacks observe accepted state and may enqueue without iterating a live collection.</summary>
    [Fact]
    public void ClearUsesSnapshotAndCallbacksMayReenterOrThrow()
    {
        using var controller = new ToastController(new Clock());
        int callbacks = 0;
        controller.Enqueue(() => Toast(), TimeSpan.Zero, _ =>
        {
            callbacks++;
            controller.Enqueue(() => Toast(), TimeSpan.Zero);
            throw new InvalidOperationException();
        });
        controller.Enqueue(() => Toast(), TimeSpan.Zero, _ => callbacks++);
        using UiSurface surface = Surface(ToastHost(controller));
        Assert.Throws<AggregateException>(controller.Clear);
        Assert.Equal(2, callbacks);
        Assert.Single(controller.CaptureSnapshot());
        controller.Clear();
        Assert.Equal(2, callbacks);
    }

    /// <summary>Detached and mounted roots cannot become a fresh notification payload.</summary>
    [Fact]
    public void ReusedContentIsRejectedWithoutDiscardingTheOldToast()
    {
        Widget reused = Toast();
        using (UiSurface prior = Surface(reused))
        {
        }

        using var controller = new ToastController(new Clock());
        ToastId first = controller.Enqueue(() => Toast(), TimeSpan.Zero);
        controller.Enqueue(() => reused, TimeSpan.Zero);
        using UiSurface surface = Surface(ToastHost(controller));
        UiElement old = surface.RootElement.Children[0];
        Assert.Throws<InvalidOperationException>(() => controller.Dismiss(first));
        Assert.Same(old, surface.RootElement.Children[0]);
    }

    /// <summary>Queue publication preserves the exact accepted declaration, including caller modifiers.</summary>
    [Fact]
    public void NotificationRefreshPreservesAcceptedHostConfiguration()
    {
        using var controller = new ToastController(new Clock());
        Compose.Definitions.ToastHost host = ToastHost(controller, width: 80, height: 60, with: [Padding.All(5)]);
        using UiSurface surface = Surface(host);
        ToastId id = controller.Enqueue(() => Toast(), TimeSpan.Zero);
        Assert.Same(host, surface.Root);
        Assert.True(controller.Dismiss(id));
        Assert.Same(host, surface.Root);
        Assert.Equal(80, surface.Root.Width);
    }

    /// <summary>A committed removal still delivers its once-only callback if unmount notification throws.</summary>
    [Fact]
    public void CommittedRemovalDeliversCallbackAfterUnmountFailure()
    {
        using var controller = new ToastController(new Clock());
        int callbacks = 0;
        ToastId id = controller.Enqueue(() => new ThrowUnmount(), TimeSpan.Zero, _ => callbacks++);
        using UiSurface surface = Surface(ToastHost(controller));
        Assert.ThrowsAny<Exception>(() => controller.Dismiss(id));
        Assert.Equal(1, callbacks);
        Assert.Empty(controller.CaptureSnapshot());
        Assert.False(controller.Dismiss(id));
    }

    /// <summary>Freshness includes old detached descendants produced by ordinary or dynamic content.</summary>
    /// <param name="dynamic">Whether the old child appears only after function preparation.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshToastRejectsDetachedPreparedDescendants(bool dynamic)
    {
        Widget old = Button();
        using (UiSurface prior = Surface(old))
        {
        }

        using var controller = new ToastController(new Clock());
        ToastId first = controller.Enqueue(() => Toast(), TimeSpan.Zero);
        controller.Enqueue(() => dynamic ? new ReuseFunction(old) : Panel()[old], TimeSpan.Zero);
        using UiSurface surface = Surface(ToastHost(controller));
        UiElement retained = surface.RootElement.Children[0];
        Assert.Throws<InvalidOperationException>(() => controller.Dismiss(first));
        Assert.Same(retained, surface.RootElement.Children[0]);
        Assert.Equal(2, controller.CaptureSnapshot().Count);
    }

    /// <summary>Hidden retained hosts defer expiry mutation; queued lifetime begins when the next item is admitted.</summary>
    [Fact]
    public void HiddenHostDefersExpiryUntilReactivation()
    {
        var clock = new Clock();
        using var controller = new ToastController(clock);
        controller.Enqueue(() => Toast(), TimeSpan.FromSeconds(1));
        ToastId next = controller.Enqueue(() => Toast(), TimeSpan.FromSeconds(1));
        using UiSurface surface = Surface(TabView(selectedKey: "host")[TabItem(key: "host")[TabItem.Content()[ToastHost(controller)]], TabItem(key: "other")[TabItem.Content()[Button()]]]);
        controller.Advance();
        surface.Dispatch(new UiPointerInput(122, UiPointerPhase.Down, new(5, 30)));
        surface.Dispatch(new UiPointerInput(122, UiPointerPhase.Up, new(5, 30)));
        surface.UpdateLayout();
        clock.Now += Duration.FromSeconds(10);
        controller.Advance();
        Assert.Equal(2, controller.CaptureSnapshot().Count);
        Assert.False(controller.CaptureSnapshot()[0].IsVisible);
        surface.Dispatch(new UiPointerInput(123, UiPointerPhase.Down, new(5)));
        surface.Dispatch(new UiPointerInput(123, UiPointerPhase.Up, new(5)));
        surface.UpdateLayout();
        surface.AdvanceAnimations();
        Assert.Equal(next, Assert.Single(controller.CaptureSnapshot()).Id);
        controller.Advance();
        Assert.Single(controller.CaptureSnapshot());
    }

    /// <summary>Label association is explicit read-only metadata and leaves shared editor names untouched.</summary>
    [Fact]
    public void FormRowPreservesExplicitAssociationWithoutEditorMutation()
    {
        Widget editor = Button(automationName: "original");
        Widget row = UiRecipes.FormRow(Rectangle(), editor, "Application name");
        using UiSurface first = Surface(row);
        using UiSurface second = Surface(row);
        Assert.Equal(new UiLabelAssociation(0, 1, "Application name"), row.LabelAssociation);
        Assert.Equal("original", editor.AutomationName);
        Assert.NotSame(first.RootElement.Children[1], second.RootElement.Children[1]);
    }

    /// <summary>A factory cannot publish into an old host after requesting a newer root replacement.</summary>
    [Fact]
    public void FactoryRootReplacementSupersedesLocalToastPublication()
    {
        using var controller = new ToastController(new Clock());
        ToastId first = controller.Enqueue(() => Toast(), TimeSpan.Zero);
        using UiSurface surface = Surface(ToastHost(controller));
        UiElement old = surface.RootElement.Children[0];
        int factories = 0;
        controller.Enqueue(
        () =>
        {
            factories++;
            surface.Rebuild(Panel());
            return Toast();
        },
        TimeSpan.Zero);
        Assert.Throws<InvalidOperationException>(() => controller.Dismiss(first));
        Assert.Same(old, surface.RootElement.Children[0]);
        Assert.Equal(1, factories);
        surface.UpdateTree();
        Assert.IsType<Lumyte.UI.Compose.Definitions.Panel>(surface.Root);
        Assert.Empty(controller.CaptureSnapshot());
    }

    /// <summary>Time spent in a staged, nonpresented surface cannot consume the first visible lifetime.</summary>
    [Fact]
    public void InvisiblePreparationDoesNotSpendVisibleToastLifetime()
    {
        var clock = new Clock();
        using var controller = new ToastController(clock);
        controller.Enqueue(() => Toast(), TimeSpan.FromSeconds(1));
        using UiSurface surface = Surface(ToastHost(controller));
        surface.SetAnimationUpdatesEnabled(false);
        clock.Now += Duration.FromSeconds(10);
        controller.Advance();
        Assert.Single(controller.CaptureSnapshot());
        surface.SetAnimationUpdatesEnabled(true);
        controller.Advance();
        Assert.Single(controller.CaptureSnapshot());
        clock.Now += Duration.FromSeconds(1);
        controller.Advance();
        Assert.Empty(controller.CaptureSnapshot());
    }

    /// <summary>Header/body descendants cannot take focus or pointer input; ordinary action descendants remain interactive.</summary>
    [Fact]
    public void ToastBodyIsPassiveAndActionsUseNormalInput()
    {
        int ownerClicks = 0;
        int bodyClicks = 0;
        int actionClicks = 0;
        Widget body = Button(testId: "body", width: 40, height: 20, onClick: () => bodyClicks++);
        Widget actions = Button(testId: "action", width: 40, height: 20, onClick: () => actionClicks++);
        using UiSurface surface = Surface(Panel()[Button(width: 100, height: 100, onClick: () => ownerClicks++), Toast()[Toast.Content()[body], Toast.Actions()[actions]]]);
        UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
        Assert.False(surface.TryFocus(surface.FindElement(snapshot.Nodes.Single(node => node.TestId == "body").Id)!));
        surface.Dispatch(new UiPointerInput(501, UiPointerPhase.Down, new(5)));
        surface.Dispatch(new UiPointerInput(501, UiPointerPhase.Up, new(5)));
        surface.Dispatch(new UiPointerInput(502, UiPointerPhase.Down, new(5, 25)));
        surface.Dispatch(new UiPointerInput(502, UiPointerPhase.Up, new(5, 25)));
        Assert.Equal(1, ownerClicks);
        Assert.Equal(0, bodyClicks);
        Assert.Equal(1, actionClicks);
        Assert.True(body.Focusable);
    }

    /// <summary>A stopped retained host remains empty through ordinary reactive reconciliation.</summary>
    [Fact]
    public void DisposedControllerRetainedHostReconcilesWithoutRevivingWork()
    {
        var width = Lumyte.Signals.Signal.Create<float?>(100f);
        var siblingWidth = Lumyte.Signals.Signal.Create<float?>(40f);
        var clock = new Clock();
        using var controller = new ToastController(clock);
        int closed = 0;
        int clicks = 0;
        ToastId stopped = controller.Enqueue(() => Toast(), TimeSpan.FromSeconds(1), _ => closed++);
        controller.Enqueue(() => Toast(), TimeSpan.FromSeconds(1), _ => closed++);
        using UiSurface surface = Surface(Panel()[Button(width: siblingWidth, height: 40, onClick: () => clicks++), ToastHost(controller, width: width)]);
        UiElement host = surface.RootElement.Children[1];
        controller.Advance();
        controller.Dispose();
        int clockReads = clock.Reads;
        width.Value = 120;
        siblingWidth.Value = 50;
        surface.UpdateLayout();
        Assert.Same(host, surface.RootElement.Children[1]);
        Assert.Equal(50, surface.RootElement.Children[0].Size.X);
        surface.AdvanceAnimations();
        Assert.False(surface.HasRunningAnimations);
        Assert.Equal(clockReads, clock.Reads);
        surface.Dispatch(new UiPointerInput(601, UiPointerPhase.Down, new(5)));
        surface.Dispatch(new UiPointerInput(601, UiPointerPhase.Up, new(5)));
        Assert.Equal(1, clicks);
        Assert.Equal(0, closed);
        Assert.Throws<ObjectDisposedException>(() => controller.Enqueue(() => Toast(), TimeSpan.Zero));
        Assert.Throws<ObjectDisposedException>(() => controller.Dismiss(stopped));
        Assert.Throws<ObjectDisposedException>(() => controller.CaptureSnapshot());
        Assert.Throws<ObjectDisposedException>(() => controller.Clear());
        Assert.Throws<ObjectDisposedException>(() => controller.Advance());
        Assert.Throws<ObjectDisposedException>(() => Surface(ToastHost(controller)));
        using var replacement = new ToastController(clock);
        surface.Rebuild(Panel()[Button(width: 50, height: 40), ToastHost(replacement, width: 120)]);
        surface.UpdateLayout();
        Assert.Same(host, surface.RootElement.Children[1]);
        replacement.Enqueue(() => Toast(), TimeSpan.FromSeconds(1));
        Assert.Single(replacement.CaptureSnapshot());
    }

    private static UiSurface Surface(Widget root)
    {
        var surface = new UiSurface(root);
        surface.SetViewport(new(200, 200));
        surface.UpdateLayout();
        return surface;
    }

    private sealed class ThrowUnmount : Widget
    {
        protected override UiElement CreateElement() => new ThrowUnmountElement(this);
    }

    private sealed class ThrowUnmountElement(Widget widget) : UiElement(widget)
    {
        protected override void OnUnmounted() => throw new InvalidOperationException("unmount");
    }

    private sealed class ReuseFunction(Widget content) : UiFunctionWidget
    {
        protected override Widget Build() => content;
    }

    private sealed class Clock : IMonotonicClock
    {
        private TimePoint _now;

        public TimePoint Now
        {
            get
            {
                Reads++;
                return _now;
            }

            set => _now = value;
        }

        internal int Reads { get; private set; }
    }
}
