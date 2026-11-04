using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks host validation follows required services independently of arbitrary declaration updates.</summary>
public sealed class HostValidationTests
{
    /// <summary>Checks registration, requirement changes and removal without a whole-tree change counter.</summary>
    [Fact]
    public void OnlyServiceRequirementChangesInvalidateTheHostIndex()
    {
        using var surface = new UiSurface(new ServiceWidget("first"));
        UiElement retained = Assert.Single(surface.HostValidationElements);
        ulong revision = surface.HostValidationRevision;
        surface.Rebuild(new ServiceWidget("first"));
        surface.UpdateTree();
        Assert.Same(retained, Assert.Single(surface.HostValidationElements));
        Assert.Equal(revision, surface.HostValidationRevision);
        surface.Rebuild(new ServiceWidget("second"));
        surface.UpdateTree();
        Assert.True(surface.HostValidationRevision > revision);
        revision = surface.HostValidationRevision;
        surface.Rebuild(Panel());
        surface.UpdateTree();
        Assert.Empty(surface.HostValidationElements);
        Assert.True(surface.HostValidationRevision > revision);
    }

    /// <summary>Checks a failing published-service hook cannot strand removed elements.</summary>
    /// <param name="requirements">Whether the registration or change hook fails.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedServiceHooksStillUnmountRemovedChildren(bool requirements)
    {
        var child = new TrackedWidget();
        var root = new ThrowingHostWidget(requirements, false);
        root.Children.Initialize(child);
        using var surface = new UiSurface(root);
        UiElement removed = Assert.Single(surface.RootElement.Children);
        surface.Rebuild(new ThrowingHostWidget(requirements, true));
        Assert.Throws<InvalidOperationException>(surface.UpdateTree);
        Assert.Empty(surface.RootElement.Children);
        Assert.False(removed.IsMounted);
        Assert.Null(removed.Parent);
        Assert.Equal(1, child.Unmounts);
        surface.Dispose();
        Assert.Equal(1, child.Unmounts);
    }

    private sealed class ThrowingHostWidget(bool requirements, bool fail) : Definitions.Panel
    {
        internal bool Requirements { get; } = requirements;

        internal bool Fail { get; } = fail;

        protected override UiElement CreateElement() => new ThrowingHostElement(this);
    }

    private sealed class ThrowingHostElement(ThrowingHostWidget widget) : UiElement<ThrowingHostWidget>(widget)
    {
        protected override bool RequiresHostValidation => Widget is { Requirements: true, Fail: true } ? throw new InvalidOperationException("registration") : true;

        protected override bool HostServicesChanged(Widget previous, Widget next) => ((ThrowingHostWidget)next).Fail ? throw new InvalidOperationException("requirements") : false;
    }

    private sealed class TrackedWidget : Widget
    {
        internal int Unmounts { get; set; }

        protected override UiElement CreateElement() => new TrackedElement(this);
    }

    private sealed class TrackedElement(TrackedWidget widget) : UiElement<TrackedWidget>(widget)
    {
        protected override void OnUnmounted() => Widget.Unmounts++;
    }

    private sealed class ServiceWidget(string requirement) : Widget
    {
        internal string Requirement { get; } = requirement;

        protected override UiElement CreateElement() => new ServiceElement(this);
    }

    private sealed class ServiceElement(ServiceWidget widget) : UiElement<ServiceWidget>(widget)
    {
        protected override bool RequiresHostValidation => true;

        protected override bool HostServicesChanged(Widget previous, Widget next) => ((ServiceWidget)previous).Requirement != ((ServiceWidget)next).Requirement;
    }
}
