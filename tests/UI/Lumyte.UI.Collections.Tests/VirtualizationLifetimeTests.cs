using Lumyte.Input.Abstractions;
using Lumyte.UI.Controls;
using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Collections.Tests;

/// <summary>Keys and retained owners have independent operation and realization lifetimes.</summary>
public sealed class VirtualizationLifetimeTests
{
    /// <summary>Queued key operations cannot act on a replaced model generation or its old index.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task QueuedRevealRejectsReorderedGenerationAsync()
    {
        Compose.Definitions.ListBox<int> Create(int[] items) => ListBox<int>(items: items, keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => SizedBox(height: 20), height: 60, virtualization: new UiItemVirtualization(20, 0));
        using var surface = new UiSurface(Create(Enumerable.Range(0, 100).ToArray()));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        ListBoxElement<int> list = Assert.IsType<ListBoxElement<int>>(surface.RootElement);
        var dispatcher = new DeferredDispatcher();
        Task pending = list.BringIntoViewAsync(dispatcher, "99").AsTask();
        Assert.False(pending.IsCompleted);
        surface.Rebuild(Create(new[] { 99 }.Concat(Enumerable.Range(0, 99)).ToArray()));
        surface.UpdateLayout();
        ScrollController scroll = Assert.IsType<ScrollViewElement>(list.Children[0]).Controller;
        Assert.Equal(20, scroll.Offset);
        dispatcher.Drain();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Equal(20, scroll.Offset);
    }

    /// <summary>Retained hidden content keeps the owner but no unfocused offscreen row widgets.</summary>
    /// <param name="tabs">Whether to hide through tab selection rather than Expander.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenRetainedOwnerDropsUnfocusedRealizations(bool tabs)
    {
        Compose.Definitions.ListBox<int> declaration = ListBox<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => SizedBox(height: 20), testId: "list", height: 60, virtualization: new UiItemVirtualization(20, 0));
        using var surface = new UiSurface(tabs
            ? Controls.Compose.TabView(selectedKey: "a")[Controls.Compose.TabItem(key: "a")[Controls.Compose.TabItem.Content()[declaration]], Controls.Compose.TabItem(key: "b")]
            : Controls.Compose.Expander(isExpanded: true)[Controls.Compose.Expander.Content()[declaration]]);
        surface.SetViewport(new(320, 160));
        UiAutomationNode metadata = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "list");
        ListBoxElement<int> list = Assert.IsType<ListBoxElement<int>>(surface.FindElement(metadata.Id));
        ScrollController controller = Assert.IsType<ScrollViewElement>(list.Children[0]).Controller;
        controller.ScrollTo(1000);
        surface.UpdateLayout();
        UiElement old = list.Children[0].Children[0].Children[0];
        if (tabs)
        {
            surface.TryFocus(surface.RootElement.Children[1]);
            surface.Dispatch(new UiKeyInput(Key.Enter, true));
            surface.Dispatch(new UiKeyInput(Key.Enter, false));
        }
        else
        {
            surface.Dispatch(new UiExpandInput(surface.RootElement.AutomationId, false, new(2)));
        }

        surface.UpdateLayout();
        Assert.True(list.IsMounted);
        Assert.Equal(0, controller.Offset);
        Assert.Empty(list.Children[0].Children[0].Children);
        Assert.False(old.IsMounted);
    }

    /// <summary>A key queued before the initial viewport realizes only the final target range.</summary>
    [Fact]
    public void KeyBeforeInitialViewportDoesNotConstructPrecedingRows()
    {
        int templates = 0;
        using var surface = new UiSurface(ListBox<int>(
            items: Enumerable.Range(0, 100).ToArray(),
            keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ =>
            {
                templates++;
                return SizedBox(height: 20);
            },
            height: 60,
            virtualization: new UiItemVirtualization(20, 0)));
        ListBoxElement<int> list = Assert.IsType<ListBoxElement<int>>(surface.RootElement);
        Assert.True(list.BringIntoView("99"));
        Assert.Equal(0, templates);
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        Assert.Equal(3, templates);
        Assert.Equal("99", list.Children[0].Children[0].Children.Last().Widget.Key);
    }

    /// <summary>Key geometry is calculated after resize discovery, and later explicit scroll intent wins.</summary>
    [Fact]
    public void ResizeThenKeyUsesPreparedViewportAndCanBeSuperseded()
    {
        using var surface = new UiSurface(ListBox<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => SizedBox(height: 20), height: (float?)null, virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 200));
        surface.UpdateLayout();
        ListBoxElement<int> list = Assert.IsType<ListBoxElement<int>>(surface.RootElement);
        ScrollController scroll = Assert.IsType<ScrollViewElement>(list.Children[0]).Controller;
        scroll.ScrollTo(500);
        surface.UpdateLayout();
        surface.SetViewport(new(100, 60));
        Assert.True(list.BringIntoView("30"));
        surface.UpdateLayout();
        Assert.Equal(560, scroll.Offset);
        Assert.Contains(list.Children[0].Children[0].Children, item => item.Widget.Key == "30");
        Assert.True(list.BringIntoView("999"));
        scroll.ScrollTo(0);
        surface.UpdateLayout();
        Assert.Equal(0, scroll.Offset);
        Assert.DoesNotContain(list.Children[0].Children[0].Children, item => item.Widget.Key == "999");
    }

    /// <summary>Model reset gives an offscreen focused key a fresh bounded pin without changing the visible anchor.</summary>
    [Fact]
    public void ResetPreservesFocusedPinAndVisibleAnchor()
    {
        Compose.Definitions.ListBox<int> Create() => ListBox<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => SizedBox(height: 20), selectedKey: "0", height: 60, virtualization: new UiItemVirtualization(20, 0));
        using var surface = new UiSurface(Create());
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        UiElement old = surface.RootElement.Children[0].Children[0].Children[0];
        surface.TryFocus(old);
        surface.UpdateLayout();
        ScrollController scroll = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[0]).Controller;
        scroll.ScrollTo(1000);
        surface.UpdateLayout();
        Assert.Same(old, surface.FocusedElement);
        surface.Rebuild(Create());
        surface.UpdateLayout();
        Assert.Equal(1000, scroll.Offset);
        Assert.Equal("0", surface.FocusedElement!.Widget.Key);
        Assert.NotEqual(old.AutomationId, surface.FocusedElement.AutomationId);
        Assert.False(old.IsMounted);
        Assert.Equal(4, surface.RootElement.Children[0].Children[0].Children.Count);
        Assert.Equal("0", Assert.IsType<ListBoxElement<int>>(surface.RootElement).SelectedKey);
    }

    /// <summary>A position intent accepted after enqueue supersedes the older asynchronous key reveal.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task QueuedRevealCannotOverrideNewerScrollAsync()
    {
        using var surface = new UiSurface(ListBox<int>(items: Enumerable.Range(0, 100).ToArray(), keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => SizedBox(height: 20), height: 60, virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        ListBoxElement<int> list = Assert.IsType<ListBoxElement<int>>(surface.RootElement);
        ScrollController scroll = Assert.IsType<ScrollViewElement>(list.Children[0]).Controller;
        var dispatcher = new DeferredDispatcher();
        Task pending = list.BringIntoViewAsync(dispatcher, "99").AsTask();
        scroll.ScrollTo(0);
        dispatcher.Drain();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        surface.UpdateLayout();
        Assert.Equal(0, scroll.Offset);
        Assert.DoesNotContain(list.Children[0].Children[0].Children, item => item.Widget.Key == "99");
    }

    /// <summary>A newer asynchronous reveal wins rather than being superseded by the older dispatch.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task NewestQueuedRevealWinsAsync()
    {
        using var surface = new UiSurface(ListBox<int>(items: Enumerable.Range(0, 100).ToArray(), keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => SizedBox(height: 20), height: 60, virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        ListBoxElement<int> list = Assert.IsType<ListBoxElement<int>>(surface.RootElement);
        var dispatcher = new DeferredDispatcher();
        Task older = list.BringIntoViewAsync(dispatcher, "99").AsTask();
        Task newer = list.BringIntoViewAsync(dispatcher, "50").AsTask();
        dispatcher.Drain();
        await Assert.ThrowsAsync<InvalidOperationException>(() => older);
        await newer;
        surface.UpdateLayout();
        Assert.Equal(960, Assert.IsType<ScrollViewElement>(list.Children[0]).Controller.Offset);
        Assert.Contains(list.Children[0].Children[0].Children, item => item.Widget.Key == "50");
    }

    private sealed class DeferredDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _actions = new();

        public bool CheckAccess() => true;

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _actions.Enqueue(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    action();
                    completion.SetResult();
                }
                catch (Exception error)
                {
                    completion.SetException(error);
                }
            });
            return new(completion.Task);
        }

        public ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        internal void Drain()
        {
            while (_actions.TryDequeue(out Action? action))
            {
                action();
            }
        }
    }
}
