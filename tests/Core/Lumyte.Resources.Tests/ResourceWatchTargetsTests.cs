using System.Reflection;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Checks immutable watch snapshots and incremental tracking without repeated snapshot allocation.</summary>
public sealed class ResourceWatchTargetsTests
{
    /// <summary>Tracking and replacement preserve old snapshots and reuse unchanged snapshots.</summary>
    [Fact]
    public void SnapshotsRemainImmutableAndUnchangedSetsAreReused()
    {
        (IResourceWatchTargets targets, Action<ResourceId> track, Action<IEnumerable<ResourceId>> set) = CreateTargets();
        ResourceId first = new("first");
        ResourceId second = new("second");
        ResourceId third = new("third");
        IReadOnlySet<ResourceId> empty = targets.Resources;

        track(first);
        track(second);
        IReadOnlySet<ResourceId> initial = targets.Resources;
        Assert.Empty(empty);
        Assert.True(initial.SetEquals([first, second]));

        track(first);
        Assert.Same(initial, targets.Resources);
        set([second, first, second]);
        Assert.Same(initial, targets.Resources);

        set([third]);
        IReadOnlySet<ResourceId> replacement = targets.Resources;
        Assert.True(replacement.SetEquals([third]));
        Assert.True(initial.SetEquals([first, second]));
        set([]);
        Assert.Empty(targets.Resources);
        Assert.True(replacement.SetEquals([third]));
    }

    /// <summary>Adding already-sized tracking entries does not materialize a frozen snapshot per identifier.</summary>
    [Fact]
    public void TrackingBurstDoesNotAllocateSnapshotsBeforeTheyAreRead()
    {
        (IResourceWatchTargets targets, Action<ResourceId> track, Action<IEnumerable<ResourceId>> set) = CreateTargets();
        ResourceId[] ids = Enumerable.Range(0, 256).Select(index => new ResourceId($"input/{index}")).ToArray();
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (ResourceId id in ids)
            {
                track(id);
            }

            set([]);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (ResourceId id in ids)
        {
            track(id);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.True(targets.Resources.SetEquals(ids));
    }

    /// <summary>Repeated publication of an unchanged set retains its immutable snapshot.</summary>
    [Fact]
    public void RepeatedSetDoesNotRebuildFrozenSnapshots()
    {
        (IResourceWatchTargets targets, Action<ResourceId> _, Action<IEnumerable<ResourceId>> set) = CreateTargets();
        ResourceId[] ids = Enumerable.Range(0, 256).Select(index => new ResourceId($"input/{index}")).ToArray();
        set(ids);
        IReadOnlySet<ResourceId> snapshot = targets.Resources;
        for (int index = 0; index < 10; index++)
        {
            set(ids);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++)
        {
            set(ids);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Same(snapshot, targets.Resources);
        Assert.InRange(allocated, 0, 6400);
    }

    private static (IResourceWatchTargets Targets, Action<ResourceId> Track, Action<IEnumerable<ResourceId>> Set) CreateTargets()
    {
        Type type = typeof(ResourceManager).Assembly.GetType("Lumyte.Resources.ResourceWatchTargets", throwOnError: true)!;
        object instance = Activator.CreateInstance(type, nonPublic: true)!;
        Action<ResourceId> track = type.GetMethod("Track", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<ResourceId>>(instance);
        Action<IEnumerable<ResourceId>> set = type.GetMethod("Set", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<IEnumerable<ResourceId>>>(instance);
        return ((IResourceWatchTargets)instance, track, set);
    }
}
