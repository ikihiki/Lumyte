using System.Reflection;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Checks immutable watch snapshots and incremental ownership without repeated snapshot allocation.</summary>
public sealed class ResourceWatchTargetsTests
{
    /// <summary>Shared build references preserve snapshots until the last owner of an identifier leaves.</summary>
    [Fact]
    public void SharedBuildReferencesPreserveImmutableSnapshots()
    {
        (IResourceWatchTargets targets, Action<ResourceId> track, Action<IEnumerable<ResourceId>> completeBuild) = CreateTargets();
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
        completeBuild([first]);
        Assert.Same(initial, targets.Resources);

        track(third);
        IReadOnlySet<ResourceId> expanded = targets.Resources;
        Assert.True(expanded.SetEquals([first, second, third]));
        completeBuild([first, second]);
        IReadOnlySet<ResourceId> remaining = targets.Resources;
        Assert.True(remaining.SetEquals([third]));
        Assert.True(initial.SetEquals([first, second]));
        Assert.True(expanded.SetEquals([first, second, third]));

        completeBuild([third]);
        Assert.Empty(targets.Resources);
        Assert.True(remaining.SetEquals([third]));
    }

    /// <summary>Adding already-sized tracking entries does not materialize a frozen snapshot per identifier.</summary>
    [Fact]
    public void TrackingBurstDoesNotAllocateSnapshotsBeforeTheyAreRead()
    {
        (IResourceWatchTargets targets, Action<ResourceId> track, Action<IEnumerable<ResourceId>> completeBuild) = CreateTargets();
        ResourceId[] ids = Enumerable.Range(0, 256).Select(index => new ResourceId($"input/{index}")).ToArray();
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (ResourceId id in ids)
            {
                track(id);
            }

            completeBuild(ids);
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

    /// <summary>Balanced additional owners of existing identifiers reuse their snapshot with bounded bookkeeping allocation.</summary>
    [Fact]
    public void BalancedBuildReferencesDoNotRebuildFrozenSnapshots()
    {
        (IResourceWatchTargets targets, Action<ResourceId> track, Action<IEnumerable<ResourceId>> completeBuild) = CreateTargets();
        ResourceId[] ids = Enumerable.Range(0, 256).Select(index => new ResourceId($"input/{index}")).ToArray();
        foreach (ResourceId id in ids)
        {
            track(id);
        }

        IReadOnlySet<ResourceId> snapshot = targets.Resources;
        for (int index = 0; index < 10; index++)
        {
            AddAndCompleteReferences();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++)
        {
            AddAndCompleteReferences();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Same(snapshot, targets.Resources);
        Assert.InRange(allocated, 0, 6400);
        completeBuild(ids);
        Assert.Empty(targets.Resources);
        Assert.True(snapshot.SetEquals(ids));

        void AddAndCompleteReferences()
        {
            foreach (ResourceId id in ids)
            {
                track(id);
            }

            completeBuild(ids);
        }
    }

    private static (IResourceWatchTargets Targets, Action<ResourceId> Track, Action<IEnumerable<ResourceId>> CompleteBuild) CreateTargets()
    {
        Type type = typeof(ResourceManager).Assembly.GetType("Lumyte.Resources.ResourceWatchTargets", throwOnError: true)!;
        object instance = Activator.CreateInstance(type, nonPublic: true)!;
        Action<ResourceId> track = type.GetMethod("Track", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<ResourceId>>(instance);
        Action<IEnumerable<ResourceId>> completeBuild = type.GetMethod("CompleteBuild", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<IEnumerable<ResourceId>>>(instance);
        return ((IResourceWatchTargets)instance, track, completeBuild);
    }
}
