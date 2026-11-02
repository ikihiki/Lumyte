using Lumyte.UI.Application.Navigation;
using Lumyte.UI.Automation;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Navigation.Tests;

/// <summary>Exercises real host publication, fresh activation identities and bounded history.</summary>
public sealed class HistoryTests
{
    /// <summary>Checks Back/Forward recreate pages while preserving entries, window identity and copied arguments.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task HistoryRecreatesPagesAndOldHostsNeverFollowTheNewSurfaceAsync()
    {
        var provider = new PageProvider();
        UiPageKey<UiNoArgs> home = provider.Register("/", (_, _) => new(Panel(width: 100, height: 100)));
        UiPageKey<int> item = provider.Register<int>("/item", static value => value, (activation, _) => new(Button(width: 100, height: 40, testId: "item", onClick: () => activation.Context.Navigation.RequestBack())));
        await using var harness = new NavigationHarness(provider, navigation => navigation.RequestNavigate(home, default));
        await harness.ReadyAsync();
        UiNavigationWindowSnapshot first = await harness.App.Dispatcher.InvokeAsync(harness.Context.View.CaptureNavigationSnapshot);
        IUiAutomationHost oldHost = await harness.App.Dispatcher.InvokeAsync(() => harness.Context.Automation);
        UiNavigationRequest next = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestNavigate(new UiPageKey<int>("/item"), 7));
        Assert.Equal(UiNavigationStatus.Committed, (await next.Completion.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        await harness.IdleAsync();
        UiNavigationWindowSnapshot second = await harness.App.Dispatcher.InvokeAsync(harness.Context.View.CaptureNavigationSnapshot);
        Assert.Equal(first.Window, second.Window);
        Assert.Equal(first.WindowContextId, second.WindowContextId);
        Assert.NotEqual(first.Current!.SurfaceId, second.Current!.SurfaceId);
        Assert.Equal(7, (await harness.SnapshotAsync()).Entries[1].Destination.GetArguments<int>());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => oldHost.RunOnUiThreadAsync(_ => true).AsTask());
        await new UiAutomationPage(harness.Context.Automation).GetByTestId("item").ClickAsync();
        await harness.WaitAsync(() => harness.Content.Navigation.CaptureSnapshot().CurrentIndex == 0);
        await harness.IdleAsync();
        UiNavigationWindowSnapshot back = await harness.App.Dispatcher.InvokeAsync(harness.Context.View.CaptureNavigationSnapshot);
        Assert.Equal(first.Current.EntryId, back.Current!.EntryId);
        Assert.NotEqual(first.Current.ActivationId, back.Current.ActivationId);
        Assert.NotEqual(first.Current.SurfaceId, back.Current.SurfaceId);
        UiNavigationRequest forward = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestForward());
        Assert.Equal(UiNavigationStatus.Committed, (await forward.Completion).Status);
        await harness.IdleAsync();
        Assert.All(provider.Activations.Take(provider.Activations.Count - 1), activation => Assert.Equal(1, activation.Disposals));
    }

    /// <summary>Checks trimming, forward truncation, Replace and empty history without factory calls.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task BoundedHistoryCommitsOnlySuccessfulOperationsAsync()
    {
        var provider = new PageProvider();
        UiPageKey<int> key = provider.Register<int>("/page", static args => args, (_, _) => new(Panel()));
        await using var harness = new NavigationHarness(provider, navigation => navigation.RequestNavigate(key, 0), new() { MaxHistoryEntries = 3 });
        await harness.ReadyAsync();
        UiNavigationRequest empty = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestBack());
        Assert.Equal(UiNavigationStatus.NoHistory, (await empty.Completion).Status);
        Assert.Single(provider.Activations);
        for (int i = 1; i <= 4; i++)
        {
            int argument = i;
            UiNavigationRequest request = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestNavigate(key, argument));
            Assert.Equal(UiNavigationStatus.Committed, (await request.Completion).Status);
            await harness.IdleAsync();
        }

        UiNavigationSnapshot snapshot = await harness.SnapshotAsync();
        Assert.Equal([2, 3, 4], snapshot.Entries.Select(entry => entry.Destination.GetArguments<int>()).ToArray());
        UiNavigationRequest back = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestBack());
        await back.Completion;
        await harness.IdleAsync();
        UiNavigationRequest replacement = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestNavigate(key, 30, UiNavigationMode.Replace));
        await replacement.Completion;
        await harness.IdleAsync();
        Assert.True((await harness.SnapshotAsync()).CanGoForward);
        UiNavigationRequest push = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestNavigate(key, 31));
        await push.Completion;
        await harness.IdleAsync();
        snapshot = await harness.SnapshotAsync();
        Assert.Equal([2, 30, 31], snapshot.Entries.Select(entry => entry.Destination.GetArguments<int>()).ToArray());
        Assert.False(snapshot.CanGoForward);
    }
}
