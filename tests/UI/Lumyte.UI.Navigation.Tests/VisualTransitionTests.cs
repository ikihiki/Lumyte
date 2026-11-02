using Lumyte.Core.Time;
using Lumyte.UI.Application;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Navigation.Tests;

/// <summary>Checks optional visuals preserve logical navigation, scope cleanup and nonfaulting request outcomes.</summary>
public sealed class VisualTransitionTests
{
    /// <summary>Checks headless capability fallback still commits and retires the old page normally.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task HeadlessFallbackKeepsCommitAndVisualResultsSeparateAsync()
    {
        var provider = new PageProvider();
        UiPageKey<UiNoArgs> home = provider.Register("/", (_, _) => new(Rectangle(width: 40, height: 40, sharedElementKey: new UiSharedElementKey("photo"))));
        UiPageKey<UiNoArgs> next = provider.Register("/next", (_, _) => new(Rectangle(width: 80, height: 40, sharedElementKey: new UiSharedElementKey("photo"))));
        await using var harness = new NavigationHarness(provider, navigation => navigation.RequestNavigate(home, default), new() { Transitions = new() { ConnectSharedElements = true } });
        await harness.ReadyAsync();
        UiNavigationRequest request = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestNavigate(next, default));
        Assert.Equal(UiNavigationStatus.Committed, (await request.Completion.WaitAsync(TimeSpan.FromSeconds(10))).Status);
        UiVisualTransitionResult visual = await request.VisualCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(UiVisualTransitionStatus.Skipped, visual.Status);
        Assert.Equal("capture-unavailable", visual.Code);
        await harness.IdleAsync();
        Assert.Equal(1, provider.Activations[0].Disposals);
        request.SkipAnimation();
        Assert.Same(visual, await request.VisualCompletion);
        UiNavigationRequest invalid = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestForward());
        Assert.NotEqual(UiNavigationStatus.Committed, (await invalid.Completion).Status);
        Assert.Equal(new(UiVisualTransitionStatus.NotStarted, "not-committed"), await invalid.VisualCompletion);
    }

    /// <summary>Checks disabled motion avoids capture and completes only the visual result as skipped.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task DisabledMotionDoesNotCancelNavigationAsync()
    {
        var provider = new PageProvider();
        UiPageKey<UiNoArgs> home = provider.Register("/", (_, _) => new(Panel()));
        UiNavigationRequest? initial = null;
        await using var harness = new NavigationHarness(provider, navigation => initial = navigation.RequestNavigate(home, default), new() { Transitions = new() { Enter = FadeIn(Duration.FromSeconds(1)) } }, configure: builder => builder.Configure(new UiApplicationOptions { Motion = new() { Policy = UiMotionPolicy.Disabled }, FrameInterval = TimeSpan.FromMilliseconds(1) }));
        await harness.ReadyAsync();
        Assert.Equal(UiNavigationStatus.Committed, (await initial!.Completion).Status);
        Assert.Equal(new(UiVisualTransitionStatus.Skipped, "motion-disabled"), await initial.VisualCompletion);
    }

    /// <summary>Checks a skip requested while business preparation is pending does not cancel that preparation.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task SkipBeforePreparationCompletesLeavesBusinessCommitIntactAsync()
    {
        var provider = new PageProvider();
        UiPageKey<UiNoArgs> home = provider.Register("/", (_, _) => new(Panel()));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        UiPageKey<UiNoArgs> next = provider.Register("/next", async (activation, _) =>
        {
            ready.TrySetResult();
            await release.Task;
            return await activation.Context.Dispatcher.InvokeAsync(() => Panel());
        });
        await using var harness = new NavigationHarness(provider, navigation => navigation.RequestNavigate(home, default), new() { Transitions = new() { ConnectSharedElements = true } });
        await harness.ReadyAsync();
        UiNavigationRequest request = await harness.App.Dispatcher.InvokeAsync(() => harness.Content.Navigation.RequestNavigate(next, default));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        request.SkipAnimation();
        release.TrySetResult();
        Assert.Equal(UiNavigationStatus.Committed, (await request.Completion.WaitAsync(TimeSpan.FromSeconds(10))).Status);
        Assert.Equal(new(UiVisualTransitionStatus.Skipped, "skipped-by-request"), await request.VisualCompletion);
        await harness.IdleAsync();
    }
}
