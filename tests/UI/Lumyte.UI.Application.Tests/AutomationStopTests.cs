using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using Xunit;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks a click can finish lease cleanup while global stop closes ordinary dispatcher admission.</summary>
public sealed class AutomationStopTests
{
    /// <summary>Checks owner-forced cleanup and a concurrent external lease return share one completion.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task ClickThatStopsTheApplicationCanReturnItsLeaseAsync()
    {
        for (int i = 0; i < 16; i++)
        {
            UiApplicationWindowContext? context = null;
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
            UiWindowDeclaration window = Window(
                "main",
                "Stop",
                root: value =>
                {
                    context = value;
                    return Button(testId: "quit", onClick: value.RequestStop);
                },
                onOpened: _ => opened.TrySetResult());
            builder.UseWindows(_ => [window]);
            await using UiApplication app = builder.Build();
            Task running = app.RunAsync();
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Run(() => new UiAutomationPage(context!.Automation).GetByTestId("quit").ClickAsync()).WaitAsync(TimeSpan.FromSeconds(5));
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(UiApplicationState.Stopped, app.State);
        }
    }
}
