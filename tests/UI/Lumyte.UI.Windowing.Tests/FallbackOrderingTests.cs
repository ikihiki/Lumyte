using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Windowing.Tests.TestTasks;

namespace Lumyte.UI.Windowing.Tests;

/// <summary>Checks public fallback identities preserve presented stacking and terminal results.</summary>
public sealed class FallbackOrderingTests
{
    /// <summary>Checks raising an older hosted window changes the fallback hit target after presentation.</summary>
    [Fact]
    public void RaisedWindowWinsAndCompletedRequestsStayStable()
    {
        using var native = new HostedUiWindowManager();
        using var hosted = new HostedUiWindowManager();
        using var windows = new FallbackUiWindowManager(native, hosted);
        var source = new TestSource();
        using var surface = new UiSurface(Panel());
        UiWindowRegistration registration = windows.Register(source);
        UiWindowRegistration[] otherRoots = Enumerable.Range(0, 4).Select(_ => windows.Register(new TestSource())).ToArray();
        void Pump()
        {
            for (int index = 0; index < 12; index++)
            {
                windows.Advance();
                source.Present();
            }
        }

        Task<UiWindowBinding> rootTask = windows.BindPresentationAsync(registration.Window, surface).AsTask();
        Pump();
        UiWindowBinding root = Completed(rootTask);
        var place = new UiWindowPlacement(registration.Window, new(0, 0, 0, 0), UiWindowPlacementSide.Below);
        UiWindowId first = windows.Create(new(registration.Window, new(80, 60), UiWindowRole.Normal, UiWindowInputMode.Interactive, place, UiWindowPolicy.HostedOnly));
        UiWindowId second = windows.Create(new(registration.Window, new(80, 60), UiWindowRole.Normal, UiWindowInputMode.Interactive, place, UiWindowPolicy.HostedOnly));
        using var firstSurface = new UiSurface(Button());
        using var secondSurface = new UiSurface(Button());
        Task<UiWindowBinding> firstTask = windows.BindPresentationAsync(first, firstSurface).AsTask();
        Task<UiWindowBinding> secondTask = windows.BindPresentationAsync(second, secondSurface).AsTask();
        Pump();
        UiWindowBinding firstBinding = Completed(firstTask);
        UiWindowBinding secondBinding = Completed(secondTask);
        windows.Show(first);
        windows.Show(second);
        Pump();
        Assert.Equal(second, windows.HitTest(registration.Window, new(10, 10))!.Value.Window);
        source.Acknowledge = false;
        UiWindowRequestId request = windows.BringToFront(first);
        Assert.False(windows.ReleaseRequest(request));
        Pump();
        Assert.Equal(second, windows.HitTest(registration.Window, new(10, 10))!.Value.Window);
        source.Acknowledge = true;
        Pump();
        Assert.Equal(first, windows.HitTest(registration.Window, new(10, 10))!.Value.Window);
        UiWindowPresentationView front = source.Presentations.Where(presentation => !presentation.Disposed && presentation.View.IsVisible)
            .OrderBy(presentation => UiWindowStacking.Key(presentation.View.Role, presentation.View.Window, presentation.View.StackingOrder)).Last().View;
        Assert.Equal(first, front.Window);
        Assert.True(windows.TryGetRequest(request, out UiWindowRequestResult? result));
        Assert.Equal(UiWindowRequestStatus.Succeeded, result!.Status);
        Pump();
        Assert.True(windows.TryGetRequest(request, out UiWindowRequestResult? unchanged));
        Assert.Same(result, unchanged);
        Assert.True(windows.ReleaseRequest(request));
        Assert.False(windows.TryGetRequest(request, out _));
        Assert.False(windows.ReleaseRequest(request));
        Pump();
        firstBinding.DisposeAsync();
        secondBinding.DisposeAsync();
        Pump();
        windows.Close(first);
        windows.Close(second);
        Pump();
        windows.Release(first);
        windows.Release(second);
        root.DisposeAsync();
        Pump();
        registration.Dispose();
        foreach (UiWindowRegistration other in otherRoots)
        {
            other.Dispose();
        }

        Pump();
        windows.BeginStop();
        Pump();
    }
}
