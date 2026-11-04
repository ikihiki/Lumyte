using System.Numerics;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Windowing.Tests.TestTasks;

namespace Lumyte.UI.Windowing.Tests;

/// <summary>Checks normal-window mutations commit with the displayed geometry.</summary>
public sealed class HostedGeometryTests
{
    /// <summary>Checks resize and stacking do not change hit testing before presentation.</summary>
    [Fact]
    public void ResizeAndStackingCommitOnlyAfterPresentation()
    {
        using var fixture = new Fixture();
        UiWindowId root = fixture.Registration.Window;
        UiWindowId first = fixture.Windows.Create(new(root, new(80, 60), UiWindowRole.Normal, UiWindowInputMode.Interactive, new(root, new(0, 0, 0, 0), UiWindowPlacementSide.Below)));
        UiWindowId second = fixture.Windows.Create(new(root, new(80, 60), UiWindowRole.Normal, UiWindowInputMode.Interactive, new(root, new(0, 0, 0, 0), UiWindowPlacementSide.Below)));
        using var firstSurface = new UiSurface(Button());
        using var secondSurface = new UiSurface(Button());
        Task<UiWindowBinding> firstBind = fixture.Windows.BindPresentationAsync(first, firstSurface).AsTask();
        Task<UiWindowBinding> secondBind = fixture.Windows.BindPresentationAsync(second, secondSurface).AsTask();
        fixture.Pump();
        UiWindowBinding firstBinding = Completed(firstBind);
        UiWindowBinding secondBinding = Completed(secondBind);
        fixture.Windows.Show(first);
        fixture.Windows.Show(second);
        fixture.Pump();
        Assert.Equal(second, fixture.Windows.HitTest(root, new(10, 10))!.Value.Window);
        fixture.Source.Acknowledge = false;
        UiWindowRequestId superseded = fixture.Windows.Resize(first, new(70, 50));
        UiWindowRequestId resize = fixture.Windows.Resize(first, new(60, 40));
        UiWindowRequestId stacking = fixture.Windows.BringToFront(first);
        fixture.Pump();
        Assert.Equal(second, fixture.Windows.HitTest(root, new(10, 10))!.Value.Window);
        Assert.True(fixture.Windows.TryGetSnapshot(first, out UiWindowSnapshot? old));
        Assert.Equal(80, old!.BoundsInPresentation.Width);
        Assert.Equal(60, old.BoundsInPresentation.Height);
        Assert.True(fixture.Windows.TryGetRequest(superseded, out UiWindowRequestResult? replaced));
        Assert.Equal("superseded", replaced!.Code);
        Assert.True(fixture.Windows.TryGetRequest(resize, out UiWindowRequestResult? pending));
        Assert.Equal(UiWindowRequestStatus.Pending, pending!.Status);
        fixture.Source.Acknowledge = true;
        fixture.Pump();
        Assert.Equal(first, fixture.Windows.HitTest(root, new(10, 10))!.Value.Window);
        Assert.True(fixture.Windows.TryGetSnapshot(first, out UiWindowSnapshot? current));
        Assert.Equal(new Vector2(60, 40), current!.ClientSize);
        Assert.True(fixture.Windows.TryGetRequest(stacking, out UiWindowRequestResult? raised));
        Assert.Equal(UiWindowRequestStatus.Succeeded, raised!.Status);
        firstBinding.DisposeAsync();
        secondBinding.DisposeAsync();
        fixture.Pump();
        fixture.Windows.Close(first);
        fixture.Windows.Close(second);
        fixture.Pump();
        fixture.Windows.Release(first);
        fixture.Windows.Release(second);
    }
}
