using System.Runtime.CompilerServices;
using Lumyte.Core.Time;

namespace Lumyte.Animation;

internal sealed class AnimationSourceEvaluationContext(HashSet<object> sharedSources)
{
    private readonly Dictionary<CacheKey, ICache> _caches = new(CacheKeyComparer.Instance);

    /// <summary>Releases cached values without discarding reusable typed storage.</summary>
    private interface ICache
    {
        /// <summary>Releases values held by this cache.</summary>
        void Clear();
    }

    internal AnimationSourceEvaluationContext? Next { get; set; }

    internal T Evaluate<T>(IAnimationSource<T> source, Duration time)
    {
        if (!sharedSources.Contains(source))
        {
            return Sample(source, time);
        }

        var key = new CacheKey(source, typeof(T));
        if (!_caches.TryGetValue(key, out ICache? entry))
        {
            entry = new Cache<T>();
            _caches.Add(key, entry);
        }

        var cache = (Cache<T>)entry;
        if (cache.Values.TryGetValue(time.Ticks, out T? value))
        {
            return value;
        }

        T result = Sample(source, time);
        cache.Values.Add(time.Ticks, result);
        return result;
    }

    internal void Clear()
    {
        foreach (ICache cache in _caches.Values)
        {
            cache.Clear();
        }
    }

    private T Sample<T>(IAnimationSource<T> source, Duration time) => source is IContextualAnimationSource<T> contextual ? contextual.Sample(time, this) : source.Sample(time);

    private readonly record struct CacheKey(object Source, Type ValueType);

    private sealed class Cache<T> : ICache
    {
        internal Dictionary<long, T> Values { get; } = [];

        /// <summary>Releases sampled values while retaining dictionary storage.</summary>
        public void Clear() => Values.Clear();
    }

    private sealed class CacheKeyComparer : IEqualityComparer<CacheKey>
    {
        internal static CacheKeyComparer Instance { get; } = new();

        /// <inheritdoc />
        public bool Equals(CacheKey x, CacheKey y) => ReferenceEquals(x.Source, y.Source) && x.ValueType == y.ValueType;

        /// <inheritdoc />
        public int GetHashCode(CacheKey obj) => HashCode.Combine(RuntimeHelpers.GetHashCode(obj.Source), obj.ValueType);
    }
}
