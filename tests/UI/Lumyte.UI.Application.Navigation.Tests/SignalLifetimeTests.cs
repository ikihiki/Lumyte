using Lumyte.Signals;
using Lumyte.UI.Navigation;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Navigation.Tests;

/// <summary>Checks optional business effects follow page activation cleanup while shared values survive.</summary>
public sealed class SignalLifetimeTests
{
    /// <summary>Checks Own admission, nested observer ownership and reverse cleanup on real page retirement.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task BusinessObserversAreReleasedBeforeThePageScopeAsync()
    {
        var value = Signal.Create(0);
        var released = new List<int>();
        int observed = -1;
        UiPageContext? active = null;
        await using var fixture = new PageFixture((builder, _) => builder.MapPage("/owned", context =>
        {
            active = context;
            context.Own(new Resource(() => released.Add(1)));
            context.CreateSignalScope().Run(() => Signal.Effect(() => observed = value.Value));
            context.Own(new Resource(() => released.Add(2)));
            return Rectangle(width: 20);
        }));
        await fixture.ReadyAsync();
        Assert.Equal(UiNavigationStatus.Committed, (await fixture.NavigateAsync("/owned")).Status);
        value.Value = 4;
        Assert.Equal(4, observed);
        await fixture.WaitAsync(() => fixture.Navigator.CaptureSnapshot().Phase == UiNavigationPhase.Idle);
        await fixture.App.Dispatcher.InvokeAsync(() => Assert.Throws<InvalidOperationException>(() => active!.Own(new Resource(() => { }))));
        Assert.Equal(UiNavigationStatus.Committed, (await fixture.NavigateAsync("/next")).Status);
        await fixture.WaitAsync(() => released.Count == 2);
        Assert.Equal([2, 1], released);
        value.Value = 9;
        Assert.Equal(4, observed);
        Assert.Equal(9, value.Value);
    }

    /// <summary>Checks page scopes revoke asynchronous work before its late completion can reach UI.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task PageRetirementCancelsOwnedAsynchronousSignalsAsync()
    {
        var pending = new TaskCompletionSource<int>();
        AsyncSignal<int>? signal = null;
        CancellationToken token = default;
        await using var fixture = new PageFixture((builder, _) => builder.MapPage("/async", context =>
        {
            context.CreateSignalScope().Run(() => signal = Signal.FromAsync(current =>
            {
                token = current;
                return pending.Task;
            }));
            Computed<string?> caption = Signal.Computed<string?>(() => signal!.Value.IsLoading ? "loading" : "ready");
            return Button(automationName: caption);
        }));
        await fixture.ReadyAsync();
        Assert.Equal(UiNavigationStatus.Committed, (await fixture.NavigateAsync("/async")).Status);
        await fixture.WaitAsync(() => fixture.Navigator.CaptureSnapshot().Phase == UiNavigationPhase.Idle);
        Assert.Equal(UiNavigationStatus.Committed, (await fixture.NavigateAsync("/next")).Status);
        await fixture.WaitAsync(() => fixture.Navigator.CaptureSnapshot().Phase == UiNavigationPhase.Idle);
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(AsyncSignalStatus.Canceled, (await signal!.Completion).Status);
        pending.SetResult(1);
        Assert.Equal(AsyncSignalStatus.Pending, signal.Value.Status);
        Assert.Null(fixture.App.Failure);
    }

    private sealed class Resource(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
