using Lumyte.Input.Abstractions;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Benchmark;
using Lumyte.UI.Automation;
using Lumyte.UI.Automation.Benchmark;
using Xunit;

namespace Lumyte.UI.Benchmark.Tests;

/// <summary>Semantic oracles for CPU automation and actual headless-owner workloads.</summary>
public sealed class AutomationApplicationBenchmarkTests
{
    /// <summary>Checks scale, detached IDs, geometry and scoped filtering.</summary>
    /// <param name="elements">Actual ordinary-control count.</param>
    /// <returns>Completed query checks.</returns>
    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    public async Task SnapshotAndLocatorHaveActualScaleAsync(int elements)
    {
        using var fixture = new AutomationFixture(elements);
        UiAutomationSnapshot first = fixture.Surface.CaptureAutomationSnapshot();
        UiAutomationSnapshot second = fixture.Surface.CaptureAutomationSnapshot();
        Assert.Equal(elements + 1, first.Nodes.Count);
        Assert.Equal(first.SurfaceId, second.SurfaceId);
        Assert.Equal(first.Nodes.Select(node => node.Id), second.Nodes.Select(node => node.Id));
        Assert.True(second.Sequence > first.Sequence);
        Assert.All(first.Nodes.Skip(1), node =>
        {
            Assert.Equal(40, node.ContentBounds.Width);
            Assert.Equal(10, node.ContentBounds.Height);
            Assert.True((node.Actions & UiAutomationActions.Click) != 0);
        });
        Assert.Equal(elements / 2, await fixture.Page.GetByTestId("controls").GetByRole(UiRole.Button, "Primary").CountAsync());
        Assert.Equal(0, await fixture.Page.GetByRole(UiRole.Button, "primary").CountAsync());
    }

    /// <summary>Checks normal pointer and keyboard routing, callbacks and frame completion.</summary>
    /// <returns>Completed action checks.</returns>
    [Fact]
    public async Task ActionsCompleteTheirRealInputAndFramesAsync()
    {
        using var fixture = new AutomationFixture(100);
        await fixture.Page.GetByTestId("control-0").ClickAsync();
        Assert.Equal(1, fixture.Clicks);
        Assert.Equal(3, fixture.ProcessedInputs);
        Assert.Equal(1, fixture.FrameOrdinal);
        await fixture.Page.GetByTestId("control-1").FocusAsync();
        Assert.Equal(5, fixture.ProcessedInputs);
        await fixture.Page.GetByTestId("control-1").PressAsync(Key.Enter);
        Assert.Equal(7, fixture.ProcessedInputs);
        Assert.Equal(2, fixture.Clicks);
        UiAutomationNode focused = fixture.Surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "control-1");
        Assert.True(focused.IsFocused);
        Assert.False(focused.HasActivePress);
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Page.ScreenshotAsync());
    }

    /// <summary>Checks real owner initialization, repeated frame barriers, and ordered stop/disposal.</summary>
    /// <param name="diagnostics">Whether bounded local diagnostics participate.</param>
    /// <returns>Joined owner completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeadlessLifecycleJoinsActualOwnerAsync(bool diagnostics)
    {
        var fixture = new ApplicationFixture(100, diagnostics);
        Assert.Equal(UiApplicationState.Created, fixture.App.State);
        await fixture.StartAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(UiApplicationState.Running, fixture.App.State);
        Assert.Equal(1, fixture.FrameOrdinal);
        int owner = await fixture.App.Dispatcher.InvokeAsync(() => Environment.CurrentManagedThreadId);
        Assert.NotEqual(Environment.CurrentManagedThreadId, owner);
        Assert.False(fixture.Host is IUiAutomationScreenshotHost);
        Lumyte.Mathematics.Colors.SRgba<float> before = await fixture.App.Dispatcher.InvokeAsync(() => fixture.ModelColor);
        await fixture.AdvanceAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotEqual(before, await fixture.App.Dispatcher.InvokeAsync(() => fixture.ModelColor));
        Assert.Equal(2, fixture.FrameOrdinal);
        UiAutomationSnapshot snapshot = await new UiAutomationPage(fixture.Host).SnapshotAsync();
        Assert.Equal(201, snapshot.Nodes.Count);
        await fixture.DisposeAsync();
        Assert.Equal(UiApplicationState.Stopped, fixture.App.State);
        Assert.True(fixture.App.Completion.IsCompletedSuccessfully);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.App.RunAsync());
    }

    /// <summary>Checks passive cold Build never starts an owner.</summary>
    /// <returns>Completed passive disposal.</returns>
    [Fact]
    public async Task ColdBuildCanStopWithoutRunningAsync()
    {
        var fixture = new ApplicationFixture(100, false);
        Assert.Equal(0, fixture.FrameOrdinal);
        await fixture.DisposeAsync();
        Assert.Equal(UiApplicationState.Stopped, fixture.App.State);
    }

    /// <summary>Checks generated metadata, redaction and bounded target exchange.</summary>
    /// <returns>Completed owner-local exchange.</returns>
    [Fact]
    public async Task LocalDiagnosticsAreBoundedAndRedactedAsync()
    {
        var benchmark = new DiagnosticsBenchmarks();
        benchmark.Setup();
        try
        {
            Assert.NotEmpty(benchmark.DiagnosticOperationSetCatalog());
            Assert.Equal("forbidden", benchmark.DeniedControl().Code);
            Assert.Equal("success", benchmark.UiAutomationDiagnosticsTargets().Status);
            UiAutomationSnapshot snapshot = benchmark.UiSurfaceRedactedSnapshot();
            Assert.Equal(3, snapshot.Nodes.Count);
            Assert.DoesNotContain(snapshot.Nodes, node => node.TestId is "private" or "secret");
            await benchmark.UiAutomationDiagnosticTargetExchangeAsync();
            Assert.Equal("success", benchmark.UiAutomationDiagnosticsTargets().Status);
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Checks partial Enter cancellation releases the lease without activation.</summary>
    /// <returns>The completed lease cleanup.</returns>
    [Fact]
    public async Task PartialInputLeaseIsCancelledBeforeReleaseAsync()
    {
        using var fixture = new AutomationFixture(100);
        await fixture.Page.GetByTestId("control-0").FocusAsync();
        IUiAutomationInputLease lease = await fixture.AcquireInputAsync();
        lease.Dispatch(fixture.Surface, new UiKeyInput(Key.Enter, true));
        Assert.True(fixture.Surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "control-0").HasActivePress);
        await lease.DisposeAsync();
        Assert.Equal(0, fixture.Clicks);
        Assert.False(fixture.Surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "control-0").HasActivePress);
        await using IUiAutomationInputLease next = await fixture.AcquireInputAsync();
    }

    /// <summary>Checks actual hosted menu routing, callback completion and adapter-bound closure.</summary>
    /// <returns>Joined popup and session cleanup.</returns>
    [Fact]
    public async Task PopupSessionRoutesAndClosesAsync()
    {
        var benchmark = new PopupAutomationBenchmarks();
        benchmark.Setup();
        try
        {
            Assert.Single(await benchmark.UiPopupAutomationSessionList());
            Assert.False(await benchmark.CancelPartialInputAsync());
            Assert.Equal(0, benchmark.Clicks);
            int before = benchmark.FrameOrdinal;
            await benchmark.UiPopupAutomationSessionClick();
            Assert.Equal(1, benchmark.Clicks);
            Assert.True(benchmark.FrameOrdinal > before);
            await benchmark.UiPopupAutomationSessionClick();
            Assert.Equal(2, benchmark.Clicks);
            Assert.Equal(0, await benchmark.CloseAsync());
        }
        finally
        {
            await benchmark.CleanupAsync();
        }
    }

    /// <summary>Checks optional navigation diagnostics are exercised through real public registration.</summary>
    [Fact]
    public void NavigationDiagnosticSchemaAndEmptyRegistryAreReal()
    {
        var benchmark = new NavigationDiagnosticsBenchmarks();
        benchmark.Setup();
        try
        {
            Assert.Single(benchmark.UiNavigationDiagnosticsCatalog());
            Lumyte.Diagnostics.DiagnosticOperationResult result = benchmark.UiNavigationDiagnosticsEmptyRegistry();
            Assert.True(result.Status == "success", $"{result.Code}: {result.Message}; operation={benchmark.UiNavigationDiagnosticsCatalog()[0].Id}; fields={string.Join(",", benchmark.UiNavigationDiagnosticsCatalog()[0].Arguments.Select(field => field.Id))}");
            Assert.Empty(Lumyte.UI.Application.Navigation.Diagnostics.UiNavigationStateSchema.Parse(result.Values!["json"].String!));
        }
        finally
        {
            benchmark.Cleanup();
        }
    }
}
