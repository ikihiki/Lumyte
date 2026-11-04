using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Prepares animation leases and adopts them on persistent elements.</summary>
public sealed partial class UiSurface
{
    private void PrepareAnimationUpdates(RebuildPlan plan)
    {
        foreach (ElementUpdate update in plan.Updates)
        {
            PrepareAnimationUpdate(update);
        }
    }

    private void PrepareAnimationUpdate(ElementUpdate update)
    {
        if (update.Animations is not null)
        {
            return;
        }

        if (ReferenceEquals(update.Previous, update.Next)
            || (update.Element.Animations is null && update.Next.AnimationArguments is not { Count: > 0 }))
        {
            return;
        }

        update.Animations = new(this, update.Element, update.Next);
        if (update.Next.NeedsAnimationRefresh)
        {
            Widget original = update.Next;
            Widget refreshed = original.Recreate(new(update.Element, original))
                ?? throw new InvalidOperationException("Animation arguments require a reproducible declaration.");
            if (refreshed.GetType() != original.GetType() || refreshed.Key != original.Key
                || refreshed.ChildCollections.Count != original.ChildCollections.Count || !AnimationLease.SameArguments(original.AnimationArguments, refreshed.AnimationArguments))
            {
                throw new InvalidOperationException("Animation reconstruction must preserve the prepared component and projection identities.");
            }

            refreshed.ProvidedContexts ??= original.ProvidedContexts;
            refreshed.Construction ??= original.Construction;
            refreshed.ConstructionReads ??= original.ConstructionReads;
            update.Next = refreshed;
        }
    }

    internal sealed class AnimationLease : IDisposable
    {
        private readonly UiSurface _surface;
        private readonly UiElement _element;
        private readonly IReadOnlyDictionary<object, IUiAnimationProjection> _arguments;
        private readonly List<IUiAnimationProjection> _retained = [];
        private bool _disposed;

        internal AnimationLease(UiSurface surface, UiElement element, IReadOnlyDictionary<object, IUiAnimationProjection> arguments)
        {
            _surface = surface;
            _element = element;
            _arguments = arguments;
            try
            {
                foreach (IUiAnimationProjection projection in arguments.Values.Distinct())
                {
                    projection.Producer.Retain(surface, projection);
                    surface._animationProducers.Add(projection.Producer);
                    _retained.Add(projection);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal IReadOnlyList<IUiAnimationProjection> Projections => _retained;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (ReferenceEquals(_element.Animations, this))
            {
                _surface._animatedElements.Remove(_element);
            }

            foreach (IUiAnimationProjection projection in _retained)
            {
                projection.Producer.Release(projection);
                if (!projection.Producer.IsConnected)
                {
                    _surface._animationProducers.Remove(projection.Producer);
                }
            }

            _retained.Clear();
        }

        internal static bool SameArguments(IReadOnlyDictionary<object, IUiAnimationProjection>? before, IReadOnlyDictionary<object, IUiAnimationProjection>? after)
        {
            if ((before?.Count ?? 0) != (after?.Count ?? 0))
            {
                return false;
            }

            return before is null || before.All(pair => after is not null && after.TryGetValue(pair.Key, out IUiAnimationProjection? current) && ReferenceEquals(current, pair.Value));
        }

        internal bool CanReuse(Widget next) => SameArguments(_arguments, next.AnimationArguments);
    }

    private sealed class AnimationUpdate : IDisposable
    {
        private readonly UiSurface _surface;
        private readonly UiElement _element;
        private readonly AnimationLease? _lease;
        private readonly bool _borrowed;
        private bool _committed;

        internal AnimationUpdate(UiSurface surface, UiElement element, Widget next)
        {
            _surface = surface;
            _element = element;
            if (next.AnimationArguments is { Count: > 0 } arguments)
            {
                if (element.Animations is { } current && current.CanReuse(next))
                {
                    _lease = current;
                    _borrowed = true;
                }
                else
                {
                    foreach ((object key, IUiAnimationProjection projection) in arguments)
                    {
                        if (element.IsMounted && !projection.Producer.IsConnected && element.Widget.AnimationArguments is { } previous
                            && previous.TryGetValue(key, out IUiAnimationProjection? before))
                        {
                            projection.Producer.Inherit(before.Producer);
                        }
                    }

                    _lease = new(surface, element, arguments);
                }
            }
        }

        public void Dispose()
        {
            if (!_committed && !_borrowed)
            {
                _lease?.Dispose();
            }
        }

        internal void Commit()
        {
            AnimationLease? previous = _element.Animations;
            _element.Animations = _lease;
            _committed = true;
            if (_lease is not null)
            {
                _surface._animatedElements[_element] = _lease;
            }
            else
            {
                _surface._animatedElements.Remove(_element);
            }

            if (!ReferenceEquals(previous, _lease))
            {
                previous?.Dispose();
            }
        }
    }
}
