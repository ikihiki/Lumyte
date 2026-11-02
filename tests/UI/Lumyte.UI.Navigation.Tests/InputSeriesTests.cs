using Lumyte.UI.Application.Navigation;
using Lumyte.UI.Windowing;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Navigation.Tests;

/// <summary>Checks input ownership survives page replacement without sending old series to a new page.</summary>
public sealed class InputSeriesTests
{
    /// <summary>Checks a held pointer is canceled on the old page and its Up is consumed after publication.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task PointerReleaseNeverClicksTheReplacementPageAsync()
    {
        var provider = new PageProvider();
        int oldClicks = 0;
        int newClicks = 0;
        UiPageKey<UiNoArgs> home = provider.Register("/", (_, _) => new(Button(onClick: () => oldClicks++)));
        UiPageKey<UiNoArgs> other = provider.Register("/other", (_, _) => new(Button(onClick: () => newClicks++)));
        await using var harness = new NavigationHarness(provider, nav => nav.RequestNavigate(home, default));
        await harness.ReadyAsync();
        await harness.WaitAsync(() => harness.Context.Windows.TryGetSnapshot(harness.Context.Window, out UiWindowSnapshot? snapshot) && snapshot!.IsVisible);
        await harness.App.Dispatcher.InvokeAsync(() =>
        {
            harness.Context.Windows.TryGetSnapshot(harness.Context.Window, out UiWindowSnapshot? snapshot);
            Assert.True(harness.Context.Popups.Dispatch(new(snapshot!.InputOrigin!.Value, new UiPointerInput(11, UiPointerPhase.Down, new(20, 20)))));
        });
        UiNavigationRequest request = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestNavigate(other, default));
        Assert.Equal(UiNavigationStatus.Committed, (await request.Completion).Status);
        await harness.IdleAsync();
        await harness.WaitAsync(() => harness.Context.Windows.TryGetSnapshot(harness.Context.Window, out UiWindowSnapshot? snapshot) && snapshot!.IsVisible);
        await harness.App.Dispatcher.InvokeAsync(() =>
        {
            harness.Context.Windows.TryGetSnapshot(harness.Context.Window, out UiWindowSnapshot? snapshot);
            Assert.True(harness.Context.Popups.Dispatch(new(snapshot!.InputOrigin!.Value, new UiPointerInput(11, UiPointerPhase.Up, new(20, 20)))));
            Assert.Equal(0, oldClicks);
            Assert.Equal(0, newClicks);
            harness.Context.Popups.Dispatch(new(snapshot.InputOrigin.Value, new UiPointerInput(12, UiPointerPhase.Down, new(20, 20))));
            harness.Context.Popups.Dispatch(new(snapshot.InputOrigin.Value, new UiPointerInput(12, UiPointerPhase.Up, new(20, 20))));
            Assert.Equal(1, newClicks);
        });
    }

    /// <summary>Checks a candidate lacking editing capabilities fails before publication and is cleaned normally.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task InvalidEditorDoesNotReplaceTheCurrentPageAsync()
    {
        var provider = new PageProvider();
        UiPageKey<UiNoArgs> home = provider.Register("/", (_, _) => new(Panel()));
        UiPageKey<UiNoArgs> invalid = provider.Register("/invalid", (_, _) => new(Lumyte.UI.TextEditing.Compose.TextBox(text: "missing layout provider")));
        await using var harness = new NavigationHarness(provider, nav => nav.RequestNavigate(home, default));
        await harness.ReadyAsync();
        UiNavigationWindowSnapshot old = await harness.App.Dispatcher.InvokeAsync(harness.Context.View.CaptureNavigationSnapshot);
        UiNavigationRequest request = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestNavigate(invalid, default));
        UiNavigationResult result = await request.Completion;
        Assert.Equal(UiNavigationStatus.Failed, result.Status);
        await harness.IdleAsync();
        Assert.Equal(old, await harness.App.Dispatcher.InvokeAsync(harness.Context.View.CaptureNavigationSnapshot));
        Assert.Equal(1, provider.Activations[1].Retires);
        Assert.Equal(1, provider.Activations[1].Disposals);
    }
}
