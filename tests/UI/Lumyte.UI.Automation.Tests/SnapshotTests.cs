using System.Numerics;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Automation.Tests;

/// <summary>Checks semantic copies, connection identity and side-effect-free pointer routing.</summary>
public sealed class SnapshotTests
{
    /// <summary>Checks generated arguments, component roles, background exclusion and detached immutable state.</summary>
    [Fact]
    public void SnapshotCopiesSemanticsAndExcludesButtonBackground()
    {
        Definitions.Button button = Button(testId: "save", automationName: "保存", width: 20, height: 10)[Rectangle(testId: "content"), Button.Background()[Rectangle(testId: "decoration")]];
        using UiSurface surface = Surface(Panel(testId: "root")[button]);
        UiAutomationSnapshot before = surface.CaptureAutomationSnapshot();
        Assert.Equal(["root", "save", "content"], before.Nodes.Select(node => node.TestId));
        Assert.Equal(UiRole.Group, before.Nodes[0].Role);
        Assert.Equal(UiRole.Button, before.Nodes[1].Role);
        Assert.Equal("保存", before.Nodes[1].Name);
        Assert.Equal(before.Nodes[0].Id, before.Nodes[1].ParentId);
        Assert.Equal(before.Nodes[1].Id, before.Nodes[2].ParentId);
        Assert.Equal(UiAutomationActions.Click | UiAutomationActions.Focus, before.Nodes[1].Actions);
        button.AutomationName = "changed";
        UiAutomationSnapshot after = surface.CaptureAutomationSnapshot();
        Assert.Equal("保存", before.Nodes[1].Name);
        Assert.True(after.Sequence > before.Sequence);
        Assert.Equal(before.Nodes[1].Id, after.Nodes[1].Id);
        Assert.Throws<NotSupportedException>(() => ((IList<UiAutomationNode>)before.Nodes).Clear());
        surface.Dispose();
        Assert.Equal(3, before.Nodes.Count);
    }

    /// <summary>Checks reconnecting the same CPU declarations never reuses a connection identity.</summary>
    [Fact]
    public void ReconnectionAndDifferentSurfacesHaveDistinctIds()
    {
        Definitions.Panel root = Panel()[Button(testId: "save")];
        UiAutomationSnapshot before;
        using (UiSurface first = Surface(root))
        {
            before = first.CaptureAutomationSnapshot();
        }

        using UiSurface second = Surface(root);
        UiAutomationSnapshot after = second.CaptureAutomationSnapshot();
        Assert.NotEqual(before.SurfaceId, after.SurfaceId);
        Assert.NotEqual(before.Nodes[1].Id, after.Nodes[1].Id);
        Assert.Same(root, second.Root);
    }

    /// <summary>Checks inherited state is preserved even when hidden geometry has no area.</summary>
    [Fact]
    public void HiddenAndDisabledAncestorsRemainInSnapshot()
    {
        using UiSurface surface = Surface(Panel(isVisible: false, isEnabled: false, hitTestVisible: false)[Button(testId: "save")]);
        UiAutomationNode node = surface.CaptureAutomationSnapshot().Nodes[1];
        Assert.False(node.IsVisible);
        Assert.False(node.IsEnabled);
        Assert.False(node.HitTestVisible);
        Assert.False(node.IsFocusable);
    }

    /// <summary>Checks overflow intersects only actual clips and viewport, never an unclipped parent.</summary>
    /// <param name="clipped">Whether the ancestor clips its children.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SnapshotAndProbeUseTheSameOverflowClip(bool clipped)
    {
        var root = new Fixture.OffsetPanel { ClipToBounds = clipped, Offset = new(20, 0), Width = 30, Height = 30 };
        root.Children.Initialize(Button(testId: "save", width: 30, height: 20));
        using UiSurface surface = Surface(Panel()[root]);
        UiAutomationNode node = surface.CaptureAutomationSnapshot().Nodes[2];
        Assert.Equal(30, node.Bounds.Width);
        Assert.Equal(clipped ? 10 : 30, node.ClippedBounds.Width);
        Assert.Equal(node.Id, surface.ProbePointer(new(25, 5)).ActivationTarget);
        Assert.Equal(clipped ? null : node.Id, surface.ProbePointer(new(40, 5)).ActivationTarget);
        Assert.Null(surface.FocusedElement?.Widget);
    }

    /// <summary>Checks content hits resolve the nearest actual Click component and role spoofing adds no action.</summary>
    [Fact]
    public void ProbeResolvesComponentsWithoutClickingOrFocusing()
    {
        int clicks = 0;
        Definitions.Button button = Button(testId: "save", width: 20, height: 20, onClick: () => clicks++)[Rectangle(testId: "content", width: 20, height: 20)];
        using UiSurface surface = Surface(Panel()[button]);
        UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
        UiPointerProbe probe = surface.ProbePointer(new(5, 5));
        Assert.Equal(snapshot.Nodes[2].Id, probe.HitElement);
        Assert.Equal(snapshot.Nodes[1].Id, probe.ActivationTarget);
        Assert.Equal(0, clicks);
        Assert.False(((ButtonElement)surface.RootElement.Children[0]).IsPressed);
        Assert.Null(surface.FocusedElement?.Widget);
        using UiSurface spoof = Surface(Rectangle(role: UiRole.Button));
        Assert.Equal(UiAutomationActions.None, spoof.CaptureAutomationSnapshot().Nodes[0].Actions);
        Assert.Null(spoof.ProbePointer(new(5, 5)).ActivationTarget);
    }

    /// <summary>Checks a child input consumer prevents both pointer bubbling and an incorrect parent activation probe.</summary>
    [Fact]
    public void DeclaredInputConsumerBlocksParentActivation()
    {
        var child = new Consumer { Width = 20, Height = 20 };
        int clicks = 0;
        using UiSurface surface = Surface(Button(onClick: () => clicks++)[child]);
        Assert.Null(surface.ProbePointer(new(5, 5)).ActivationTarget);
        surface.Dispatch(new UiPointerInput(0, UiPointerPhase.Down, new(5, 5)));
        surface.Dispatch(new UiPointerInput(0, UiPointerPhase.Up, new(5, 5)));
        Assert.Equal(0, clicks);
        Assert.Equal(1, child.Downs);
    }

    /// <summary>Checks malformed semantic selections never publish a partial snapshot.</summary>
    /// <param name="mode">Duplicate, foreign or null selection.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidSemanticChildrenAreRejected(int mode)
    {
        using UiSurface surface = Surface(new BadChildren(mode));
        Assert.Throws<InvalidOperationException>(() => surface.CaptureAutomationSnapshot());
    }

    /// <summary>Checks metadata accepts null and unknown roles without enum validation, while empty test identities are rejected.</summary>
    [Fact]
    public void MetadataDoesNotInvalidateLayoutOrValidateEnums()
    {
        Definitions.Rectangle node = Rectangle(testId: "initial", role: (UiRole)777);
        using UiSurface surface = Surface(node);
        node.TestId = null;
        Assert.Null(surface.CaptureAutomationSnapshot().Nodes[0].TestId);
        Assert.Equal((UiRole)777, surface.CaptureAutomationSnapshot().Nodes[0].Role);
        Assert.Throws<ArgumentException>(() => node.TestId = " ");
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.ProbePointer(new(float.NaN, 0)));
        surface.Dispose();
        Assert.Throws<ObjectDisposedException>(() => surface.CaptureAutomationSnapshot());
    }

    /// <summary>Checks snapshot bounds reject an unrepresentable far edge instead of publishing infinite geometry.</summary>
    [Fact]
    public void OverflowingSemanticBoundsDoNotPublish()
    {
        var root = new Fixture.OffsetPanel { Offset = new(float.MaxValue, 0) };
        root.Children.Initialize(Rectangle(width: float.MaxValue, height: 10));
        using UiSurface surface = Surface(root);
        Assert.Throws<InvalidOperationException>(() => surface.CaptureAutomationSnapshot());
    }

    /// <summary>Checks Button subclasses share propagation decisions between probe and ordinary Down.</summary>
    [Fact]
    public void ButtonOverrideCanDeclinePointerDownWithoutProbeDivergence()
    {
        int outerClicks = 0;
        var inner = new PassiveButton { Width = 20, Height = 20 };
        using UiSurface surface = Surface(Button(testId: "outer", onClick: () => outerClicks++)[inner]);
        UiAutomationId outer = surface.CaptureAutomationSnapshot().Nodes[0].Id;
        Assert.Equal(outer, surface.ProbePointer(new(5, 5)).ActivationTarget);
        surface.Dispatch(new UiPointerInput(0, UiPointerPhase.Down, new(5, 5)));
        surface.Dispatch(new UiPointerInput(0, UiPointerPhase.Up, new(5, 5)));
        Assert.Equal(1, outerClicks);
        Assert.False(((ButtonElement)surface.RootElement.Children[0]).IsPressed);
    }

    /// <summary>Checks keyed instance identity survives replacement and follows keys during reordering.</summary>
    [Fact]
    public void KeyedRebuildPreservesAutomationIdentity()
    {
        using UiSurface surface = Surface(Panel()[Button(key: "a", testId: "a"), Button(key: "b", testId: "b")]);
        UiAutomationSnapshot before = surface.CaptureAutomationSnapshot();
        surface.Rebuild(Panel()[Button(key: "b", testId: "b"), Button(key: "a", testId: "a")]);
        UiAutomationSnapshot after = surface.CaptureAutomationSnapshot();
        Assert.Equal(before.Nodes[1].Id, after.Nodes[2].Id);
        Assert.Equal(before.Nodes[2].Id, after.Nodes[1].Id);
        surface.Rebuild(Panel()[Rectangle(key: "a", testId: "a")]);
        Assert.NotEqual(before.Nodes[1].Id, surface.CaptureAutomationSnapshot().Nodes[1].Id);
    }

    private static UiSurface Surface(Widget root)
    {
        var surface = new UiSurface(root);
        surface.SetViewport(new(100, 80));
        surface.UpdateLayout();
        return surface;
    }

    private sealed class PassiveButton : Definitions.Button
    {
        protected override UiElement CreateElement() => new PassiveButtonElement(this);
    }

    private sealed class PassiveButtonElement : ButtonElement
    {
        internal PassiveButtonElement(PassiveButton widget)
            : base(widget)
        {
        }

        protected override UiInputResult OnProbePointerDown(Vector2 localPosition) => UiInputResult.Unhandled;
    }

    private sealed class Consumer : Widget
    {
        internal ConsumerElement? Runtime { get; set; }

        internal int Downs => Runtime?.Downs ?? 0;

        protected override UiElement CreateElement() => new ConsumerElement(this);
    }

    private sealed class ConsumerElement : UiElement<Consumer>
    {
        internal ConsumerElement(Consumer widget)
            : base(widget)
        {
        }

        internal int Downs { get; private set; }

        protected override void OnMounted() => Widget.Runtime = this;

        protected override UiInputResult OnProbePointerDown(Vector2 localPosition) => UiInputResult.Handled;

        protected override UiInputResult OnInput(in UiInputContext context)
        {
            if (context.Input is UiPointerInput { Phase: UiPointerPhase.Down })
            {
                Downs++;
                return OnProbePointerDown(context.PointerPosition!.Value);
            }

            return UiInputResult.Unhandled;
        }
    }

    private sealed class BadChildren : Definitions.Panel
    {
        internal BadChildren(int mode)
        {
            Mode = mode;
            Children.Initialize(Rectangle());
        }

        internal int Mode { get; }

        protected override UiElement CreateElement() => new BadChildrenElement(this);
    }

    private sealed class BadChildrenElement : PanelElement
    {
        internal BadChildrenElement(BadChildren widget)
            : base(widget)
        {
        }

        protected override IEnumerable<UiElement> GetAutomationChildren() => ((BadChildren)Widget).Mode switch
        {
            0 => [Children[0], Children[0]],
            1 => [new BadChildrenElement(new BadChildren(0))],
            _ => null!,
        };
    }
}
