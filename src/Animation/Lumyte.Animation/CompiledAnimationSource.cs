using Lumyte.Core.Time;

namespace Lumyte.Animation;

internal sealed class CompiledAnimationSource<T> : IAnimationSource<T>
{
    private readonly IAnimationSource<T> _source;
    private readonly HashSet<object> _sharedSources;
    private readonly object _gate = new();
    private AnimationSourceEvaluationContext? _available;

    internal CompiledAnimationSource(IAnimationSource<T> source, IReadOnlyCollection<object> sharedSources)
    {
        _source = source;
        _sharedSources = new(sharedSources, ReferenceEqualityComparer.Instance);
        _available = new(_sharedSources);
    }

    /// <inheritdoc />
    public Duration Duration => _source.Duration;

    /// <inheritdoc />
    public T Sample(Duration time)
    {
        AnimationSourceEvaluationContext context = Rent();
        try
        {
            return context.Evaluate(_source, time);
        }
        finally
        {
            context.Clear();
            lock (_gate)
            {
                context.Next = _available;
                _available = context;
            }
        }
    }

    private AnimationSourceEvaluationContext Rent()
    {
        lock (_gate)
        {
            if (_available is { } context)
            {
                _available = context.Next;
                context.Next = null;
                return context;
            }
        }

        return new(_sharedSources);
    }
}
