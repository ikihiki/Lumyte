using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Checks resource notifications begin after publication and cleanup survives callback failures.</summary>
public sealed class ElementLifetimeTests
{
    /// <summary>Checks a failed mount releases all published instances, including unnotified siblings.</summary>
    [Fact]
    public void FailedMountCleansTheCompletePublishedTree()
    {
        var instances = new List<LifetimeElement>();
        Definitions.Panel root = Panel()[new LifetimeWidget(instances) { RejectMount = true }, new LifetimeWidget(instances)];
        Assert.Throws<InvalidOperationException>(() => new UiSurface(root));
        Assert.Equal(2, instances.Count);
        Assert.All(instances, element =>
        {
            Assert.False(element.IsMounted);
            Assert.Equal(1, element.Unmounts);
        });
        Assert.Equal(1, instances.Sum(element => element.Mounts));
    }

    /// <summary>Checks a throwing cleanup does not leave other instances mounted.</summary>
    [Fact]
    public void FailedUnmountStillReleasesOtherInstances()
    {
        var instances = new List<LifetimeElement>();
        using var surface = new UiSurface(Panel()[new LifetimeWidget(instances) { RejectUnmount = true }, new LifetimeWidget(instances)]);
        Assert.Throws<InvalidOperationException>(surface.Dispose);
        Assert.All(instances, element =>
        {
            Assert.False(element.IsMounted);
            Assert.Equal(1, element.Unmounts);
        });
    }

    /// <summary>Checks disposal from one mount cannot start resources on a subsequent sibling.</summary>
    [Fact]
    public void DisposalDuringMountSkipsRemainingStarts()
    {
        var instances = new List<LifetimeElement>();
        using var surface = new UiSurface(Panel()[new LifetimeWidget(instances) { DisposeAtMount = true }, new LifetimeWidget(instances)]);
        Assert.Equal(1, instances.Sum(element => element.Mounts));
        Assert.All(instances, element =>
        {
            Assert.False(element.IsMounted);
            Assert.Equal(1, element.Unmounts);
        });
    }

    private sealed class LifetimeWidget(List<LifetimeElement> instances) : Definitions.Widget
    {
        internal bool RejectMount { get; init; }

        internal bool RejectUnmount { get; init; }

        internal bool DisposeAtMount { get; init; }

        protected override UiElement CreateElement()
        {
            var element = new LifetimeElement(this);
            instances.Add(element);
            return element;
        }
    }

    private sealed class LifetimeElement(LifetimeWidget widget) : UiElement<LifetimeWidget>(widget)
    {
        internal int Mounts { get; private set; }

        internal int Unmounts { get; private set; }

        protected override void OnMounted()
        {
            Mounts++;
            if (Widget.RejectMount)
            {
                throw new InvalidOperationException("Failed start.");
            }

            if (Widget.DisposeAtMount)
            {
                Surface!.Dispose();
            }
        }

        protected override void OnUnmounted()
        {
            Unmounts++;
            if (Widget.RejectUnmount)
            {
                throw new InvalidOperationException("Failed cleanup.");
            }
        }
    }
}
