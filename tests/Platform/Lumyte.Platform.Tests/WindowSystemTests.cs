using System.Numerics;
using Lumyte.Platform.Abstractions;
using Lumyte.Windowing;
using Lumyte.Windowing.Abstractions;
using Xunit;

namespace Lumyte.Platform.Tests;

/// <summary>Verifies optional management, presentation geometry and native ownership.</summary>
public sealed class WindowSystemTests
{
    /// <summary>Checks optional attachment without pumping or taking native ownership.</summary>
    [Fact]
    public void BorrowedWindowCanBeAttachedAndDetachedWithoutClosing()
    {
        using var platform = new TestPlatform();
        using IWindow native = platform.CreateWindow(new WindowDescriptor());
        using var system = new WindowSystem(platform);
        WindowNodeId root = system.Attach(native, WindowRole.Normal, WindowOwnership.Borrowed);
        Assert.Same(native, system.GetNativeWindow(root));
        WindowNodeRequestId request = system.Detach(root);
        var completed = new List<WindowNodeRequestResult>();
        system.RequestCompleted += completed.Add;
        system.Update();
        Assert.Empty(system.Windows);
        Assert.False(((TestWindow)native).Disposed);
        Assert.Equal(WindowLifetime.Open, native.State.Lifetime);
        Assert.Equal(request, Assert.Single(completed).Request);
        Assert.Equal(0, platform.Pumps);
    }

    /// <summary>Checks duplicate registration across systems and platform scopes.</summary>
    [Fact]
    public void RegistrationRejectsDuplicateAndForeignWindows()
    {
        using var platform = new TestPlatform();
        using var other = new TestPlatform();
        using var first = new WindowSystem(platform);
        using var second = new WindowSystem(platform);
        IWindow native = platform.CreateWindow(new WindowDescriptor());
        first.Attach(native, WindowRole.Normal, WindowOwnership.Borrowed);
        Assert.Throws<ArgumentException>(() => second.Attach(native, WindowRole.Normal, WindowOwnership.Owned));
        Assert.Throws<ArgumentException>(() => first.Attach(other.CreateWindow(new WindowDescriptor()), WindowRole.Normal, WindowOwnership.Borrowed));
        Assert.Throws<ArgumentException>(() => first.GetState(new WindowNodeId(Guid.NewGuid(), 1)));
    }

    /// <summary>Checks clipping, layer order and focus preservation for temporary displays.</summary>
    [Fact]
    public void HostedDisplaysClipAndPreserveKeyboardFocus()
    {
        using var platform = new TestPlatform();
        using var system = new WindowSystem(platform);
        WindowNodeId root = system.CreateNativeWindow(new WindowDescriptor { ClientSize = new Vector2(100, 100) }, WindowRole.Normal);
        system.Show(root);
        system.Update();
        platform.PumpEvents();
        system.Activate(root);
        system.Update();
        platform.PumpEvents();
        system.Update();
        WindowNodeId child = system.CreateHostedWindow(new HostedWindowDescriptor { Parent = root, Bounds = new WindowRect(80, 80, 50, 50) });
        WindowNodeId popup = system.CreateHostedWindow(new HostedWindowDescriptor
        {
            Parent = child,
            Bounds = new WindowRect(5, 5, 50, 50),
            Role = WindowRole.ContextMenu,
            Activation = WindowActivation.PreserveFocus,
        });
        system.Show(child);
        system.Show(popup);
        system.Update();
        Assert.Equal(new[] { child, popup }, system.GetHostedOrder(root));
        Assert.Equal(popup, system.HitTest(root, new Vector2(90, 90)));
        Assert.Null(system.HitTest(root, new Vector2(110, 110)));
        Assert.Equal(root, system.KeyboardTarget);
        system.Activate(child);
        system.Update();
        Assert.Equal(child, system.KeyboardTarget);
        system.BringToFront(popup);
        system.Update();
        Assert.Equal(child, system.KeyboardTarget);
        Assert.Throws<ArgumentException>(() => system.CreateHostedWindow(new HostedWindowDescriptor
        {
            Parent = popup,
            Bounds = new WindowRect(0, 0, 10, 10),
            Role = WindowRole.Normal,
        }));
    }

    /// <summary>Checks anchor flipping and placement updates after native resize.</summary>
    [Fact]
    public void PlacementFlipsClampsAndRecomputesAfterResize()
    {
        using var platform = new TestPlatform();
        using var system = new WindowSystem(platform);
        WindowNodeId root = system.CreateNativeWindow(new WindowDescriptor { ClientSize = new Vector2(100, 100) }, WindowRole.Normal);
        WindowNodeId popup = system.CreateHostedWindow(new HostedWindowDescriptor
        {
            Parent = root,
            Bounds = new WindowRect(0, 0, 30, 20),
            Role = WindowRole.Popup,
            Activation = WindowActivation.PreserveFocus,
        });
        system.Place(popup, new WindowPlacement(new WindowRect(90, 90, 10, 10), WindowPlacementSide.Below));
        system.Update();
        Assert.Equal(new WindowRect(70, 70, 30, 20), system.GetState(popup).Bounds);
        system.GetNativeWindow(root).Resize(new Vector2(60, 60));
        platform.PumpEvents();
        system.Update();
        Assert.Equal(new WindowRect(30, 40, 30, 20), system.GetState(popup).Bounds);
    }

    /// <summary>Checks physical child closure before parent closure and deferred native retention.</summary>
    [Fact]
    public void CloseWaitsForRetainedChildAndCompletesExactlyOnce()
    {
        using var platform = new TestPlatform();
        using var system = new WindowSystem(platform);
        WindowNodeId root = system.CreateNativeWindow(new WindowDescriptor(), WindowRole.Normal);
        IWindow parent = system.GetNativeWindow(root);
        WindowNodeId child = system.CreateNativeWindow(new WindowDescriptor { Owner = parent, Kind = WindowKind.Tool }, WindowRole.Tool, root);
        IWindow native = system.GetNativeWindow(child);
        using IDisposable retained = native.RetainNativeTarget();
        var completed = new List<WindowNodeRequestResult>();
        var removed = new List<WindowNodeId>();
        system.RequestCompleted += completed.Add;
        system.Removed += removed.Add;
        WindowNodeRequestId request = system.Close(root);
        system.Update();
        platform.PumpEvents();
        system.Update();
        Assert.Equal(WindowLifetime.Closing, native.State.Lifetime);
        Assert.Equal(WindowLifetime.Open, parent.State.Lifetime);
        Assert.Empty(completed);
        retained.Dispose();
        for (int i = 0; i < 4; i++)
        {
            platform.PumpEvents();
            system.Update();
        }

        Assert.Empty(system.Windows);
        Assert.Equal(new[] { child, root }, removed);
        Assert.Equal(request, Assert.Single(completed).Request);
        Assert.Equal(WindowRequestStatus.Applied, completed[0].Status);
    }

    /// <summary>Checks native request translation and the absence of implicit OS pumping.</summary>
    [Fact]
    public void NativeRequestCompletesOnlyAfterPlatformPump()
    {
        using var platform = new TestPlatform();
        using var system = new WindowSystem(platform);
        WindowNodeId root = system.CreateNativeWindow(new WindowDescriptor(), WindowRole.Normal);
        var results = new List<WindowNodeRequestResult>();
        system.RequestCompleted += results.Add;
        WindowNodeRequestId show = system.Show(root);
        system.Update();
        Assert.Empty(results);
        Assert.False(system.GetState(root).IsVisible);
        platform.PumpEvents();
        system.Update();
        Assert.Equal(show, Assert.Single(results).Request);
        Assert.True(system.GetState(root).IsVisible);
    }

    /// <summary>Checks that hidden temporary regions terminate while ordinary children remain.</summary>
    [Fact]
    public void HideEndsTemporaryChildrenWithoutRevivingThem()
    {
        using var platform = new TestPlatform();
        using var system = new WindowSystem(platform);
        WindowNodeId root = system.CreateNativeWindow(new WindowDescriptor(), WindowRole.Normal);
        WindowNodeId child = system.CreateHostedWindow(new HostedWindowDescriptor { Parent = root, Bounds = new WindowRect(0, 0, 50, 50) });
        WindowNodeId popup = system.CreateHostedWindow(new HostedWindowDescriptor
        {
            Parent = child,
            Bounds = new WindowRect(0, 0, 10, 10),
            Role = WindowRole.Popup,
            Activation = WindowActivation.PreserveFocus,
        });
        system.Show(root);
        system.Show(child);
        system.Show(popup);
        system.Update();
        platform.PumpEvents();
        system.Update();
        system.Hide(root);
        system.Update();
        platform.PumpEvents();
        system.Update();
        Assert.Throws<KeyNotFoundException>(() => system.GetState(popup));
        Assert.True(system.GetState(child).IsVisible);
        Assert.False(system.GetState(child).IsEffectivelyVisible);
        system.Show(root);
        system.Update();
        platform.PumpEvents();
        system.Update();
        Assert.True(system.GetState(child).IsEffectivelyVisible);
    }

    /// <summary>Checks callback isolation and rejection of reentrant mutation.</summary>
    [Fact]
    public void NotificationFailureDoesNotPreventOtherSubscribers()
    {
        using var platform = new TestPlatform();
        using var system = new WindowSystem(platform);
        WindowNodeId root = system.CreateNativeWindow(new WindowDescriptor(), WindowRole.Normal);
        int observed = 0;
        system.Changed += _ => system.Show(root);
        system.Changed += _ => observed++;
        Assert.Throws<AggregateException>(system.Update);
        Assert.Equal(1, observed);
        Assert.Single(system.Windows);
    }

    /// <summary>Checks resource-release retries without disposing borrowed natives.</summary>
    [Fact]
    public void DisposeContinuesIndependentReleaseAndCanBeRetried()
    {
        using var platform = new TestPlatform();
        var system = new WindowSystem(platform);
        WindowNodeId owned = system.CreateNativeWindow(new WindowDescriptor(), WindowRole.Normal);
        var first = (TestWindow)system.GetNativeWindow(owned);
        first.FailDispose = true;
        var borrowed = (TestWindow)platform.CreateWindow(new WindowDescriptor());
        system.Attach(borrowed, WindowRole.Normal, WindowOwnership.Borrowed);
        Assert.Throws<AggregateException>(system.Dispose);
        Assert.Single(system.Windows);
        Assert.False(borrowed.Disposed);
        first.FailDispose = false;
        system.Dispose();
        system.Dispose();
        Assert.True(first.Disposed);
    }

    /// <summary>Checks that disposal completes queued and terminating requests once.</summary>
    [Fact]
    public void DisposeSupersedesUnprocessedRequestsExactlyOnce()
    {
        using var platform = new TestPlatform();
        var system = new WindowSystem(platform);
        WindowNodeId root = system.CreateNativeWindow(new WindowDescriptor(), WindowRole.Normal);
        WindowNodeRequestId show = system.Show(root);
        WindowNodeRequestId close = system.Close(root);
        var completed = new List<WindowNodeRequestResult>();
        system.RequestCompleted += completed.Add;
        system.Dispose();
        system.Dispose();
        Assert.Equal(new[] { show, close }, completed.Select(result => result.Request));
        Assert.All(completed, result => Assert.Equal(WindowRequestStatus.Superseded, result.Status));
    }

    /// <summary>Checks user-close handling for borrowed windows.</summary>
    [Fact]
    public void UserCloseOfBorrowedWindowAsksOwnerWithoutClosing()
    {
        using var platform = new TestPlatform();
        using var system = new WindowSystem(platform);
        var native = (TestWindow)platform.CreateWindow(new WindowDescriptor());
        WindowNodeId root = system.Attach(native, WindowRole.Normal, WindowOwnership.Borrowed);
        var requests = new List<WindowNodeId>();
        system.CloseRequested += requests.Add;
        native.UserClose();
        system.Update();
        Assert.Equal(root, Assert.Single(requests));
        Assert.Equal(WindowLifetime.Open, native.State.Lifetime);
        Assert.Throws<InvalidOperationException>(() => system.Close(root));
    }

    /// <summary>Checks temporary native policies and denied hosted activation.</summary>
    [Fact]
    public void TemporaryDisplaysRequireMatchingNativePolicyAndPreserveFocus()
    {
        using var platform = new TestPlatform();
        using var system = new WindowSystem(platform);
        Assert.Throws<ArgumentException>(() => system.CreateNativeWindow(new WindowDescriptor(), WindowRole.Popup));
        WindowNodeId root = system.CreateNativeWindow(new WindowDescriptor(), WindowRole.Normal);
        WindowNodeId popup = system.CreateHostedWindow(new HostedWindowDescriptor
        {
            Parent = root,
            Bounds = new WindowRect(0, 0, 10, 10),
            Role = WindowRole.Popup,
            Activation = WindowActivation.PreserveFocus,
        });
        var results = new List<WindowNodeRequestResult>();
        system.RequestCompleted += results.Add;
        WindowNodeRequestId request = system.Activate(popup);
        system.Update();
        Assert.Equal(WindowRequestStatus.Denied, Assert.Single(results, r => r.Request == request).Status);
    }

    /// <summary>Checks that native children cannot be added after their owner starts terminating.</summary>
    [Fact]
    public void ClosingOwnerRejectsNewNativeChildren()
    {
        using var platform = new TestPlatform();
        using var system = new WindowSystem(platform);
        WindowNodeId root = system.CreateNativeWindow(new WindowDescriptor(), WindowRole.Normal);
        system.Close(root);
        Assert.Throws<ArgumentException>(() => system.CreateNativeWindow(
            new WindowDescriptor
            {
                Owner = system.GetNativeWindow(root),
                Kind = WindowKind.Tool,
            },
            WindowRole.Tool,
            root));
    }

    /// <summary>Checks focus returns past a preserve-focus ancestor to the nearest eligible ancestor.</summary>
    [Fact]
    public void RemovedTargetRestoresNearestFocusableAncestor()
    {
        using var platform = new TestPlatform();
        using var system = new WindowSystem(platform);
        WindowNodeId root = system.CreateNativeWindow(new WindowDescriptor(), WindowRole.Normal);
        system.Show(root);
        Pump(platform, system);
        system.Activate(root);
        Pump(platform, system);
        WindowNodeId focusable = system.CreateHostedWindow(new HostedWindowDescriptor { Parent = root, Bounds = new WindowRect(0, 0, 100, 100) });
        WindowNodeId preserving = system.CreateHostedWindow(new HostedWindowDescriptor
        {
            Parent = focusable,
            Bounds = new WindowRect(0, 0, 90, 90),
            Activation = WindowActivation.PreserveFocus,
        });
        WindowNodeId target = system.CreateHostedWindow(new HostedWindowDescriptor { Parent = preserving, Bounds = new WindowRect(0, 0, 80, 80) });
        system.Show(focusable);
        system.Show(preserving);
        system.Show(target);
        system.Update();
        system.Activate(target);
        system.Update();
        system.Close(target);
        system.Update();
        Assert.Equal(focusable, system.KeyboardTarget);
    }

    /// <summary>Checks externally closed parents are reaped after their final child closes.</summary>
    [Fact]
    public void ExternallyClosedParentIsReapedAfterChild()
    {
        using var platform = new TestPlatform();
        using var system = new WindowSystem(platform);
        WindowNodeId root = system.CreateNativeWindow(new WindowDescriptor(), WindowRole.Normal);
        WindowNodeId child = system.CreateNativeWindow(
            new WindowDescriptor
            {
                Owner = system.GetNativeWindow(root),
                Kind = WindowKind.Tool,
            },
            WindowRole.Tool,
            root);
        var rootWindow = (TestWindow)system.GetNativeWindow(root);
        var childWindow = (TestWindow)system.GetNativeWindow(child);
        system.Update();
        rootWindow.Change(rootWindow.State with { Lifetime = WindowLifetime.Closed });
        childWindow.Change(childWindow.State with { Lifetime = WindowLifetime.Closed });
        system.Update();
        Assert.Empty(system.Windows);
        Assert.True(rootWindow.Disposed);
        Assert.True(childWindow.Disposed);
    }

    /// <summary>Checks dependency boundaries independently of native availability.</summary>
    [Fact]
    public void ContractsDoNotDependOnRuntimeLibraries()
    {
        Assert.DoesNotContain(typeof(IWindow).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("Lumyte.", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(IPlatform).Assembly.GetReferencedAssemblies(), a => a.Name is "Lumyte.Input" or "Lumyte.Windowing");
        Assert.DoesNotContain(typeof(Lumyte.Input.Abstractions.IInputSource).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("Lumyte.", StringComparison.Ordinal));
    }

    private static void Pump(TestPlatform platform, WindowSystem system)
    {
        system.Update();
        platform.PumpEvents();
        system.Update();
    }
}
