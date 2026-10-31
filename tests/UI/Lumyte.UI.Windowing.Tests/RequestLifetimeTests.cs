using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Windowing.Tests.TestTasks;

namespace Lumyte.UI.Windowing.Tests;

/// <summary>Checks optional result ownership independently from operation and window lifetime.</summary>
public sealed class RequestLifetimeTests
{
    /// <summary>Checks only terminal results can be forgotten and cleanup does not recreate forgotten results.</summary>
    /// <param name="fallback">Whether identities pass through fallback.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleasingResultsPreservesPendingOperationsAndWindowState(bool fallback)
    {
        using var native = new HostedUiWindowManager();
        using var hosted = new HostedUiWindowManager();
        IUiWindowManager windows = fallback ? new FallbackUiWindowManager(native, hosted) : hosted;
        IUiWindowRequestLifetime results = Assert.IsAssignableFrom<IUiWindowRequestLifetime>(windows);
        var source = new TestSource();
        using var root = new UiSurface(Panel());
        using var popup = new UiSurface(Button());
        UiWindowRegistration registration = windows.Register(source);
        void Pump()
        {
            for (int index = 0; index < 12; index++)
            {
                windows.Advance();
                source.Present();
            }
        }

        Task<UiWindowBinding> rootTask = windows.BindPresentationAsync(registration.Window, root).AsTask();
        Pump();
        UiWindowBinding rootBinding = Completed(rootTask);
        UiWindowId child = windows.Create(new(registration.Window, new(80, 60), UiWindowRole.Popup, UiWindowInputMode.Passive, new(registration.Window, new(0, 0, 0, 0), UiWindowPlacementSide.Below), UiWindowPolicy.HostedOnly));
        Task<UiWindowBinding> childTask = windows.BindPresentationAsync(child, popup).AsTask();
        Pump();
        UiWindowBinding childBinding = Completed(childTask);
        UiWindowRequestId show = windows.Show(child);
        Assert.False(results.ReleaseRequest(show));
        Pump();
        Assert.True(results.ReleaseRequest(show));
        Assert.False(results.ReleaseRequest(show));
        Assert.False(windows.TryGetRequest(show, out _));
        Pump();
        Assert.True(windows.TryGetSnapshot(child, out UiWindowSnapshot? visible));
        Assert.True(visible!.IsVisible);
        childBinding.DisposeAsync();
        Pump();
        UiWindowRequestId close = windows.Close(child);
        Pump();
        Assert.True(results.ReleaseRequest(close));
        Pump();
        Assert.False(windows.TryGetRequest(close, out _));
        UiWindowRequestId repeatedClose = windows.Close(child);
        Assert.NotEqual(close, repeatedClose);
        Pump();
        Assert.True(windows.TryGetRequest(repeatedClose, out UiWindowRequestResult? repeated));
        Assert.Equal(UiWindowRequestStatus.Succeeded, repeated!.Status);
        windows.Release(child);
        rootBinding.DisposeAsync();
        Pump();
        registration.Dispose();
        Pump();
        windows.BeginStop();
        Pump();
        if (fallback)
        {
            windows.Dispose();
        }
    }
}
