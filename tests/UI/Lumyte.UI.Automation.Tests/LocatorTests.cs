using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Automation.Tests;

/// <summary>Checks lazy scoped search, strictness and retrying assertions.</summary>
public sealed class LocatorTests
{
    /// <summary>Checks exact ordinal names, hidden filtering, descendant scopes and overlap deduplication.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task QueriesRespectScopeRoleNamesAndHiddenElementsAsync()
    {
        await using var fixture = new Fixture(Panel(testId: "scope")[Stack(testId: "scope")[Button(testId: "save", automationName: "Save", width: 10, height: 10), Button(testId: "save", automationName: "Save", isVisible: false)]]);
        UiAutomationPage page = fixture.Page;
        Assert.Equal(2, await page.GetByTestId("scope").GetByTestId("save").CountAsync());
        Assert.Equal(1, await page.GetByRole(UiRole.Button, "Save").CountAsync());
        Assert.Equal(2, await page.GetByRole(UiRole.Button, "Save", includeHidden: true).CountAsync());
        Assert.Equal(0, await page.GetByRole(UiRole.Button, "save").CountAsync());
        Assert.Equal(1, await page.GetByTestId("scope").GetByTestId("scope").CountAsync());
        await page.Expect(page.GetByTestId("save")).ToHaveCountAsync(2);
        await page.Expect(page.GetByTestId("missing")).ToBeHiddenAsync();
    }

    /// <summary>Checks strict ambiguity fails immediately without waiting or sending input.</summary>
    /// <param name="click">Whether to test an action rather than an assertion.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleMatchesFailImmediatelyAsync(bool click)
    {
        await using var fixture = new Fixture(Panel()[Button(testId: "save"), Button(testId: "save")]);
        UiLocator locator = fixture.Page.GetByTestId("save");
        UiAutomationException error = await Assert.ThrowsAsync<UiAutomationException>(() => click ? locator.ClickAsync() : fixture.Page.Expect(locator).ToBeVisibleAsync());
        Assert.Equal(UiAutomationFailureKind.AmbiguousMatch, error.Kind);
        Assert.NotNull(error.LastSnapshot);
        Assert.Equal(0, fixture.Frames);
        Assert.Empty(fixture.Host.Sent);
    }

    /// <summary>Checks assertions retry state changes on fresh frames rather than caching widgets.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task AssertionsRetryVisibilityEnablementAndAppearanceAsync()
    {
        var button = new Fixture.RuntimeButton { TestId = "save", Width = 10, Height = 10, IsVisible = false, IsEnabled = false };
        await using var fixture = new Fixture(Panel()[button]);
        fixture.OnFrame = () => button.Change(node =>
        {
            node.IsVisible = true;
            node.IsEnabled = true;
            node.TestId = "ready";
        });
        await fixture.Page.Expect(fixture.Page.GetByTestId("ready")).ToBeVisibleAsync();
        await fixture.Page.Expect(fixture.Page.GetByTestId("ready")).ToBeEnabledAsync();
        Assert.Equal(1, fixture.Frames);
        Assert.Equal(0, await fixture.Page.GetByTestId("save").CountAsync());
        Assert.Empty(fixture.Host.Sent);
    }

    /// <summary>Checks visibility assertions allow occlusion, while zero area counts as hidden.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task VisibilityDoesNotRequirePointerActionabilityAsync()
    {
        await using var fixture = new Fixture(Panel()[Button(testId: "covered", width: 10, height: 10), Rectangle(width: 20, height: 20), Button(testId: "zero", width: 0, height: 0)]);
        await fixture.Page.Expect(fixture.Page.GetByTestId("covered")).ToBeVisibleAsync();
        await fixture.Page.Expect(fixture.Page.GetByTestId("zero")).ToBeHiddenAsync();
        Assert.Equal(0, fixture.Frames);
    }

    /// <summary>Checks virtual deadlines retain bounded diagnostics and release the page guard.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task TimeoutUsesHostClockAndBoundsTraceAsync()
    {
        await using var fixture = new Fixture(Panel());
        UiAutomationException error = await Assert.ThrowsAsync<UiAutomationException>(() => fixture.Page.GetByTestId("missing").ClickAsync());
        Assert.Equal(UiAutomationFailureKind.Timeout, error.Kind);
        Assert.Equal(100, fixture.Frames);
        Assert.InRange(error.Trace.Count, 1, 64);
        Assert.Contains(error.Trace, line => line.Contains("missing", StringComparison.Ordinal));
        Assert.NotNull(error.LastSnapshot);
        Assert.Equal(0, await fixture.Page.GetByTestId("missing").CountAsync());
    }

    /// <summary>Checks separate pages cannot exchange locators and unsupported screenshots do not fabricate PNGs.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task PageAndScreenshotBoundariesAreExplicitAsync()
    {
        await using var fixture = new Fixture(Panel());
        var other = new UiAutomationPage(fixture.Host);
        Assert.Throws<ArgumentException>(() => fixture.Page.Expect(other.GetByTestId("save")));
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Page.ScreenshotAsync());
        Assert.Throws<ArgumentException>(() => new UiAutomationPage(fixture.Host, new() { Timeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentException>(() => fixture.Page.GetByRole(UiRole.None));
        Assert.Throws<ArgumentException>(() => fixture.Page.GetByTestId(" "));
    }

    /// <summary>Checks a blocked frame rejects concurrent work on the same page and caller cancellation remains cancellation.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ConcurrentOperationIsRejectedAndCancellationReleasesGuardAsync()
    {
        await using var fixture = new Fixture(Panel());
        using var cancel = new CancellationTokenSource();
        var frame = new TaskCompletionSource();
        fixture.WaitFrame = frame.Task;
        Task waiting = fixture.Page.GetByTestId("missing").ClickAsync(cancellationToken: cancel.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Page.SnapshotAsync());
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Empty(fixture.Host.Sent);
        Assert.NotNull(await fixture.Page.SnapshotAsync());
    }
}
