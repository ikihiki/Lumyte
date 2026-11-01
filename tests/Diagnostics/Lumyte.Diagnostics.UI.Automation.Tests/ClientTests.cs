using Lumyte.Diagnostics.UI.Automation.Client;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Xunit;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

/// <summary>Checks typed client behavior through the actual wire envelope and a separate owner-thread game loop.</summary>
public sealed class ClientTests
{
    /// <summary>Checks discovery, strict catalog matching, heartbeat, normal input, observations, hash verification and capacity release.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task TypedClientCompletesGameSideOperationsAsync()
    {
        using var handler = new LoopbackHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        await using UiRemoteControl control = await client.AcquireControlAsync(target.Id);
        await control.Page.GetByTestId("save").ClickAsync();
        await control.Page.Expect(control.Page.GetByTestId("saved-indicator")).ToBeVisibleAsync();
        await control.Page.GetByTestId("save").PressAsync("Enter");
        Assert.Equal(1, await client.Page(target.Id).GetByRole("button", "保存").CountAsync());
        byte[] image = await client.Page(target.Id).ScreenshotAsync();
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, image);
        Assert.Equal(2, handler.Clicks);
        await Task.Delay(1100);
        await control.Page.GetByTestId("save").ClickAsync();
        Assert.Equal(3, handler.Clicks);
    }

    /// <summary>Checks lost admission retries identical envelope bytes and sequence rather than issuing a replacement click.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task LostAdmissionRetriesTheSameLogicalActionAsync()
    {
        using var handler = new LoopbackHandler { DropStarts = 1 };
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        await using UiRemoteControl control = await client.AcquireControlAsync(target.Id);
        await control.Page.GetByTestId("save").ClickAsync();
        Assert.Equal(1, handler.Clicks);
        string[] requests = handler.ActionBodies.ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Equal(requests[0], requests[1]);
    }

    /// <summary>Checks unknown admission poisons input reuse and never chooses a new sequence or control automatically.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task LostAllAdmissionRepliesRemainOutcomeUnknownAsync()
    {
        using var handler = new LoopbackHandler { DropStarts = 2 };
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        await using UiRemoteControl control = await client.AcquireControlAsync(target.Id);
        UiRemoteAutomationException failure = await Assert.ThrowsAsync<UiRemoteAutomationException>(() => control.Page.GetByTestId("save").ClickAsync());
        Assert.Equal("outcome-unknown", failure.Code);
        Assert.True(failure.InputMayHaveBeenSent);
        await Assert.ThrowsAsync<UiRemoteAutomationException>(() => control.Page.GetByTestId("save").ClickAsync());
        Assert.Equal(2, handler.ActionBodies.Count);
    }

    /// <summary>Checks an accepted action with an expired admission response cannot be mistaken for a later action.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ExpiredAdmissionPreservesUnknownOutcomeAndBlocksFurtherInputAsync()
    {
        using var handler = new LoopbackHandler { ExpireStarts = true };
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        await using UiRemoteControl control = await client.AcquireControlAsync(target.Id);
        UiRemoteAutomationException failure = await Assert.ThrowsAsync<UiRemoteAutomationException>(() => control.Page.GetByTestId("save").ClickAsync());
        Assert.Equal("expired", failure.Code);
        Assert.True(failure.InputMayHaveBeenSent);
        Assert.True(SpinWait.SpinUntil(() => handler.Clicks == 1, TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<UiRemoteAutomationException>(() => control.Page.GetByTestId("save").ClickAsync());
        Assert.Single(handler.ActionBodies);
        Assert.Equal(1, handler.Clicks);
    }

    /// <summary>Checks corrupted artifact bytes/hash never become successful output and observation pages reject input locally.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task BadHashAndObservationInputAreRejectedAsync()
    {
        using var handler = new LoopbackHandler { CorruptHash = true };
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        UiRemotePage page = client.Page(target.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.GetByTestId("save").ClickAsync());
        UiRemoteAutomationException error = await Assert.ThrowsAsync<UiRemoteAutomationException>(() => page.ScreenshotAsync());
        Assert.Equal("artifact-hash-mismatch", error.Code);
        Assert.Empty(handler.ActionBodies);
    }

    /// <summary>Checks a mismatched operation permission is rejected by catalog validation before any operation submission.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task MismatchedCatalogIsNotAcceptedAsync()
    {
        using var handler = new LoopbackHandler { CorruptCatalog = true };
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteAutomationException error = await Assert.ThrowsAsync<UiRemoteAutomationException>(() => client.ListTargetsAsync());
        Assert.Equal("unsupported-catalog", error.Code);
        Assert.Empty(handler.ActionBodies);
    }
}
