using Lumyte.Graphics.Drawing2D;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Windowing.Tests.TestTasks;

namespace Lumyte.UI.Windowing.Tests;

/// <summary>Verifies ManagerTests.</summary>
public sealed class ManagerTests
{
    /// <summary>Checks PlacementFlipsAndClampsWithRootClipAndDpi.</summary>
    [Fact]
    public void PlacementFlipsAndClampsWithRootClipAndDpi()
    {
        using var f = new Fixture();
        f.Source.Scale = new(2, 1.5f);
        UiWindowId id = f.Windows.Create(new(f.Registration.Window, new(90, 90), UiWindowRole.Popup, UiWindowInputMode.Interactive, new(f.Registration.Window, new(180, 130, 20, 20), UiWindowPlacementSide.Below)));
        using var surface = new UiSurface(Button());
        Task<UiWindowBinding> bind = f.Windows.BindPresentationAsync(id, surface).AsTask();
        f.Pump();
        UiWindowBinding binding = Completed(bind);
        f.Windows.Show(id);
        f.Pump();
        Assert.True(f.Windows.TryGetSnapshot(id, out UiWindowSnapshot? state));
        Assert.Equal(new Rect2D(110, 40, 90, 90), state!.BoundsInPresentation);
        Assert.Equal(f.Source.Scale, state.Scale);
        Assert.Equal(id, f.Windows.HitTest(f.Registration.Window, new(115, 45))!.Value.Window);
        binding.DisposeAsync();
        f.Pump();
        f.Windows.Close(id);
        f.Pump();
        f.Windows.Release(id);
    }

    /// <summary>Checks a hosted popup restores its requested size when the presentation root grows again.</summary>
    [Fact]
    public void HostedSizeRestoresAfterRootIsShrunkAndExpanded()
    {
        using var f = new Fixture();
        f.Source.Size = new(500, 400);
        f.Pump();
        UiWindowId id = f.Windows.Create(new(f.Registration.Window, new(200, 100), UiWindowRole.Popup, UiWindowInputMode.Interactive, new(f.Registration.Window, new(0, 0, 20, 20), UiWindowPlacementSide.Below)));
        using var surface = new UiSurface(Button());
        Task<UiWindowBinding> bind = f.Windows.BindPresentationAsync(id, surface).AsTask();
        f.Pump();
        UiWindowBinding binding = Completed(bind);
        f.Windows.Show(id);
        f.Pump();
        Assert.True(f.Windows.TryGetSnapshot(id, out UiWindowSnapshot? state));
        Assert.Equal(new System.Numerics.Vector2(200, 100), state!.ClientSize);
        f.Source.Size = new(80, 80);
        f.Pump();
        Assert.True(f.Windows.TryGetSnapshot(id, out state));
        Assert.Equal(new System.Numerics.Vector2(80, 80), state!.ClientSize);
        f.Source.Size = new(500, 400);
        f.Pump();
        Assert.True(f.Windows.TryGetSnapshot(id, out state));
        Assert.Equal(new System.Numerics.Vector2(200, 100), state!.ClientSize);
        binding.DisposeAsync();
        f.Pump();
        f.Windows.Close(id);
        f.Pump();
        f.Windows.Release(id);
    }

    /// <summary>Checks completed inner retirement failures complete the outer binding and allow shutdown.</summary>
    [Fact]
    public void FallbackRetirementFailureDoesNotBlockShutdown()
    {
        var native = new HostedUiWindowManager();
        var hosted = new HostedUiWindowManager();
        var windows = new FallbackUiWindowManager(native, hosted);
        var source = new TestSource();
        using var surface = new UiSurface(Button(width: 20, height: 20));
        UiWindowRegistration registration = windows.Register(source);
        Task<UiWindowBinding> bind = windows.BindPresentationAsync(registration.Window, surface).AsTask();
        void Pump()
        {
            for (int i = 0; i < 15; i++)
            {
                windows.Advance();
                source.Present();
            }
        }

        Pump();
        UiWindowBinding binding = Completed(bind);
        source.FailRetirement = true;
        Task retired = binding.DisposeAsync().AsTask();
        Pump();
        Assert.True(retired.IsFaulted);
        Assert.Throws<InvalidOperationException>(() => retired.GetAwaiter().GetResult());
        Assert.All(source.Presentations, presentation => Assert.True(presentation.Disposed));
        source.FailRetirement = false;
        registration.Dispose();
        windows.BeginStop();
        Pump();
        Assert.True(registration.IsReleased);
        Assert.True(windows.IsStopped);
        windows.Dispose();
        native.BeginStop();
        hosted.BeginStop();
        native.Advance();
        hosted.Advance();
        native.Dispose();
        hosted.Dispose();
    }

    /// <summary>Checks GeometryIsNotPublishedBeforePresentationAcknowledgesIt.</summary>
    [Fact]
    public void GeometryIsNotPublishedBeforePresentationAcknowledgesIt()
    {
        using var f = new Fixture();
        UiPopupHandle popup = f.Open();
        UiWindowSnapshot state = f.Windows.CaptureWindows().Single(s => s.Role == UiWindowRole.Popup);
        f.Source.Acknowledge = false;
        f.Windows.Place(state.Id, new(f.Registration.Window, new(100, 50, 10, 10), UiWindowPlacementSide.Below));
        f.Pump();
        Assert.True(f.Windows.TryGetSnapshot(state.Id, out UiWindowSnapshot? pending));
        Assert.Equal(state.BoundsInPresentation, pending!.BoundsInPresentation);
        f.Source.Acknowledge = true;
        f.Pump();
        f.Windows.TryGetSnapshot(state.Id, out UiWindowSnapshot? presented);
        Assert.NotEqual(state.BoundsInPresentation, presented!.BoundsInPresentation);
    }

    /// <summary>Checks WrongManagerDefaultAndRetiredIdsAreRejected.</summary>
    [Fact]
    public void WrongManagerDefaultAndRetiredIdsAreRejected()
    {
        using var f = new Fixture();
        using var other = new HostedUiWindowManager();
        Assert.Throws<ArgumentException>(() => other.TryGetSnapshot(f.Registration.Window, out _));
        Assert.Throws<ArgumentException>(() => f.Windows.TryGetSnapshot(default, out _));
        Assert.Throws<InvalidOperationException>(() => f.Windows.Close(f.Registration.Window));
        Assert.Null(f.Windows.ResolveInputRoot(new(Guid.NewGuid())));
    }

    /// <summary>Checks HostedRequireNativeFailsWithoutPreparingAnotherSource.</summary>
    [Fact]
    public void HostedRequireNativeFailsWithoutPreparingAnotherSource()
    {
        using var f = new Fixture();
        int before = f.Source.Presentations.Count;
        UiPopupHandle popup = f.Popups.RequestOpen(f.Owner, new(f.Anchor, new(40, 30), () => Button(), policy: UiWindowPolicy.RequireNative));
        f.Pump();
        Assert.Equal(UiPopupState.Failed, popup.State);
        Assert.Equal("native-unavailable", popup.FailureCode);
        Assert.Equal(before, f.Source.Presentations.Count);
    }

    /// <summary>Checks preparation timeout waits for delayed token recovery and GPU retirement.</summary>
    [Fact]
    public void TimeoutDrainsLatePresentationBeforeFailure()
    {
        var clock = new Clock();
        var source = new TestSource();
        using var windows = new HostedUiWindowManager(clock);
        using var rootSurface = new UiSurface(Button());
        UiWindowRegistration root = windows.Register(source);
        Task<UiWindowBinding> rootTask = windows.BindPresentationAsync(root.Window, rootSurface).AsTask();
        windows.Advance();
        source.Present();
        windows.Advance();
        UiWindowBinding rootDrawing = Completed(rootTask);
        using var content = new UiSurface(Button());
        UiWindowId id = windows.Create(new(root.Window, new(40, 30), UiWindowRole.Popup, UiWindowInputMode.Interactive, new(root.Window, new(0, 0, 10, 10), UiWindowPlacementSide.Below)));
        var delayed = new TaskCompletionSource<IUiWindowPresentation>();
        source.PendingPrepare = delayed;
        Task<UiWindowBinding> binding = windows.BindPresentationAsync(id, content).AsTask();
        windows.Advance();
        clock.Advance(TimeSpan.FromSeconds(3));
        windows.Advance();
        Assert.False(binding.IsCompleted);
        Assert.True(windows.TryGetSnapshot(id, out UiWindowSnapshot? closing));
        Assert.Equal(UiWindowState.Closing, closing!.State);
        var retirement = new TaskCompletionSource();
        source.RetireGate = retirement;
        delayed.SetResult(source.Presentations.Last());
        windows.Advance();
        Assert.False(binding.IsCompleted);
        retirement.SetResult();
        source.RetireGate = null;
        source.PendingPrepare = null;
        windows.Advance();
        UiWindowOperationException error = Assert.Throws<UiWindowOperationException>(() => binding.GetAwaiter().GetResult());
        Assert.Equal("opening-timeout", error.Code);
        windows.Release(id);
        rootDrawing.DisposeAsync();
        windows.Advance();
        root.Dispose();
        windows.Advance();
        Assert.True(root.IsReleased);
        windows.BeginStop();
        windows.Advance();
    }

    /// <summary>Checks a root source loan delays unregister without taking source ownership.</summary>
    [Fact]
    public void SourceLoanDelaysRootRelease()
    {
        var source = new TestSource();
        using var windows = new HostedUiWindowManager();
        using var surface = new UiSurface(Button());
        UiWindowRegistration root = windows.Register(source);
        Task<UiWindowBinding> bind = windows.BindPresentationAsync(root.Window, surface).AsTask();
        windows.Advance();
        source.Present();
        windows.Advance();
        UiWindowSourceLease loan = windows.BorrowPresentationSource(root.Window);
        root.Dispose();
        windows.Advance();
        Assert.False(root.IsReleased);
        loan.DisposeAsync();
        windows.Advance();
        Assert.True(root.IsReleased);
        windows.BeginStop();
        windows.Advance();
    }

    /// <summary>Checks FallbackPublishesOnlyItsOwnIdSpaceAndBorrowsManagers.</summary>
    [Fact]
    public void FallbackPublishesOnlyItsOwnIdSpaceAndBorrowsManagers()
    {
        var native = new HostedUiWindowManager();
        var hosted = new HostedUiWindowManager();
        var windows = new FallbackUiWindowManager(native, hosted);
        var source = new TestSource();
        using var surface = new UiSurface(Button(width: 20, height: 20));
        UiWindowRegistration registration = windows.Register(source);
        Task<UiWindowBinding> bind = windows.BindPresentationAsync(registration.Window, surface).AsTask();
        void Pump()
        {
            for (int i = 0; i < 15; i++)
            {
                windows.Advance();
                source.Present();
            }
        }

        Pump();
        UiWindowBinding root = Completed(bind);
        UiWindowId popup = windows.Create(new(registration.Window, new(50, 30), UiWindowRole.Popup, UiWindowInputMode.Interactive, new(registration.Window, new(0, 0, 20, 20), UiWindowPlacementSide.Below), UiWindowPolicy.PreferNative));
        using var content = new UiSurface(Button());
        Task<UiWindowBinding> pending = windows.BindPresentationAsync(popup, content).AsTask();
        Pump();
        UiWindowBinding drawing = Completed(pending);
        windows.Show(popup);
        Pump();
        Assert.True(windows.TryGetSnapshot(popup, out UiWindowSnapshot? s));
        Assert.Equal(windows.Id, s!.Id.ManagerId);
        Assert.Equal(registration.Window, s.PresentationRoot);
        Assert.Equal("native-unavailable", s.FallbackCode);
        Assert.All(source.Presentations, p => Assert.Equal(windows.Id, p.View.Window.ManagerId));
        UiWindowHit hit = windows.HitTest(registration.Window, new(5, 25))!.Value;
        Assert.Equal(source.Presentations.Single(p => p.View.Window == popup).PresentedGeometryRevision, hit.GeometryRevision);
        drawing.DisposeAsync();
        Pump();
        windows.Close(popup);
        Pump();
        windows.Release(popup);
        root.DisposeAsync();
        Pump();
        registration.Dispose();
        Pump();
        Assert.True(registration.IsReleased);
        windows.BeginStop();
        Pump();
        Assert.True(windows.IsStopped);
        windows.Dispose();
        Assert.NotEqual(Guid.Empty, hosted.Id);
        native.BeginStop();
        hosted.BeginStop();
        native.Advance();
        hosted.Advance();
        native.Dispose();
        hosted.Dispose();
    }

    /// <summary>Checks normal Hosted owners receive keys after pointer selection while passive popups preserve that selection.</summary>
    [Fact]
    public void NormalHostedOwnerGetsKeyboardAfterPointerSelection()
    {
        using var f = new Fixture();
        int clicks = 0;
        UiWindowId id = f.Windows.Create(new(f.Registration.Window, new(80, 50), UiWindowRole.Normal, UiWindowInputMode.Interactive, new(f.Registration.Window, new(100, 20, 10, 10), UiWindowPlacementSide.Below)));
        using var surface = new UiSurface(Button(width: 60, height: 30, onClick: () => clicks++));
        Task<UiWindowBinding> drawing = f.Windows.BindPresentationAsync(id, surface).AsTask();
        f.Pump();
        f.Windows.Show(id);
        f.Pump();
        UiPopupOwner owner = f.Popups.RegisterOwner(id, surface);
        f.Send(new UiPointerInput(20, UiPointerPhase.Down, new(105, 35)));
        f.Send(new UiPointerInput(20, UiPointerPhase.Up, new(105, 35)));
        Assert.Equal(id, f.Windows.GetKeyboardTarget(f.Registration.Window));
        f.Send(new UiKeyInput(Lumyte.Input.Abstractions.Key.Enter, true));
        f.Send(new UiKeyInput(Lumyte.Input.Abstractions.Key.Enter, false));
        f.Open(mode: UiPopupInputMode.Passive);
        Assert.Equal(id, f.Windows.GetKeyboardTarget(f.Registration.Window));
        Assert.Equal(2, clicks);
        owner.Dispose();
        f.Pump();
        Completed(drawing).DisposeAsync();
        f.Pump();
        f.Windows.Close(id);
        f.Pump();
        f.Windows.Release(id);
    }

    private sealed class Clock : TimeProvider
    {
        private long _now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _now;

        internal void Advance(TimeSpan elapsed) => _now += elapsed.Ticks;
    }
}
