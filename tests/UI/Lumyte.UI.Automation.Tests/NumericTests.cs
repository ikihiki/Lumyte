using System.Numerics;
using Lumyte.UI.Controls;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;

namespace Lumyte.UI.Automation.Tests;

/// <summary>Numeric commands share ordinary routing, capability checks and exclusive leases.</summary>
public sealed class NumericTests
{
    /// <summary>Closed commands update the real accepted owner and skip already matching values.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ScalarAndRangeCommandsAreAtomicAndIdempotentAsync()
    {
        var changes = new List<SliderRange>();
        await using var fixture = new Fixture(Stack()[
            Slider(min: -100, max: 100, step: 1, testId: "scalar"),
            RangeSlider(min: -100, max: 100, step: 1, testId: "range", onChanged: changes.Add)]);
        await fixture.Page.GetByTestId("scalar").SetValueAsync(42);
        await fixture.Page.GetByTestId("range").SetRangeAsync(-25, 75);
        UiAutomationSnapshot snapshot = await fixture.Page.SnapshotAsync();
        Assert.Equal(42, snapshot.Nodes.Single(node => node.TestId == "scalar").Numeric!.Value);
        Assert.Equal(new SliderRange(-25, 75), Assert.Single(changes));
        int sent = fixture.Host.Sent.Count;
        await fixture.Page.GetByTestId("range").SetRangeAsync(-25, 75);
        Assert.Equal(sent, fixture.Host.Sent.Count);
        Assert.Equal(2, fixture.Host.Sent.OfType<UiSetNumericInput>().Count());
        Assert.Empty(fixture.Host.Sent.OfType<UiPointerInput>());
    }

    /// <summary>Setting the role alone grants no numeric input capability.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task RolesDoNotGrantNumericCapabilitiesAsync()
    {
        await using var fixture = new Fixture(Rectangle(testId: "fake", role: UiRole.Slider));
        UiAutomationException error = await Assert.ThrowsAsync<UiAutomationException>(() => fixture.Page.GetByTestId("fake").SetValueAsync(1));
        Assert.Equal(UiAutomationFailureKind.UnsupportedAction, error.Kind);
        Assert.Empty(fixture.Host.Sent);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Page.GetByTestId("fake").SetValueAsync(double.NaN));
    }

    /// <summary>Scope rejection after acquiring the lease is terminal, not a successful or retried write.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task RejectedDispatchNeverRetriesOrMutatesAsync()
    {
        await using var fixture = new Fixture(Slider(testId: "scalar"));
        var host = new RejectingHost(fixture);
        var page = new UiAutomationPage(host);
        UiAutomationException error = await Assert.ThrowsAsync<UiAutomationException>(() => page.GetByTestId("scalar").SetValueAsync(1));
        Assert.Equal(UiAutomationFailureKind.ActionFailed, error.Kind);
        Assert.Equal(1, host.Attempts);
        Assert.Equal(0, ((SliderElement)fixture.Surface.RootElement).Value);
    }

    /// <summary>Callback publication is not overwritten by an automatic resend.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task CallbackRebuildIsNeverRetriedAsync()
    {
        Fixture? current = null;
        await using var fixture = new Fixture(Slider(testId: "scalar", onChanged: _ =>
        {
            current!.Surface.Rebuild(Slider(testId: "scalar", value: 0.2));
            current.Surface.UpdateTree();
        }));
        current = fixture;
        await fixture.Page.GetByTestId("scalar").SetValueAsync(0.8);
        Assert.Equal(0.2, ((SliderElement)fixture.Surface.RootElement).Value);
        Assert.Single(fixture.Host.Sent.OfType<UiSetNumericInput>());
    }

    private sealed class RejectingHost(Fixture fixture) : IUiAutomationHost, IUiAutomationRoutingHost
    {
        public TimeProvider TimeProvider => fixture.Host.TimeProvider;

        public int Attempts { get; private set; }

        public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default) => fixture.Host.RunOnUiThreadAsync(action, cancellationToken);

        public ValueTask NextFrameAsync(CancellationToken cancellationToken = default) => fixture.Host.NextFrameAsync(cancellationToken);

        public ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default) => fixture.Host.AcquireInputAsync(cancellationToken);

        public UiPointerProbe ProbePointer(UiSurface surface, Vector2 position) => surface.ProbePointer(position);

        public bool CanReceiveKeys(UiSurface surface) => true;

        public bool Dispatch(UiSurface surface, UiInput input)
        {
            if (input is UiSetNumericInput)
            {
                Attempts++;
                return false;
            }

            return surface.Dispatch(input);
        }
    }
}
