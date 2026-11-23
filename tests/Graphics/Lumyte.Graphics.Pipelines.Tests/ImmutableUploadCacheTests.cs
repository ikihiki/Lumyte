using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Pipelines.Tests;

/// <summary>Checks real native immutable owner identity, admission and publication isolation.</summary>
public sealed class ImmutableUploadCacheTests
{
    /// <summary>Unpublished identical bytes cannot be borrowed by another preparation.</summary>
    [NativeFact]
    [Trait("Execution", "Gpu")]
    public void PendingCopiesAreIsolatedAndPublishedCopiesShareOnlyExactNodeSchemaBytes()
    {
        using var device = VulkanDevice.Create();
        using var cache = new ImmutableUploadCache(new(1024, 8, 64));
        using ImmutableUploadCache.Lease<uint> first = cache.Create<uint, Words>(1, [1u, 2u], device);
        Assert.Null(cache.Find<uint, Words>(1, [1u, 2u]));
        first.Publish();
        using ImmutableUploadCache.Lease<uint> same = cache.Find<uint, Words>(1, [1u, 2u])!;
        Assert.Same(first.Buffer, same.Buffer);
        Assert.Null(cache.Find<uint, Words>(2, [1u, 2u]));
        Assert.Null(cache.Find<uint, OtherWords>(1, [1u, 2u]));
        Assert.Null(cache.Find<uint, Words>(1, [1u, 3u]));
        using var otherBuild = new ImmutableUploadCache(new(1024, 8, 64));
        Assert.Null(otherBuild.Find<uint, Words>(1, [1u, 2u]));
        Assert.Equal(BufferUsage.CopyDestination | BufferUsage.CopySource | BufferUsage.ShaderRead, first.Buffer.Usage);
    }

    /// <summary>Active and unpublished versions stay charged; failure cannot evict them to satisfy a new allocation.</summary>
    [NativeFact]
    [Trait("Execution", "Gpu")]
    public void FiniteAdmissionPreservesActiveOwnersAndUnsubmittedReleaseDoesNotPublish()
    {
        using var device = VulkanDevice.Create();
        using var cache = new ImmutableUploadCache(new(8, 1, 64));
        ImmutableUploadCache.Lease<uint> first = cache.Create<uint, Words>(1, [1u, 2u], device);
        Assert.Throws<InvalidOperationException>(() => cache.Create<uint, Words>(1, [3u, 4u], device));
        Assert.Single(cache.Snapshot().Owners);
        Assert.Equal(8, cache.Snapshot().Bytes);
        first.Dispose();
        Assert.Empty(cache.Snapshot().Owners);
        Assert.Null(cache.Find<uint, Words>(1, [1u, 2u]));
        using ImmutableUploadCache.Lease<uint> replacement = cache.Create<uint, Words>(1, [3u, 4u], device);
        replacement.Publish();
        replacement.Dispose();
        using ImmutableUploadCache.Lease<uint> next = cache.Create<uint, Words>(1, [5u, 6u], device);
        Assert.Equal(3, cache.Snapshot().Created);
        Assert.Null(cache.Find<uint, Words>(1, [3u, 4u]));
    }

    /// <summary>Zero owner capacity refuses before any native owner is created.</summary>
    [NativeFact]
    [Trait("Execution", "Gpu")]
    public void ZeroCapacityHasNoNativeAllocationOrUnboundedRetry()
    {
        using var device = VulkanDevice.Create();
        using var cache = new ImmutableUploadCache(new(0, 0));
        Assert.Throws<InvalidOperationException>(() => cache.Create<uint, Words>(1, [1u], device));
        Assert.Equal(0, cache.Snapshot().Created);
        Assert.Empty(cache.Snapshot().Owners);
    }

    /// <summary>Whole-upload misses are admitted together and required cache hits cannot be evicted to disguise excess demand.</summary>
    [NativeFact]
    [Trait("Execution", "Gpu")]
    public void AggregateAdmissionRejectsBeforeNativeAllocationAndProtectsMatchingInputs()
    {
        using var device = VulkanDevice.Create();
        using var cache = new ImmutableUploadCache(new(8, 2, 64));
        Assert.Throws<InvalidOperationException>(() => cache.Admit<uint, Words>(1, [1u, 2u, 3u], 1));
        Assert.Equal(0, cache.Snapshot().Created);
        ImmutableUploadCache.Lease<uint> first = cache.Create<uint, Words>(1, [1u], device);
        first.Publish();
        first.Dispose();
        ImmutableUploadCache.Lease<uint> second = cache.Create<uint, Words>(1, [2u], device);
        second.Publish();
        second.Dispose();
        cache.Admit<uint, Words>(1, [1u, 3u], 1);
        Assert.Null(cache.Find<uint, Words>(1, [2u]));
        using ImmutableUploadCache.Lease<uint> retained = cache.Find<uint, Words>(1, [1u])!;
        Assert.NotNull(retained);
        Assert.Throws<InvalidOperationException>(() => cache.Admit<uint, Words>(1, [1u, 3u, 4u], 1));
        Assert.Equal(2, cache.Snapshot().Created);
    }

    private sealed class Words : ISchema
    {
        public static SchemaDescriptor Descriptor { get; } = new(1, typeof(uint), sizeof(uint), new Dictionary<string, object>());
    }

    private sealed class OtherWords : ISchema
    {
        public static SchemaDescriptor Descriptor { get; } = new(1, typeof(uint), sizeof(uint), new Dictionary<string, object>());
    }
}
