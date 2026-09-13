namespace Lumyte.Graphics.Native.RenderGraph.Tests;

public sealed class NativePreparationCacheTests
{
    [Fact]
    public async Task UnchangedKeysReuseCompletedPreparation()
    {
        var cache = new NativePassPreparationCache<int, object>();
        var first = await cache.GetOrCreateAsync(1, (_, _) => new(new object()));

        var second = await cache.GetOrCreateAsync(1, (_, _) => throw new InvalidOperationException("must not prepare again"));

        Assert.Same(first, second);
    }

    [Fact]
    public async Task LeastRecentlyUsedPreparationsAreEvicted()
    {
        var cache = new NativePassPreparationCache<int, object>(2);
        var one = await cache.GetOrCreateAsync(1, NewValue);
        var two = await cache.GetOrCreateAsync(2, NewValue);
        await cache.GetOrCreateAsync(1, NewValue);

        await cache.GetOrCreateAsync(3, NewValue);
        var rebuilt = await cache.GetOrCreateAsync(2, NewValue);

        Assert.NotSame(two, rebuilt);
        Assert.Equal(2, cache.Count);
        GC.KeepAlive(one);
    }

    [Fact]
    public async Task FailedPreparationCanBeRetried()
    {
        var cache = new NativePassPreparationCache<int, object>();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await cache.GetOrCreateAsync(1, (_, _) => throw new InvalidOperationException("failed")));

        var result = await cache.GetOrCreateAsync(1, NewValue);

        Assert.NotNull(result); Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task RemovingOneKeyLeavesTheOtherPreparationAvailable()
    {
        var cache = new NativePassPreparationCache<int, object>();
        await cache.GetOrCreateAsync(1, NewValue);
        var other = await cache.GetOrCreateAsync(2, NewValue);

        Assert.True(cache.Remove(1));

        Assert.Same(other, await cache.GetOrCreateAsync(2, NewValue));
    }

    private static ValueTask<object> NewValue(int key, CancellationToken cancellationToken) => new(new object());
}
