using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Automation.Tests;

/// <summary>Checks closed selection operations do not infer capabilities or inject redundant input.</summary>
public sealed class SelectionTests
{
    /// <summary>Checks spoofed selection roles never grant write capabilities.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task RolesDoNotGrantSelectionActionsAsync()
    {
        await using var fixture = new Fixture(Rectangle(testId: "fake", width: 20, height: 20, role: UiRole.CheckBox));
        UiLocator locator = fixture.Page.GetByTestId("fake");
        Assert.Equal(UiAutomationFailureKind.UnsupportedAction, (await Assert.ThrowsAsync<UiAutomationException>(() => locator.CheckAsync())).Kind);
        Assert.Equal(UiAutomationFailureKind.UnsupportedAction, (await Assert.ThrowsAsync<UiAutomationException>(() => locator.UncheckAsync())).Kind);
        Assert.Equal(UiAutomationFailureKind.UnsupportedAction, (await Assert.ThrowsAsync<UiAutomationException>(() => locator.SelectOptionAsync("one"))).Kind);
        Assert.Empty(fixture.Host.Sent);
    }

    /// <summary>Checks matching state is a no-op even for a disabled target.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task MatchingDesiredStateDoesNotAcquireInputAsync()
    {
        var widget = new SelectionWidget { TestId = "choice", IsEnabled = false };
        await using var fixture = new Fixture(widget);
        UiLocator locator = fixture.Page.GetByTestId("choice");
        await locator.CheckAsync();
        await locator.SelectOptionAsync("one");
        Assert.Empty(fixture.Host.Sent);
        Assert.Equal(0, fixture.Frames);
        await Assert.ThrowsAsync<ArgumentException>(() => locator.SelectOptionAsync(" "));
    }

    /// <summary>Checks three-state desired operations commit directly once and never cycle through mixed.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ThreeStateDesiredOperationsCommitOnceAsync()
    {
        var changes = new List<bool?>();
        var check = new Lumyte.UI.Controls.Compose.Definitions.CheckBox
        {
            TestId = "check",
            IsChecked = true,
            ThreeState = true,
            Width = 24,
            Height = 24,
            OnChanged = changes.Add,
        };
        await using var fixture = new Fixture(check);
        UiLocator locator = fixture.Page.GetByTestId("check");
        await locator.UncheckAsync();
        Assert.Equal(new bool?[] { false }, changes);
        Assert.False(Assert.Single((await fixture.Page.SnapshotAsync()).Nodes).IsChecked);
        int sent = fixture.Host.Sent.Count;
        await locator.UncheckAsync();
        Assert.Equal(sent, fixture.Host.Sent.Count);
        await locator.CheckAsync();
        Assert.Equal(new bool?[] { false, true }, changes);
        Assert.Equal(2, fixture.Host.Sent.OfType<UiCheckInput>().Count());
        Assert.DoesNotContain(fixture.Host.Sent.OfType<UiPointerInput>(), input => input.Phase is UiPointerPhase.Down or UiPointerPhase.Up);
    }

    /// <summary>Checks selecting a group key uses one ordinary radio activation and then becomes a no-op.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task SelectOptionUsesNormalRadioPointerAsync()
    {
        var changes = new List<string?>();
        Lumyte.UI.Controls.Compose.Definitions.RadioGroup group = Lumyte.UI.Controls.Compose.RadioGroup(testId: "group", onChanged: changes.Add)[
            Lumyte.UI.Controls.Compose.RadioButton(key: "one", width: 24, height: 24),
            Lumyte.UI.Controls.Compose.RadioButton(key: "two", width: 24, height: 24)];
        await using var fixture = new Fixture(group);
        UiLocator locator = fixture.Page.GetByTestId("group");
        await locator.SelectOptionAsync("two");
        Assert.Equal(new[] { "two" }, changes);
        Assert.Equal(new[] { UiPointerPhase.Move, UiPointerPhase.Down, UiPointerPhase.Up }, fixture.Host.Sent.OfType<UiPointerInput>().Select(input => input.Phase));
        int sent = fixture.Host.Sent.Count;
        await locator.SelectOptionAsync("two");
        Assert.Equal(sent, fixture.Host.Sent.Count);
        UiAutomationNode owner = (await fixture.Page.SnapshotAsync()).Nodes.Single(node => node.TestId == "group");
        Assert.Equal("two", owner.SelectedKey);
    }

    /// <summary>Dispatch rejection after a successful probe is terminal, without callback or retry.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task DesiredDispatchRejectionIsFailureWithoutRetryAsync()
    {
        int changes = 0;
        await using var fixture = new Fixture(Lumyte.UI.Controls.Compose.CheckBox(testId: "check", onChanged: _ => changes++));
        var host = new RejectingHost(fixture);
        var page = new UiAutomationPage(host);
        UiAutomationException error = await Assert.ThrowsAsync<UiAutomationException>(() => page.GetByTestId("check").CheckAsync());
        Assert.Equal(UiAutomationFailureKind.ActionFailed, error.Kind);
        Assert.Equal(1, host.Attempts);
        Assert.Equal(0, changes);
        Assert.False(fixture.Surface.CaptureAutomationSnapshot().Nodes[0].IsChecked);
    }

    /// <summary>Delivered callbacks may replace declarations without another desired input.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task DesiredCallbackChangingValueDoesNotRetryAsync()
    {
        int changes = 0;
        Fixture? current = null;
        await using var fixture = new Fixture(Lumyte.UI.Controls.Compose.CheckBox(testId: "check", onChanged: _ =>
        {
            changes++;
            current!.Surface.Rebuild(Lumyte.UI.Controls.Compose.CheckBox(testId: "check", isChecked: false));
            current.Surface.UpdateLayout();
        }));
        current = fixture;
        await fixture.Page.GetByTestId("check").CheckAsync();
        Assert.Equal(1, changes);
        Assert.Single(fixture.Host.Sent.OfType<UiCheckInput>());
        Assert.False(fixture.Surface.CaptureAutomationSnapshot().Nodes[0].IsChecked);
    }

    private sealed class RejectingHost(Fixture fixture) : IUiAutomationHost, IUiAutomationRoutingHost
    {
        private bool _scopeAccepts = true;

        public int Attempts { get; private set; }

        public TimeProvider TimeProvider => fixture.Host.TimeProvider;

        public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default) => fixture.Host.RunOnUiThreadAsync(action, cancellationToken);

        public ValueTask NextFrameAsync(CancellationToken cancellationToken = default) => fixture.Host.NextFrameAsync(cancellationToken);

        public async ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default)
        {
            IUiAutomationInputLease lease = await fixture.Host.AcquireInputAsync(cancellationToken);
            _scopeAccepts = false;
            return lease;
        }

        public UiPointerProbe ProbePointer(UiSurface surface, System.Numerics.Vector2 position) => surface.ProbePointer(position);

        public bool CanReceiveKeys(UiSurface surface) => true;

        public bool Dispatch(UiSurface surface, UiInput input)
        {
            if (input is UiCheckInput)
            {
                Attempts++;
                return _scopeAccepts && surface.Dispatch(input);
            }

            return surface.Dispatch(input);
        }
    }

    private sealed class SelectionWidget : Widget
    {
        protected override UiElement CreateElement() => new SelectionElement(this);
    }

    private sealed class SelectionElement(SelectionWidget widget) : UiElement<SelectionWidget>(widget)
    {
        protected override UiAutomationActions AutomationActions => UiAutomationActions.Check | UiAutomationActions.SelectOption;

        protected override bool? AutomationChecked => true;

        protected override string? AutomationSelectedKey => "one";
    }
}
