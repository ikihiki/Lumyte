using System.Runtime.ExceptionServices;
using Lumyte.Core.Time;
using Lumyte.Signals;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private readonly HashSet<UiAnimationProducer> _animationProducers = [];
    private readonly HashSet<UiAnimationProducer> _visibleAnimationProducers = [];
    private Widget? _animationTreeRoot;
    private (Widget Widget, UiAnimationProducer Producer)[] _animationParticipants = [];
    private IMonotonicClock _animationClock = new SystemMonotonicClock();
    private bool _animationUpdatesEnabled = true;
    private bool _advancingAnimations;
    private bool _publishingAnimations;
    private Exception? _animationFailure;

    /// <summary>Initializes a new instance of the <see cref="UiSurface"/> class.</summary>
    /// <param name="root">The disconnected declaration tree.</param>
    /// <param name="dispatcher">The borrowed owner dispatcher.</param>
    /// <param name="animationClock">The borrowed monotonic animation clock.</param>
    public UiSurface(Widget root, IUiDispatcher dispatcher, IMonotonicClock animationClock)
        : this(RequireAnimationClock(root, animationClock), dispatcher)
    {
        _animationClock = animationClock;
    }

    /// <summary>Gets a value indicating whether any visible producer needs continuous time progression.</summary>
    public bool HasRunningAnimations => !_isDisposed && _animationUpdatesEnabled && _animationFailure is null && HasVisibleAnimation(true);

    /// <summary>Gets a value indicating whether visible animations have a one-shot target or control update.</summary>
    public bool HasPendingAnimationUpdates => !_isDisposed && _animationUpdatesEnabled && _animationFailure is null && HasVisibleAnimation(false);

    private ulong AnimationRevision => _animationProducers.Count == 0 ? 0 : _animationProducers.Max(producer => producer.Revision);

    /// <summary>Freezes or resumes animation time without stopping independent model values.</summary>
    /// <param name="enabled">Whether the owning host can update and present this surface.</param>
    public void SetAnimationUpdatesEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_animationUpdatesEnabled == enabled)
        {
            return;
        }

        _animationUpdatesEnabled = enabled;
        foreach (UiAnimationProducer producer in _animationProducers)
        {
            producer.Freeze();
        }
    }

    /// <summary>Evaluates one animation phase; layout, recording and getters never advance time.</summary>
    public void AdvanceAnimations()
    {
        ThrowIfDisposed();
        if (_animationFailure is { } failed)
        {
            ExceptionDispatchInfo.Capture(failed).Throw();
        }

        if (_advancingAnimations || _dispatchDepth != 0 || _layoutPass is not null || _recordDepth != 0 || _rebuildDepth != 0)
        {
            throw new InvalidOperationException("Advance animations once at the owner's update boundary.");
        }

        if (!_animationUpdatesEnabled)
        {
            return;
        }

        _advancingAnimations = true;
        try
        {
            UpdateTree();
            if (_isDisposed || _animationProducers.Count == 0)
            {
                return;
            }

            HashSet<UiAnimationProducer> visible = _visibleAnimationProducers;
            visible.Clear();
            foreach ((Widget widget, UiAnimationProducer producer) in AnimationParticipants())
            {
                if (IsAnimationVisible(widget))
                {
                    visible.Add(producer);
                }
            }

            bool needsEvaluation = false;
            foreach (UiAnimationProducer producer in _animationProducers)
            {
                if (!visible.Contains(producer))
                {
                    producer.Freeze();
                }
                else
                {
                    needsEvaluation |= producer.IsRunning || producer.HasPending;
                }
            }

            if (!needsEvaluation)
            {
                return;
            }

            TimePoint now = _animationClock.Now;
            UiAnimationProducer[] snapshot = _animationProducers.ToArray();
            var callbacks = new List<(UiAnimationProducer Producer, ulong Generation, Action Callback)>();
            _publishingAnimations = true;
            try
            {
                foreach (UiAnimationProducer producer in snapshot)
                {
                    producer.Stage(now, visible.Contains(producer));
                }

                PublishAnimationValues(snapshot, visible);

                foreach (UiAnimationProducer producer in snapshot)
                {
                    foreach (Action callback in producer.TakeNotifications())
                    {
                        callbacks.Add((producer, producer.Generation, callback));
                    }
                }
            }
            finally
            {
                _publishingAnimations = false;
            }

            UpdateTree();
            foreach ((UiAnimationProducer producer, ulong generation, Action callback) in callbacks)
            {
                if (_isDisposed || !_animationUpdatesEnabled)
                {
                    break;
                }

                if (producer.IsConnected && _animationProducers.Contains(producer) && producer.Generation == generation)
                {
                    callback();
                    if (!_isDisposed)
                    {
                        UpdateTree();
                    }
                }
            }
        }
        catch (Exception error)
        {
            _animationFailure = error;
            _signalFailure = new UiStateUpdateException(error);
            _animationUpdatesEnabled = false;
            _signals?.Dispose();
            _pendingRebuild?.DisposeSignals();
            throw;
        }
        finally
        {
            _advancingAnimations = false;
        }
    }

    internal void SetAnimationClock(IMonotonicClock clock) => _animationClock = clock;

    private static Widget RequireAnimationClock(Widget root, IMonotonicClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        return root;
    }

    private static void PublishAnimationValues(UiAnimationProducer[] snapshot, HashSet<UiAnimationProducer> visible)
    {
        Signal.Batch(() =>
        {
            foreach (UiAnimationProducer producer in snapshot)
            {
                if (visible.Contains(producer))
                {
                    producer.Publish();
                }
            }
        });
    }

    private static bool IsAnimationVisible(Widget widget)
    {
        for (Widget? current = widget; current is not null; current = current.Parent)
        {
            if (!current.IsVisible)
            {
                return false;
            }
        }

        return true;
    }

    private bool HasVisibleAnimation(bool running)
    {
        if (_animationProducers.Count == 0)
        {
            return false;
        }

        foreach ((Widget widget, UiAnimationProducer producer) in AnimationParticipants())
        {
            if ((running ? producer.IsRunning : producer.HasPending) && IsAnimationVisible(widget))
            {
                return true;
            }
        }

        return false;
    }

    private (Widget Widget, UiAnimationProducer Producer)[] AnimationParticipants()
    {
        if (!ReferenceEquals(_animationTreeRoot, _root))
        {
            _animationParticipants = _root.CollectSubtree()
                .SelectMany(widget => (widget.AnimationArguments?.Values ?? []).Select(projection => (widget, projection.Producer))).ToArray();
            _animationTreeRoot = _root;
        }

        return _animationParticipants;
    }

    private AnimationLease PrepareAnimationArguments(Widget root)
    {
        if (!ReferenceEquals(root, _root))
        {
            var matches = new Dictionary<Widget, Widget>(ReferenceEqualityComparer.Instance);
            RebuildPlan.Match(_root, root, matches);
            foreach ((Widget previous, Widget current) in matches)
            {
                if (previous.AnimationArguments is not { } before || current.AnimationArguments is not { } after)
                {
                    continue;
                }

                foreach ((object key, IUiAnimationProjection projection) in after)
                {
                    if (!projection.Producer.IsConnected && before.TryGetValue(key, out IUiAnimationProjection? prior))
                    {
                        projection.Producer.Inherit(prior.Producer);
                    }
                }
            }
        }

        return new(this, root.CollectSubtree().SelectMany(widget => widget.AnimationArguments?.Values ?? []).Distinct().ToArray());
    }

    private sealed class AnimationLease : IDisposable
    {
        private readonly UiSurface _surface;
        private readonly List<IUiAnimationProjection> _retained = [];
        private bool _disposed;

        internal AnimationLease(UiSurface surface, IUiAnimationProjection[] projections)
        {
            _surface = surface;
            try
            {
                foreach (IUiAnimationProjection projection in projections)
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

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
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
    }
}
