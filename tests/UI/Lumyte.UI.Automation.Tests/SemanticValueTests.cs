using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Automation.Tests;

/// <summary>Checks bounded, detached read-only component semantics.</summary>
public sealed class SemanticValueTests
{
    /// <summary>Checks component hooks copy state without inferring capabilities from roles.</summary>
    /// <param name="role">The semantic role, including spoofed interactive metadata.</param>
    [Theory]
    [InlineData(UiRole.Text)]
    [InlineData(UiRole.Image)]
    [InlineData(UiRole.Icon)]
    [InlineData(UiRole.ProgressBar)]
    [InlineData(UiRole.BusyIndicator)]
    [InlineData(UiRole.Button)]
    public void SnapshotCopiesHookStateWithoutAddingActions(UiRole role)
    {
        var widget = new SemanticWidget { Role = role, Focusable = true, Value = 0.25 };
        using var surface = new UiSurface(widget);
        surface.SetViewport(new(100, 80));
        UiAutomationNode before = Assert.Single(surface.CaptureAutomationSnapshot().Nodes);
        Assert.Equal(0.25, before.Value);
        Assert.False(before.IsIndeterminate);
        Assert.Equal(role, before.Role);
        Assert.Equal(UiAutomationActions.None, before.Actions);
        Assert.Null(surface.ProbePointer(new(1, 1)).ActivationTarget);
        widget.Value = null;
        widget.Indeterminate = true;
        UiAutomationNode after = Assert.Single(surface.CaptureAutomationSnapshot().Nodes);
        Assert.Null(after.Value);
        Assert.True(after.IsIndeterminate);
        Assert.Equal(0.25, before.Value);
        Assert.False(before.IsIndeterminate);
        surface.Dispose();
        Assert.Equal(0.25, before.Value);
    }

    /// <summary>Checks invalid hook results never escape as partially published snapshots.</summary>
    /// <param name="value">An invalid normalized value.</param>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void InvalidValuesAreRejected(double value)
    {
        using var surface = new UiSurface(new SemanticWidget { Value = value });
        Assert.Throws<InvalidOperationException>(() => surface.CaptureAutomationSnapshot());
    }

    /// <summary>Checks both inclusive bounds and the default absence of semantic state.</summary>
    [Fact]
    public void BoundsAndDefaultsArePreserved()
    {
        var widget = new SemanticWidget { Value = 0 };
        using var surface = new UiSurface(widget);
        Assert.Equal(0, Assert.Single(surface.CaptureAutomationSnapshot().Nodes).Value);
        widget.Value = 1;
        Assert.Equal(1, Assert.Single(surface.CaptureAutomationSnapshot().Nodes).Value);
        surface.Rebuild(Rectangle());
        UiAutomationNode plain = Assert.Single(surface.CaptureAutomationSnapshot().Nodes);
        Assert.Null(plain.Value);
        Assert.False(plain.IsIndeterminate);
    }

    /// <summary>Checks redaction excludes semantic hooks as well as names and descendants.</summary>
    [Fact]
    public void RedactedBoundariesNeverReadOrExposeSemanticState()
    {
        var widget = new SemanticWidget { Redacted = true, Value = double.NaN, Indeterminate = true, TestId = "private", AutomationName = "secret" };
        using var surface = new UiSurface(widget);
        UiAutomationNode node = Assert.Single(surface.CaptureAutomationSnapshot().Nodes);
        Assert.Null(node.TestId);
        Assert.Null(node.Name);
        Assert.Null(node.Value);
        Assert.False(node.IsIndeterminate);
        Assert.Equal(0, widget.HookReads);
    }

    private sealed class SemanticWidget : Widget
    {
        internal double? Value { get; set; }

        internal bool Indeterminate { get; set; }

        internal bool Redacted { get; set; }

        internal int HookReads { get; set; }

        protected override UiElement CreateElement() => new SemanticElement(this);
    }

    private sealed class SemanticElement(SemanticWidget widget) : UiElement<SemanticWidget>(widget)
    {
        protected override UiAutomationExposure AutomationExposure => Widget.Redacted ? UiAutomationExposure.RedactedSubtree : UiAutomationExposure.Default;

        protected override UiAutomationActions AutomationActions => UiAutomationActions.None;

        protected override double? AutomationValue
        {
            get
            {
                Widget.HookReads++;
                return Widget.Value;
            }
        }

        protected override bool IsAutomationIndeterminate
        {
            get
            {
                Widget.HookReads++;
                return Widget.Indeterminate;
            }
        }
    }
}
